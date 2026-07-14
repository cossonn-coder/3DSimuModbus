"""Fixtures pytest partagees du testbench.

Deux familles de tests :
  - **unitaires** : valident le loader pivot, ne demandent aucun serveur. Tournent
    partout, tout le temps.
  - **integration Modbus** : demandent un runtime Godot (serveur FluentModbus) en
    ecoute. Ils **skippent proprement** si le serveur est absent, pour ne jamais
    echouer a cause de l'environnement (regle /test_suite).

Configuration serveur via variables d'environnement :
    MODBUS_HOST (defaut 127.0.0.1), MODBUS_PORT (defaut 502), MODBUS_UNIT (defaut 1).
"""

from __future__ import annotations

import os

import pytest

from pivot_loader import Mapping, load_pivot


@pytest.fixture(scope="session")
def mapping() -> Mapping:
    """Table Modbus resolue depuis le pivot de reference (bases par defaut)."""
    return load_pivot()


@pytest.fixture(scope="session")
def modbus_target() -> dict:
    return {
        "host": os.environ.get("MODBUS_HOST", "127.0.0.1"),
        "port": int(os.environ.get("MODBUS_PORT", "502")),
        "unit": int(os.environ.get("MODBUS_UNIT", "1")),
    }


@pytest.fixture()
def modbus_client(modbus_target):
    """Client Modbus TCP jouant le M580. Skip si le serveur Godot n'ecoute pas.

    On importe pymodbus ici (pas au niveau module) pour que les tests unitaires
    tournent meme si pymodbus n'est pas installe.
    """
    try:
        from pymodbus.client import ModbusTcpClient
    except ImportError:
        pytest.skip("pymodbus non installe (pip install -r requirements.txt)")

    client = ModbusTcpClient(
        host=modbus_target["host"], port=modbus_target["port"], timeout=1.0
    )
    if not client.connect():
        pytest.skip(
            f"Aucun serveur Modbus sur {modbus_target['host']}:{modbus_target['port']} "
            f"(lancer le runtime Godot d'abord)"
        )
    try:
        yield client
    finally:
        client.close()
