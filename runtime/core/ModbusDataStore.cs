// =============================================================================
// ModbusDataStore — source de verite des mots d'echange Modbus (Arch A)
// =============================================================================
//
// Role : detenir l'etat des deux zones d'echange sous forme de mots 16 bits bruts.
//   - zone cmd : PLC -> simulation (commandes ecrites par le M580 via FC16)
//   - zone ret : simulation -> PLC (retours lus par le M580 via FC3)
//
// C'est un objet C# PUR : tableaux `ushort[]` + un verrou, ZERO dependance Godot
// (il vit dans CarrouselCore, donc testable hors moteur — cf. dette D-006). Le thread
// du serveur Modbus ne doit jamais toucher le scene tree Godot ; ce datastore est le
// point de rendez-vous neutre entre les deux mondes.
//
// --- Place dans l'architecture (Arch A, actee 2026-07-14) ------------------------------
// Le datastore est la SOURCE DE VERITE. Le buffer interne de FluentModbus n'est qu'un
// detail prive du futur ModbusServer (brique 3), recopie <-> datastore une fois par tick
// physique, sous server.Lock :
//     debut de tick : wire cmd -> datastore   (WriteCommandsFromWire)   [pull]
//     fin de tick   : datastore ret -> wire   (CopyReturnsToWire)       [push]
// Entre les deux, la boucle de simulation (brique 4) lit les commandes en debut de tick
// (SnapshotCommands) et publie les retours en fin de tick (PublishReturns). Ce grain
// « une zone entiere par tick » garantit la COHERENCE INTRA-SCAN cote PLC : le M580 voit
// toujours un jeu de retours fige, jamais un etat a moitie calcule.
//
// --- Transport de mots BRUTS, pas de semantique machine ---------------------------------
// Le datastore ne connait NI les bits (cmd_run, S11...) NI le heartbeat : il ne fait que
// stocker et copier des mots. Le (de)codage bit <-> signal reste au PivotModel / a la
// boucle de simulation (ex. `pivot.GetSignal("KM1","cmd_run").ReadBit(cmd[wordRel])`).
// Le heartbeat n'est PAS incremente ici : c'est la simulation qui le reconstruit dans son
// `ushort[] ret` a chaque tick avant PublishReturns. Cela garde le datastore generique,
// sans horloge ni logique metier, donc trivialement testable.
//
// --- Aucune adresse absolue en dur ------------------------------------------------------
// Les tailles des deux tableaux sont tirees du pivot (`GetZone(...).SizeWords`), jamais de
// constantes. Le decoupage par base absolue (%MW100/%MW200) reste l'affaire du serveur
// (brique 3) : ce datastore ne manipule que des mots RELATIFS a une zone, et se contente
// de verifier que les longueurs correspondent (code defensif sur les entrees externes).

namespace CarrouselCore;

/// <summary>
/// Datastore Modbus thread-safe : deux zones de mots 16 bits (cmd/ret) + verrou.
/// Source de verite d'Arch A. Transport brut, sans decodage bit ni heartbeat.
/// </summary>
public sealed class ModbusDataStore
{
    // Noms canoniques des zones du pivot (contrat Phase 0 : cmd = PLC->sim, ret = sim->PLC).
    // Ce sont des CLES de zone, pas des adresses : les rendre nommees evite les fautes de
    // frappe et documente l'intention. Les adresses absolues, elles, restent dans le pivot.
    private const string CmdZone = "cmd";
    private const string RetZone = "ret";

    // Verrou interne. En Arch A un SEUL thread (physique) touche le datastore, donc ce
    // verrou n'est pas strictement necessaire aujourd'hui. On le conserve (decision D-d) :
    //   - « belt and suspenders » : le pattern impose par CLAUDE.md est `ushort[]` + verrou ;
    //   - il ouvre la porte a une lecture concurrente future (IHM de debug) sans re-toucher
    //     cette classe.
    // Cout nul en pratique (contention quasi inexistante) et pas d'ordre de verrous a
    // respecter : le pont serveur prend server.Lock (externe) PUIS ce verrou (interne),
    // jamais l'inverse ailleurs.
    private readonly object _lock = new();

    // Etat detenu : un tableau par zone, dimensionne au chargement, jamais realloue ensuite.
    private readonly ushort[] _cmd;
    private readonly ushort[] _ret;

    /// <summary>
    /// Alloue les deux zones aux tailles declarees dans le pivot (`size_words`).
    /// Ici : cmd = 1 mot, ret = 2 mots — mais rien n'est code en dur, tout vient du pivot.
    /// </summary>
    public ModbusDataStore(PivotModel pivot)
    {
        _cmd = new ushort[pivot.GetZone(CmdZone).SizeWords];
        _ret = new ushort[pivot.GetZone(RetZone).SizeWords];
    }

    /// <summary>Nombre de mots de la zone cmd (PLC -> sim).</summary>
    public int CommandWordCount => _cmd.Length;

    /// <summary>Nombre de mots de la zone ret (sim -> PLC).</summary>
    public int ReturnWordCount => _ret.Length;

    // =====================================================================
    // Cote simulation (thread physique)
    // =====================================================================

    /// <summary>
    /// Renvoie une COPIE atomique de la zone cmd (snapshot debut de tick). Muter le tableau
    /// rendu ne modifie jamais l'etat interne : la simulation peut travailler dessus librement.
    /// </summary>
    public ushort[] SnapshotCommands()
    {
        lock (_lock)
            return (ushort[])_cmd.Clone();
    }

    /// <summary>
    /// Remplace la zone ret d'un bloc (publication fin de tick). La longueur doit valoir
    /// exactement <see cref="ReturnWordCount"/>, sinon on echoue clairement (jamais de
    /// publication partielle). Le contenu est COPIE : l'appelant garde la propriete de son
    /// tableau.
    /// </summary>
    public void PublishReturns(ushort[] ret)
    {
        ArgumentNullException.ThrowIfNull(ret);
        if (ret.Length != _ret.Length)
            throw new ArgumentException(
                $"PublishReturns : {ret.Length} mots recus, {_ret.Length} attendus (size_words de la zone ret)",
                nameof(ret));

        lock (_lock)
            Array.Copy(ret, _ret, _ret.Length);
    }

    // =====================================================================
    // Pont serveur (brique 3), appele sous server.Lock
    // =====================================================================
    //
    // Les deux methodes recoivent un span DEJA DECOUPE a la taille de la zone : c'est le
    // serveur qui slice son buffer FluentModbus par base absolue (buffer.Slice(base, size)).
    // Le datastore ne connait donc jamais %MW100/%MW200 ; il verifie seulement la longueur.
    // Span<ushort> plutot que ushort[] : zero allocation et alignement exact sur l'API buffer
    // de FluentModbus (`GetHoldingRegisters` renvoie un span).

    /// <summary>
    /// Pull : recopie les mots de commande venus du fil (buffer serveur) dans le datastore.
    /// Longueur attendue = <see cref="CommandWordCount"/>.
    /// </summary>
    public void WriteCommandsFromWire(ReadOnlySpan<ushort> words)
    {
        if (words.Length != _cmd.Length)
            throw new ArgumentException(
                $"WriteCommandsFromWire : {words.Length} mots recus, {_cmd.Length} attendus (zone cmd)",
                nameof(words));

        lock (_lock)
            words.CopyTo(_cmd);
    }

    /// <summary>
    /// Push : recopie les retours du datastore vers le fil (buffer serveur).
    /// Longueur attendue = <see cref="ReturnWordCount"/>.
    /// </summary>
    public void CopyReturnsToWire(Span<ushort> words)
    {
        if (words.Length != _ret.Length)
            throw new ArgumentException(
                $"CopyReturnsToWire : {words.Length} mots cibles, {_ret.Length} attendus (zone ret)",
                nameof(words));

        lock (_lock)
            _ret.CopyTo(words);
    }
}
