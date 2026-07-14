---
description: Etat des lieux — coherence pivot / mapping Modbus / dettes / docs
---

Realise un audit **en lecture seule** du projet (ne modifie rien sans validation).
Cible optionnelle : **$ARGUMENTS** (par defaut : audit complet).

Verifications :

1. **Coherence du pivot Modbus** (`pivot/machine_carrousel.json`) :
   - chaque composant a une adresse `{zone, word, bit}` ou `{zone, word}` valide,
     dans les bornes de `taille_mots` de sa zone
   - aucun **conflit d'adresse** (deux TOR sur le meme mot+bit, deux analogiques sur
     le meme mot, un analogique qui recouvre un mot de TOR)
   - heartbeat bien en mot 0 de `ret` ; pas de decalage `%MW` introduit
   - bits packes coherents (<= 16/mot), pas de 32 bits (interdit en V1)
2. **Alignement docs ↔ code** : les decisions de `memory.md` sont-elles respectees par
   le pivot et le code existant ? Signale tout ecart.
3. **Dettes** : `dettes.md` est-il a jour ? Des dettes `ouverte` auraient-elles du etre
   traitees dans le sprint courant ? Du code contredit-il une dette `assumee` ?
4. **Vocabulaire Schneider** : reperes et conventions conformes (KM/YV/S/B, %MW,
   FC3/FC16, I/O Scanner) ?
5. **Cote runtime** (si present) : le datastore reste-t-il un objet C# pur ? Le thread
   serveur touche-t-il le scene tree quelque part (interdit) ?

Rends un rapport structure : ✅ conforme / ⚠️ a surveiller / ❌ non conforme, avec
pour chaque anomalie le fichier:ligne et la correction proposee. Ne corrige qu'apres
validation.
