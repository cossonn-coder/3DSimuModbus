"""Chargement et resolution du modele pivot Modbus.

Ce module lit `pivot/machine_carrousel.json` (contrat central du projet) et resout,
pour chaque signal, son **adresse Modbus absolue** a partir de la base de sa zone
(`base_mw`) et de son offset relatif (`word`).

Pourquoi ce module existe :
    Le pivot exprime les adresses de facon RELATIVE (`{zone, word, bit}`) pour qu'on
    puisse recaler une zone entiere (changer `base_mw`) sans reconstruire le mapping.
    Les tests, eux, ont besoin de l'adresse ABSOLUE `%MW` a interroger. On resout ici,
    une fois, plutot que de coder une adresse en dur dans chaque test.

Convention (figee en Phase 0) :
    - `%MWn` cote Control Expert = holding register d'adresse protocole `n`. Pas de
      decalage (+1, 4xxxx).
    - Zone `cmd` (PLC -> sim, ecrite en FC16) et zone `ret` (sim -> PLC, lue en FC3),
      chacune avec sa base `base_mw` et sa taille `size_words`.
    - Signaux TOR : `{zone, word, bit}` (bit 0..15), avec un `tag` optionnel (S11...).
    - Le heartbeat est un compteur mot entier (`modbus.heartbeat`, zone `ret`, word).
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional


DEFAULT_PIVOT_PATH = Path(__file__).resolve().parents[1] / "pivot" / "machine_carrousel.json"


class PivotError(ValueError):
    """Pivot malforme ou incoherent. On echoue clairement (code defensif)."""


@dataclass(frozen=True)
class Signal:
    """Un signal resolu, pret a etre interroge sur le bus."""

    component_id: str
    name: str               # cle du signal dans le composant (cmd_run, ret_extended...)
    tag: Optional[str]      # repere schema (S11, KM1_AUX...) si defini
    zone: str               # "cmd" ou "ret"
    is_tor: bool            # True = bit dans un mot ; False = mot entier (compteur)
    word_rel: int           # offset mot relatif a la base de la zone
    bit: Optional[int]      # position 0..15 si TOR, sinon None
    abs_word: int           # adresse %MW absolue resolue

    def read_bit(self, word_value: int) -> bool:
        if not self.is_tor or self.bit is None:
            raise PivotError(f"{self.name} n'est pas un signal TOR")
        return bool((word_value >> self.bit) & 0x1)


@dataclass(frozen=True)
class ZoneLayout:
    name: str
    base: int
    size_words: int
    description: str


@dataclass(frozen=True)
class Component:
    id: str
    tag: str
    type: str
    signals: dict[str, Signal] = field(default_factory=dict)


@dataclass(frozen=True)
class Mapping:
    """Vue resolue du pivot."""

    zones: dict[str, ZoneLayout]
    components: dict[str, Component]        # cle = component id
    by_tag: dict[str, Component]            # cle = tag composant (KM1, YV1...)
    signals_by_tag: dict[str, Signal]       # cle = tag signal (S11, KM1_AUX...)
    all_signals: list[Signal]
    heartbeat: Signal

    def zone(self, name: str) -> ZoneLayout:
        if name not in self.zones:
            raise PivotError(f"Zone inconnue : {name!r}")
        return self.zones[name]

    def component(self, key: str) -> Component:
        if key in self.components:
            return self.components[key]
        if key in self.by_tag:
            return self.by_tag[key]
        raise PivotError(f"Composant inconnu : {key!r}")

    def signal(self, component_key: str, signal_name: str) -> Signal:
        comp = self.component(component_key)
        if signal_name not in comp.signals:
            raise PivotError(f"{comp.id} n'a pas de signal {signal_name!r}")
        return comp.signals[signal_name]

    def signal_by_tag(self, tag: str) -> Signal:
        if tag not in self.signals_by_tag:
            raise PivotError(f"Signal de tag inconnu : {tag!r}")
        return self.signals_by_tag[tag]


def load_pivot(
    path: Path | str = DEFAULT_PIVOT_PATH,
    *,
    base_overrides: Optional[dict[str, int]] = None,
) -> Mapping:
    """Charge le pivot et resout toutes les adresses absolues.

    `base_overrides` permet de recaler une zone (ex. {"cmd": 300}) sans toucher au
    pivot — utile si l'automaticien doit caser les zones dans un adressage existant.
    """
    path = Path(path)
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise PivotError(f"Pivot introuvable : {path}") from exc
    except json.JSONDecodeError as exc:
        raise PivotError(f"Pivot JSON invalide ({path}) : {exc}") from exc

    modbus = raw.get("modbus")
    if not isinstance(modbus, dict) or "zones" not in modbus:
        raise PivotError("Section 'modbus.zones' absente du pivot")

    zones = _resolve_zones(modbus["zones"], base_overrides or {})

    # Anti-collision : un bit TOR est unique ; un mot compteur reserve le mot entier.
    occupied_bits: set[tuple[str, int, int]] = set()
    occupied_words: set[tuple[str, int]] = set()

    # Heartbeat = mot entier dans la zone ret.
    heartbeat = _resolve_heartbeat(modbus.get("heartbeat"), zones)
    _check_bounds(heartbeat, zones)
    occupied_words.add((heartbeat.zone, heartbeat.abs_word))

    components: dict[str, Component] = {}
    by_tag: dict[str, Component] = {}
    signals_by_tag: dict[str, Signal] = {}
    all_signals: list[Signal] = [heartbeat]

    for comp_raw in raw.get("components", []):
        comp_id = str(comp_raw.get("id", ""))
        comp_tag = str(comp_raw.get("tag", comp_id))
        sigs: dict[str, Signal] = {}
        for name, spec in (comp_raw.get("signals") or {}).items():
            sig = _resolve_signal(comp_id, name, spec, zones)
            _check_bounds(sig, zones)
            _check_collision(sig, occupied_bits, occupied_words)
            sigs[name] = sig
            all_signals.append(sig)
            if sig.tag:
                if sig.tag in signals_by_tag:
                    raise PivotError(f"Tag de signal duplique : {sig.tag}")
                signals_by_tag[sig.tag] = sig
        comp = Component(id=comp_id, tag=comp_tag, type=str(comp_raw.get("type", "")), signals=sigs)
        components[comp_id] = comp
        by_tag[comp_tag] = comp

    if len(all_signals) <= 1:
        raise PivotError("Aucun signal de composant resolu depuis le pivot")

    return Mapping(
        zones=zones,
        components=components,
        by_tag=by_tag,
        signals_by_tag=signals_by_tag,
        all_signals=all_signals,
        heartbeat=heartbeat,
    )


def _resolve_zones(zones_raw: dict, overrides: dict[str, int]) -> dict[str, ZoneLayout]:
    out: dict[str, ZoneLayout] = {}
    for name, z in zones_raw.items():
        base = overrides.get(name, z.get("base_mw"))
        if base is None:
            raise PivotError(f"Zone {name!r} sans 'base_mw' ni override")
        size = z.get("size_words")
        if size is None:
            raise PivotError(f"Zone {name!r} sans 'size_words'")
        out[name] = ZoneLayout(name=name, base=int(base), size_words=int(size),
                               description=str(z.get("description", "")))
    return out


def _resolve_heartbeat(hb: Optional[dict], zones: dict) -> Signal:
    if not isinstance(hb, dict):
        raise PivotError("Section 'modbus.heartbeat' absente ou invalide")
    zone = hb.get("zone")
    if zone not in zones:
        raise PivotError(f"heartbeat : zone {zone!r} inconnue")
    word = hb.get("word")
    if word is None:
        raise PivotError("heartbeat : champ 'word' manquant")
    return Signal(
        component_id="_heartbeat", name="heartbeat", tag="HEARTBEAT", zone=zone,
        is_tor=False, word_rel=int(word), bit=None, abs_word=zones[zone].base + int(word),
    )


def _resolve_signal(comp_id: str, name: str, spec: dict, zones: dict) -> Signal:
    zone = spec.get("zone")
    if zone not in zones:
        raise PivotError(f"{comp_id}.{name} : zone {zone!r} inconnue")
    word = spec.get("word")
    if word is None:
        raise PivotError(f"{comp_id}.{name} : champ 'word' manquant")
    bit = spec.get("bit")  # None => mot entier
    return Signal(
        component_id=comp_id, name=name, tag=spec.get("tag"), zone=zone,
        is_tor=bit is not None, word_rel=int(word),
        bit=None if bit is None else int(bit),
        abs_word=zones[zone].base + int(word),
    )


def _check_bounds(sig: Signal, zones: dict) -> None:
    zone = zones[sig.zone]
    if not (0 <= sig.word_rel < zone.size_words):
        raise PivotError(
            f"{sig.component_id}.{sig.name} : word {sig.word_rel} hors zone {sig.zone} "
            f"(size_words {zone.size_words})"
        )
    if sig.is_tor and not (0 <= sig.bit <= 15):  # type: ignore[operator]
        raise PivotError(f"{sig.component_id}.{sig.name} : bit {sig.bit} hors [0..15]")


def _check_collision(sig: Signal, bits: set, words: set) -> None:
    if sig.is_tor:
        key = (sig.zone, sig.abs_word, sig.bit)
        if key in bits:
            raise PivotError(f"Conflit d'adresse TOR : {sig.zone} %MW{sig.abs_word} bit {sig.bit}")
        if (sig.zone, sig.abs_word) in words:
            raise PivotError(
                f"{sig.component_id}.{sig.name} : TOR sur un mot deja reserve "
                f"(compteur) {sig.zone} %MW{sig.abs_word}"
            )
        bits.add(key)
    else:
        wkey = (sig.zone, sig.abs_word)
        if wkey in words or any(z == sig.zone and w == sig.abs_word for z, w, _ in bits):
            raise PivotError(f"{sig.component_id}.{sig.name} : mot {sig.zone} %MW{sig.abs_word} deja occupe")
        words.add(wkey)


if __name__ == "__main__":
    # Utilitaire : afficher la table resolue (pratique en SSH pour verifier le mapping).
    m = load_pivot()
    for name, z in m.zones.items():
        print(f"[zone {name}] base=%MW{z.base} size={z.size_words} mots - {z.description}")
    print()
    hb = m.heartbeat
    print(f"  {'HEARTBEAT':10s} CNT  %MW{hb.abs_word}")
    for comp in m.components.values():
        for sig in comp.signals.values():
            addr = f"%MW{sig.abs_word}" + (f".{sig.bit}" if sig.is_tor else "")
            tag = sig.tag or "-"
            print(f"  {comp.tag:6s} {sig.name:14s} {addr:10s} tag={tag}")
