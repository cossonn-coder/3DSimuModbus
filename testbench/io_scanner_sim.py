"""Simulateur d'I/O Scanner — joue le M580 face au serveur Modbus Godot.

Utilitaire manuel (pas un test) : reproduit le comportement de l'I/O Scanner
d'EcoStruxure Control Expert, avec ses **2 lignes de scan** :
    - FC3 (Read Holding Registers)   -> lit la zone `ret` (retours simulation)
    - FC16 (Write Multiple Registers) -> ecrit la zone `cmd` (commandes PLC)

Il scrute a cadence fixe et affiche l'etat decode depuis le pivot. Sert a valider la
chaine « a la main » avant/en complement du M580 reel, et a lever D-001 (observer le
serveur FluentModbus sous scan repete).

Exemples :
    python io_scanner_sim.py --period 0.1
    python io_scanner_sim.py --run 1 --yv1 1     # convoyeur ON + verin 1 sorti, puis scrute
"""

from __future__ import annotations

import argparse
import sys
import time

from pivot_loader import Mapping, Signal, load_pivot


def _read_zone(client, unit: int, zone) -> list[int]:
    rr = client.read_holding_registers(zone.base, count=zone.size_words, device_id=unit)
    if rr.isError():
        raise SystemExit(f"FC3 sur zone {zone.name} en erreur : {rr}")
    return list(rr.registers)


def _apply_commands(client, unit: int, mapping: Mapping, forces: dict[str, bool]) -> None:
    """Ecrit la zone cmd complete (FC16) en appliquant les forcages de bits demandes.

    forces : { "<component_key>.<signal>": bool }.
    """
    cmd = mapping.zone("cmd")
    words = [0] * cmd.size_words
    for ref, value in forces.items():
        comp_key, _, signal_name = ref.partition(".")
        sig: Signal = mapping.signal(comp_key, signal_name)
        if sig.zone != "cmd":
            raise SystemExit(f"{ref} n'est pas une commande")
        rel = sig.abs_word - cmd.base
        if value and sig.is_tor:
            words[rel] |= 1 << sig.bit
    wr = client.write_registers(cmd.base, words, device_id=unit)
    if wr.isError():
        raise SystemExit(f"FC16 sur zone cmd en erreur : {wr}")


def _decode_returns(mapping: Mapping, words: list[int]) -> str:
    ret = mapping.zone("ret")
    parts = [f"HB={words[mapping.heartbeat.abs_word - ret.base]}"]
    for sig in mapping.all_signals:
        if sig.zone != "ret" or not sig.is_tor:
            continue
        rel = sig.abs_word - ret.base
        state = (words[rel] >> sig.bit) & 0x1
        label = sig.tag or f"{sig.component_id}.{sig.name}"
        parts.append(f"{label}={state}")
    return "  ".join(parts)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Simulateur d'I/O Scanner (joue le M580)")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=502)
    ap.add_argument("--unit", type=int, default=1)
    ap.add_argument("--period", type=float, default=0.1, help="cadence de scan (s)")
    ap.add_argument("--cycles", type=int, default=0, help="0 = infini")
    ap.add_argument("--run", type=int, choices=[0, 1], help="convoyeur (KM1.cmd_run)")
    ap.add_argument("--yv1", type=int, choices=[0, 1], help="verin 1 (cylinder_1.cmd_extend)")
    ap.add_argument("--yv2", type=int, choices=[0, 1], help="verin 2 (cylinder_2.cmd_extend)")
    args = ap.parse_args(argv)

    try:
        from pymodbus.client import ModbusTcpClient
    except ImportError:
        raise SystemExit("pymodbus non installe : pip install -r requirements.txt")

    mapping = load_pivot()
    client = ModbusTcpClient(host=args.host, port=args.port, timeout=1.0)
    if not client.connect():
        raise SystemExit(f"Connexion impossible a {args.host}:{args.port}")

    forces: dict[str, bool] = {}
    if args.run is not None:
        forces["KM1.cmd_run"] = bool(args.run)
    if args.yv1 is not None:
        forces["cylinder_1.cmd_extend"] = bool(args.yv1)
    if args.yv2 is not None:
        forces["cylinder_2.cmd_extend"] = bool(args.yv2)

    try:
        if forces:
            _apply_commands(client, args.unit, mapping, forces)
            print("commandes ecrites (FC16) :", forces)

        n = 0
        while args.cycles == 0 or n < args.cycles:
            words = _read_zone(client, args.unit, mapping.zone("ret"))
            print(f"[{n:5d}] {_decode_returns(mapping, words)}")
            n += 1
            time.sleep(args.period)
    except KeyboardInterrupt:
        pass
    finally:
        client.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
