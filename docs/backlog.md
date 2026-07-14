# backlog.md — Phases et tâches

Statuts : ✅ fait · 🔄 en cours · ⏳ à venir · 🧊 gelé (post-démonstrateur)

## Phase 0 — Modèle pivot ✅
- ✅ Spec fonctionnelle carrousel validée
- ✅ Table Modbus validée (2 zones, 2 lignes I/O Scanner FC3/FC16)
- ✅ `pivot/machine_carrousel.json` v0.2
- ✅ CLAUDE.md + orchestration + commandes

## Phase 1 — Chaîne Modbus de bout en bout 🔄
Brief : `docs/sprints/sprint_01_brief.md`
- ⏳ Sprint 1 : datastore thread-safe + serveur Modbus + heartbeat + banc de test
  Python (émulateur I/O Scanner) + pytest
- ⏳ Validation croisée avec le M580 réel (2 lignes de scan dans Control Expert)

## Phase 1bis — Carrousel 3D et cinématique ⏳
- ⏳ Sprint 2 : chargeur JSON pivot défensif + scène 3D procédurale
  (anneau, palettes, vérins, zones capteurs)
- ⏳ Sprint 3 : cinématique (vérins interpolés + capteurs à seuils, palettes +
  accumulation, présence, retour KM1) branchée sur le datastore
  (snapshot début de tick / publication fin de tick)

## Phase 4 — Intégration M580 réelle ⏳
- ⏳ Sprint 4 : campagne avec le programme PLC de l'automaticien, mesure de latence
  scan↔simulation, IHM minimale (état des mots, forçage basique de debug)

## Phases 2-3 — Pipeline d'extraction Python 🧊 (après démonstrateur)
- 🧊 DWG → DXF (ezdxf) → géométrie 2D → volumes basiques
- 🧊 Schéma électrique PDF → OCR/CV → composants + repères → mapping Modbus proposé
- 🧊 Mécanisme de relecture/correction de l'extraction
- 🧊 JSON Schema de validation du pivot (rembourse D-005)

## Phase 5 — IHM automaticien 🧊
- 🧊 Édition du mapping, diagnostic, forçage, visualisation des échanges en direct
  (à spécifier avec l'automaticien après le démonstrateur)
