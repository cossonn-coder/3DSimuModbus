"""Tests d'integration de la chaine Modbus (skippent si le serveur Godot est absent).

Ces tests jouent le role du M580 : ecriture de la zone `cmd` en FC16, lecture de la
zone `ret` en FC3, comme l'I/O Scanner. Ils encodent la Definition of Done du Sprint 1.
Tant que le runtime Godot n'ecoute pas sur le port 502, ils **skippent** (fixture
`modbus_client`) — ils ne comptent donc pas comme des echecs.

Lancer le runtime, puis :  python -m pytest test_modbus_chain.py -v
"""

from __future__ import annotations

import time

import pytest

from pivot_loader import Mapping, Signal


def _read_word(client, unit: int, abs_word: int) -> int:
    rr = client.read_holding_registers(abs_word, count=1, slave=unit)
    if rr.isError():
        raise AssertionError(f"FC3 %MW{abs_word} en erreur : {rr}")
    return rr.registers[0]


def _read_bit(client, unit: int, sig: Signal) -> bool:
    return sig.read_bit(_read_word(client, unit, sig.abs_word))


def _write_word(client, unit: int, abs_word: int, value: int) -> None:
    wr = client.write_registers(abs_word, [value & 0xFFFF], slave=unit)
    if wr.isError():
        raise AssertionError(f"FC16 %MW{abs_word} en erreur : {wr}")


def _set_cmd_bit(client, unit: int, sig: Signal, value: bool) -> None:
    """Positionne un bit de commande sans ecraser les autres bits du mot."""
    current = _read_word(client, unit, sig.abs_word)
    current = (current | (1 << sig.bit)) if value else (current & ~(1 << sig.bit) & 0xFFFF)
    _write_word(client, unit, sig.abs_word, current)


def test_heartbeat_progresse(modbus_client, mapping: Mapping, modbus_target):
    """Le heartbeat (ret mot 0) doit s'incrementer : preuve que la simulation vit."""
    unit = modbus_target["unit"]
    hb = mapping.heartbeat
    v1 = _read_word(modbus_client, unit, hb.abs_word)
    time.sleep(0.5)  # a period_ms=100, on attend ~5 increments
    v2 = _read_word(modbus_client, unit, hb.abs_word)
    assert v1 != v2, "heartbeat fige : simulation arretee"


def test_marche_convoyeur_colle_km1_aux(modbus_client, mapping, modbus_target):
    """cmd_run=1 -> KM1_AUX (ret_running) passe a 1 apres feedback_delay ; =0 -> retombe."""
    unit = modbus_target["unit"]
    run = mapping.signal("KM1", "cmd_run")
    aux = mapping.signal_by_tag("KM1_AUX")

    _set_cmd_bit(modbus_client, unit, run, True)
    time.sleep(0.3)  # > feedback_delay_ms (50 ms)
    assert _read_bit(modbus_client, unit, aux) is True, "KM1_AUX attendu a 1 (moteur alimente)"

    _set_cmd_bit(modbus_client, unit, run, False)
    time.sleep(0.3)
    assert _read_bit(modbus_client, unit, aux) is False, "KM1_AUX doit retomber a l'arret"


@pytest.mark.parametrize(
    "comp, extended_tag, retracted_tag",
    [("cylinder_1", "S12", "S11"), ("cylinder_2", "S22", "S21")],
)
def test_verin_monostable_sortie_et_rappel(
    modbus_client, mapping, modbus_target, comp, extended_tag, retracted_tag
):
    """cmd_extend=1 -> sorti (fin de course extended) ; =0 -> rappel ressort (retracted)."""
    unit = modbus_target["unit"]
    extend = mapping.signal(comp, "cmd_extend")
    fc_extended = mapping.signal_by_tag(extended_tag)
    fc_retracted = mapping.signal_by_tag(retracted_tag)

    _set_cmd_bit(modbus_client, unit, extend, True)
    time.sleep(0.8)  # > travel_time_ms (500 ms)
    assert _read_bit(modbus_client, unit, fc_extended) is True, "fin de course sortie attendue"
    assert _read_bit(modbus_client, unit, fc_retracted) is False

    _set_cmd_bit(modbus_client, unit, extend, False)
    time.sleep(0.8)  # rappel ressort (monostable)
    assert _read_bit(modbus_client, unit, fc_retracted) is True, "rappel ressort attendu (rentre)"
    assert _read_bit(modbus_client, unit, fc_extended) is False
