// =============================================================================
// ModbusServerTests — validation d'INTEGRATION du pont Modbus (brique 3, reco 2a)
// =============================================================================
//
// Ces tests demarrent un VRAI ModbusServer sur loopback et le martelent avec un vrai
// ModbusTcpClient FluentModbus, in-process (pas de reseau exterieur, pas de M580). Ils
// verrouillent les deux proprietes que la brique 3 doit garantir AVANT que la boucle de
// simulation (brique 4) n'arrive :
//   1. TRANSPORT : ce que le client ecrit en FC16 arrive au datastore apres PullCommands,
//      et ce que le datastore publie ressort au client en FC3 apres PushReturns ;
//   2. ENDIANNESS : le serveur serialise le fil en BIG-endian (Set/GetBigEndian). Un client
//      big-endian retrouve la valeur a l'identique ; un client little-endian lit les octets
//      INVERSES — preuve concrete que le format fil est bien big-endian (le vrai M580/pymodbus
//      est big-endian, cf. POC D-001).
//
// Port : on ne prend PAS le 502 du pivot reel (privilege/conflit/flakiness en test). On
// injecte un port EPHEMERE libre dans un pivot de test et on ecoute sur loopback. Le vrai
// M580 utilisera le port du pivot sur toutes les interfaces (IPAddress.Any en prod).

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using CarrouselCore;
using FluentModbus;
using Xunit;

namespace CarrouselServer.Tests;

public class ModbusServerTests
{
    // Pivot de test : meme structure que le pivot reel (cmd base 100/1 mot, ret base 200/2 mots,
    // heartbeat mot 0 de ret, un composant pour satisfaire "au moins un signal"), avec le port
    // injecte a l'execution. __PORT__ est remplace par un port libre.
    private const string PivotTemplate = """
    {
      "modbus": {
        "port": __PORT__,
        "unit_id": 1,
        "zones": {
          "cmd": { "base_mw": 100, "size_words": 1 },
          "ret": { "base_mw": 200, "size_words": 2 }
        },
        "heartbeat": { "zone": "ret", "word": 0 }
      },
      "components": [
        { "id": "conveyor", "tag": "KM1", "type": "conveyor_circular",
          "signals": {
            "cmd_run": { "zone": "cmd", "word": 0, "bit": 0 },
            "ret_running": { "zone": "ret", "word": 1, "bit": 6, "tag": "KM1_AUX" }
          } }
      ]
    }
    """;

    // Reserve un port TCP libre en liant brievement un listener sur le port 0 (l'OS choisit),
    // puis le relache. Fenetre de course minuscule et acceptable pour un test local.
    private static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static PivotModel LoadPivotWithPort(int port)
    {
        string json = PivotTemplate.Replace("__PORT__", port.ToString());
        string path = Path.Combine(Path.GetTempPath(), $"pivot_srv_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return PivotModel.Load(path);
    }

    // Contexte d'un test : pivot + datastore + serveur demarre sur un port libre.
    private sealed record Boot(PivotModel Pivot, ModbusDataStore Store, ModbusServer Server, int Port) : IDisposable
    {
        public void Dispose() => Server.Dispose();
    }

    private static Boot Start()
    {
        int port = FreeTcpPort();
        var pivot = LoadPivotWithPort(port);
        var store = new ModbusDataStore(pivot);
        // bind loopback : test local isole (le prod utilisera IPAddress.Any par defaut).
        var server = new ModbusServer(pivot, store, IPAddress.Loopback);
        server.Start();
        return new Boot(pivot, store, server, port);
    }

    private static ModbusTcpClient Connect(int port, ModbusEndianness endianness)
    {
        var client = new ModbusTcpClient();
        client.Connect(new IPEndPoint(IPAddress.Loopback, port), endianness);
        return client;
    }

    // Petite attente active bornee : le handler RegistersChanged fire sur le THREAD SERVEUR
    // FluentModbus. En pratique il est leve pendant le traitement de la requete FC16, donc avant
    // meme que WriteMultipleRegisters ne rende la main cote client ; ce poll n'est qu'un filet
    // anti-flakiness (ordonnancement de thread) et sort des que la condition est vraie.
    private static void WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            Thread.Sleep(5);
    }

    // =====================================================================
    // 0. Sante du serveur : bind occupe bruyant (D-013) + IsListening + activite PLC
    // =====================================================================

    // D-013 : REPRODUCTION du bind occupe. Un 1er serveur ecoute sur un port P ; un 2nd serveur
    // demarre sur le MEME P doit echouer CLAIREMENT (ModbusServerException) et non pretendre
    // ecouter. C'est la preuve que l'echec de bind est desormais bruyant (avant : muet).
    [Fact]
    public void Bind_occupe_leve_ModbusServerException()
    {
        using var first = Start();   // occupe loopback:P

        // 2nd serveur sur le meme port, meme interface loopback.
        var pivot2 = LoadPivotWithPort(first.Port);
        var store2 = new ModbusDataStore(pivot2);
        using var second = new ModbusServer(pivot2, store2, IPAddress.Loopback);

        var ex = Assert.Throws<ModbusServerException>(() => second.Start());
        Assert.Contains(first.Port.ToString(), ex.Message);   // message FR cite bind:port
        Assert.False(second.IsListening);                     // n'a jamais pretendu ecouter
    }

    // IsListening : faux avant Start, vrai apres un Start reussi.
    [Fact]
    public void IsListening_faux_avant_start_vrai_apres()
    {
        int port = FreeTcpPort();
        var pivot = LoadPivotWithPort(port);
        var store = new ModbusDataStore(pivot);
        using var server = new ModbusServer(pivot, store, IPAddress.Loopback);

        Assert.False(server.IsListening);   // pas encore demarre
        server.Start();
        Assert.True(server.IsListening);    // ecoute confirmee
    }

    // Activite PLC : LastClientWriteUtc null tant qu'aucun client n'a ecrit, puis non-null apres
    // une trame FC16. Second volet : l'event RegistersChanged fire MEME si la valeur cmd ne change
    // pas (AlwaysRaiseChangedEvent) — indispensable car l'I/O Scanner reecrit cmd a l'identique a
    // chaque scan. On reecrit la MEME valeur et on verifie que l'horodatage AVANCE malgre tout.
    [Fact]
    public void LastClientWrite_est_alimente_par_une_ecriture_fc16()
    {
        using var ctx = Start();
        using var client = Connect(ctx.Port, ModbusEndianness.BigEndian);

        Assert.Null(ctx.Server.LastClientWriteUtc);   // aucun client n'a encore ecrit

        const ushort cmdValue = 0b0000_0011;
        int cmdBase = ctx.Pivot.GetZone("cmd").Base;
        client.WriteMultipleRegisters<ushort>(ctx.Pivot.UnitId, cmdBase, new[] { cmdValue });

        WaitUntil(() => ctx.Server.LastClientWriteUtc is not null);
        var firstWrite = ctx.Server.LastClientWriteUtc;
        Assert.NotNull(firstWrite);

        // Ecriture IDENTIQUE : sans AlwaysRaiseChangedEvent, aucun event ne serait leve et
        // l'horodatage stagnerait. On exige qu'il avance -> preuve que la valeur inchangee fire.
        Thread.Sleep(5);   // garantit un tick horloge distinct (resolution 100 ns depassee)
        client.WriteMultipleRegisters<ushort>(ctx.Pivot.UnitId, cmdBase, new[] { cmdValue });

        WaitUntil(() => ctx.Server.LastClientWriteUtc > firstWrite);
        Assert.True(ctx.Server.LastClientWriteUtc > firstWrite,
            "RegistersChanged doit fire meme sur une ecriture cmd de valeur identique");
    }

    // =====================================================================
    // 1. Transport : FC16 (client -> buffer) puis PullCommands -> datastore
    // =====================================================================

    [Fact]
    public void Fc16_puis_pull_alimente_le_datastore()
    {
        using var ctx = Start();
        using var client = Connect(ctx.Port, ModbusEndianness.BigEndian);

        // Le M580 ecrit la zone cmd (%MW100) via FC16 : cmd_run (bit 0) + cmd_extend YV1 (bit 1).
        const ushort cmdValue = 0b0000_0011;
        client.WriteMultipleRegisters<ushort>(ctx.Pivot.UnitId, ctx.Pivot.GetZone("cmd").Base, new[] { cmdValue });

        // Le thread appelant (ici le test) tire les commandes en debut de tick.
        ctx.Server.PullCommands();

        Assert.Equal(new ushort[] { cmdValue }, ctx.Store.SnapshotCommands());
    }

    // =====================================================================
    // 2. Transport : PublishReturns -> PushReturns -> FC3 (buffer -> client)
    // =====================================================================

    [Fact]
    public void Publish_puis_push_est_lu_par_fc3()
    {
        using var ctx = Start();
        using var client = Connect(ctx.Port, ModbusEndianness.BigEndian);

        // La simulation publie ses retours en fin de tick : heartbeat + quelques bits (KM1_AUX...).
        var ret = new ushort[] { 0x00AB, 0b0100_0001 };
        ctx.Store.PublishReturns(ret);
        ctx.Server.PushReturns();

        // Le M580 relit la zone ret (%MW200..201) via FC3.
        Span<ushort> read = client.ReadHoldingRegisters<ushort>(
            ctx.Pivot.UnitId, ctx.Pivot.GetZone("ret").Base, ctx.Store.ReturnWordCount);

        Assert.Equal(ret, read.ToArray());
    }

    // =====================================================================
    // 3. Endianness : le fil est bien big-endian (un client little-endian lit inverse)
    // =====================================================================

    [Fact]
    public void Push_serialise_le_fil_en_big_endian()
    {
        using var ctx = Start();

        // ret[1] = 0x1234. Le serveur ecrit via SetBigEndian => octets fil = 12 34.
        ctx.Store.PublishReturns(new ushort[] { 0x0000, 0x1234 });
        ctx.Server.PushReturns();

        // Un client LITTLE-endian interprete donc 34 12 = 0x3412 : preuve que le fil est
        // big-endian (si le serveur avait ecrit en natif little-endian, on lirait 0x1234).
        using var little = Connect(ctx.Port, ModbusEndianness.LittleEndian);
        Span<ushort> read = little.ReadHoldingRegisters<ushort>(
            ctx.Pivot.UnitId, ctx.Pivot.GetZone("ret").Base + 1, 1);
        Assert.Equal((ushort)0x3412, read[0]);

        // Et un client big-endian, lui, retrouve 0x1234 a l'identique.
        using var big = Connect(ctx.Port, ModbusEndianness.BigEndian);
        Span<ushort> readBig = big.ReadHoldingRegisters<ushort>(
            ctx.Pivot.UnitId, ctx.Pivot.GetZone("ret").Base + 1, 1);
        Assert.Equal((ushort)0x1234, readBig[0]);
    }

    // =====================================================================
    // 4. Coupure TCP volontaire (S5.4) : Disconnect / Reconnect
    // =====================================================================
    //
    // Verdict du SPIKE FluentModbus 5.3.2 (documente dans NOTES + memory) : un ModbusTcpServer
    // se REARME sur la MEME instance apres Stop()/Start() — port re-ouvert, unite et buffer
    // conserves, client reconnectable, AddUnit inutile a rearmer. Disconnect=Stop, Reconnect=Start.
    // Ces tests verrouillent la nouvelle capacite transport (le defaut de comm « perte de l'esclave »).

    // IsListening suit Disconnect (false) puis Reconnect (true). Coeur du contrat transport S5.4.
    [Fact]
    public void Disconnect_coupe_l_ecoute_reconnect_la_retablit()
    {
        using var ctx = Start();
        Assert.True(ctx.Server.IsListening);   // etat initial : a l'ecoute

        ctx.Server.Disconnect();
        Assert.False(ctx.Server.IsListening);  // ecoute coupee (I/O Scanner perd l'esclave)

        ctx.Server.Reconnect();
        Assert.True(ctx.Server.IsListening);   // ecoute retablie apres « Reparer »
    }

    // Idempotence : un 2e Disconnect (ou un 2e Reconnect) ne fait rien et ne leve pas. L'IHM
    // (toggle) peut donc appeler sans se soucier de l'etat courant.
    [Fact]
    public void Disconnect_et_reconnect_sont_idempotents()
    {
        using var ctx = Start();

        ctx.Server.Disconnect();
        ctx.Server.Disconnect();               // 2e coupure : no-op, pas d'exception
        Assert.False(ctx.Server.IsListening);

        ctx.Server.Reconnect();
        ctx.Server.Reconnect();                // 2e reconnexion : no-op, pas d'exception
        Assert.True(ctx.Server.IsListening);
    }

    // Garde des no-op Pull/Push hors ecoute : StepSim doit pouvoir continuer a tourner pendant la
    // coupure sans qu'un GetHoldingRegisters d'un serveur arrete ne leve. On publie des retours,
    // on coupe, puis on appelle Pull/Push : aucune exception ne doit fuiter.
    [Fact]
    public void Pull_et_push_hors_ecoute_ne_levent_pas()
    {
        using var ctx = Start();
        ctx.Store.PublishReturns(new ushort[] { 0x00AB, 0x0001 });

        ctx.Server.Disconnect();

        // Les deux doivent etre de simples no-op (pas d'acces buffer) — l'absence d'exception EST
        // l'assertion. On les appelle plusieurs fois comme le ferait la boucle physique.
        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 3; i++)
            {
                ctx.Server.PullCommands();
                ctx.Server.PushReturns();
            }
        });
        Assert.Null(ex);
    }

    // Bout-en-bout : un client PERD la connexion apres Disconnect (une nouvelle connexion est
    // refusee), puis PEUT se reconnecter et relire les registres apres Reconnect. C'est la preuve
    // concrete de « perte de l'esclave » puis « rescrutation » cote M580 (validation manuelle Nico).
    [Fact]
    public void Client_perd_la_connexion_puis_se_reconnecte_apres_reparation()
    {
        using var ctx = Start();

        // Etat nominal : le serveur publie un retour, un client le lit.
        ctx.Store.PublishReturns(new ushort[] { 0x0000, 0x1234 });
        ctx.Server.PushReturns();
        int retBase = ctx.Pivot.GetZone("ret").Base;
        using (var before = Connect(ctx.Port, ModbusEndianness.BigEndian))
        {
            Span<ushort> read = before.ReadHoldingRegisters<ushort>(ctx.Pivot.UnitId, retBase + 1, 1);
            Assert.Equal((ushort)0x1234, read[0]);
        }

        // Coupure TCP : une NOUVELLE connexion doit desormais echouer (port ferme).
        ctx.Server.Disconnect();
        Assert.ThrowsAny<Exception>(() => Connect(ctx.Port, ModbusEndianness.BigEndian));

        // « Reparer » : le serveur re-ecoute. Un client se reconnecte et relit la MEME valeur
        // (unite + buffer conserves sur la meme instance, verdict du spike).
        ctx.Server.Reconnect();
        ctx.Server.PushReturns();   // republie apres re-ouverture (buffer neuf servi par FluentModbus)
        using var after = Connect(ctx.Port, ModbusEndianness.BigEndian);
        Span<ushort> readAfter = after.ReadHoldingRegisters<ushort>(ctx.Pivot.UnitId, retBase + 1, 1);
        Assert.Equal((ushort)0x1234, readAfter[0]);
    }
}
