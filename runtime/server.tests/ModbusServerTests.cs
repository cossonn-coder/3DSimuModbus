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
}
