"""Tests unitaires du loader pivot — ne demandent aucun serveur.

Ils verrouillent le contrat Modbus fige en Phase 0 (schema 0.2.0) : bonnes zones,
adressage relatif correctement resolu, absence de conflit, invariants (heartbeat en
mot 0 de ret, KM1_AUX present, tags S11/S12/S21/S22). Si un futur commit casse le
pivot, ces tests le signalent.

Adresses attendues (bases cmd=100, ret=200) :
    cmd_run(KM1)   %MW100.0     ret_running(KM1_AUX) %MW201.6
    cmd_extend YV1 %MW100.1     S11/S12              %MW201.0 / .1
    cmd_extend YV2 %MW100.2     S21/S22              %MW201.2 / .3
    heartbeat      %MW200       B1/B2 presence       %MW201.4 / .5
"""

from __future__ import annotations

import json

import pytest

from pivot_loader import PivotError, load_pivot


def test_zones_et_bases(mapping):
    assert set(mapping.zones) == {"cmd", "ret"}
    assert (mapping.zone("cmd").base, mapping.zone("cmd").size_words) == (100, 1)
    assert (mapping.zone("ret").base, mapping.zone("ret").size_words) == (200, 2)


def test_heartbeat_mot0_de_ret(mapping):
    hb = mapping.heartbeat
    assert hb.zone == "ret"
    assert hb.word_rel == 0
    assert hb.abs_word == 200
    assert hb.is_tor is False


def test_commande_convoyeur(mapping):
    run = mapping.signal("KM1", "cmd_run")   # lookup par tag composant
    assert (run.zone, run.abs_word, run.bit) == ("cmd", 100, 0)


def test_retour_marche_moteur_km1_aux(mapping):
    aux = mapping.signal_by_tag("KM1_AUX")
    assert (aux.zone, aux.abs_word, aux.bit) == ("ret", 201, 6)
    # KM1_AUX est bien le ret_running du composant conveyor (ex-B3 supprime).
    assert aux is mapping.signal("conveyor", "ret_running")


@pytest.mark.parametrize(
    "tag, abs_word, bit",
    [("S11", 201, 0), ("S12", 201, 1), ("S21", 201, 2), ("S22", 201, 3)],
)
def test_fins_de_course_verins(mapping, tag, abs_word, bit):
    s = mapping.signal_by_tag(tag)
    assert (s.zone, s.abs_word, s.bit) == ("ret", abs_word, bit)


def test_presences_palette(mapping):
    b1 = mapping.signal("B1", "ret_active")
    b2 = mapping.signal("B2", "ret_active")
    assert (b1.abs_word, b1.bit) == (201, 4)
    assert (b2.abs_word, b2.bit) == (201, 5)


def test_lecture_bit_depuis_mot(mapping):
    s11 = mapping.signal_by_tag("S11")           # bit 0
    aux = mapping.signal_by_tag("KM1_AUX")       # bit 6
    word = 0b0100_0001                           # bits 0 et 6 a 1
    assert s11.read_bit(word) is True
    assert aux.read_bit(word) is True
    assert mapping.signal_by_tag("S12").read_bit(word) is False


def test_bases_surchargeables():
    """Recaler une zone (adressage memoire existant) ne casse pas le mapping."""
    m = load_pivot(base_overrides={"cmd": 300, "ret": 400})
    assert m.zone("cmd").base == 300
    assert m.signal("KM1", "cmd_run").abs_word == 300
    assert m.heartbeat.abs_word == 400
    assert m.signal_by_tag("KM1_AUX").abs_word == 401


def test_aucun_hors_borne(mapping):
    for sig in mapping.all_signals:
        z = mapping.zone(sig.zone)
        assert 0 <= sig.word_rel < z.size_words
        if sig.is_tor:
            assert 0 <= sig.bit <= 15


# --- Robustesse : un pivot malforme doit echouer CLAIREMENT ---

_MINIMAL = {
    "modbus": {
        "zones": {
            "cmd": {"base_mw": 100, "size_words": 1},
            "ret": {"base_mw": 200, "size_words": 2},
        },
        "heartbeat": {"zone": "ret", "word": 0},
    },
    "components": [
        {"id": "c", "tag": "KM1", "type": "conveyor_circular",
         "signals": {"cmd_run": {"zone": "cmd", "word": 0, "bit": 0}}},
    ],
}


def _write(tmp_path, obj):
    p = tmp_path / "pivot.json"
    p.write_text(json.dumps(obj), encoding="utf-8")
    return p


def test_json_invalide_echoue(tmp_path):
    p = tmp_path / "broken.json"
    p.write_text("{ pas du json", encoding="utf-8")
    with pytest.raises(PivotError):
        load_pivot(p)


def test_bit_hors_borne_echoue(tmp_path):
    obj = json.loads(json.dumps(_MINIMAL))
    obj["components"][0]["signals"]["cmd_run"]["bit"] = 16
    with pytest.raises(PivotError, match="bit"):
        load_pivot(_write(tmp_path, obj))


def test_word_hors_zone_echoue(tmp_path):
    obj = json.loads(json.dumps(_MINIMAL))
    obj["components"][0]["signals"]["cmd_run"]["word"] = 5  # zone cmd size_words=1
    with pytest.raises(PivotError, match="hors zone"):
        load_pivot(_write(tmp_path, obj))


def test_conflit_adresse_echoue(tmp_path):
    obj = json.loads(json.dumps(_MINIMAL))
    obj["components"][0]["signals"]["cmd_dup"] = {"zone": "cmd", "word": 0, "bit": 0}
    with pytest.raises(PivotError, match="[Cc]onflit"):
        load_pivot(_write(tmp_path, obj))


def test_heartbeat_absent_echoue(tmp_path):
    obj = json.loads(json.dumps(_MINIMAL))
    del obj["modbus"]["heartbeat"]
    with pytest.raises(PivotError, match="heartbeat"):
        load_pivot(_write(tmp_path, obj))
