// =============================================================================
// SimHost — hote headless de la simulation (brique 4a)
// =============================================================================
//
// Assemble la chaine complete et la fait tourner en boucle, hors Godot :
//     PivotModel  ->  ModbusDataStore  <->  ModbusServer (FC3/FC16 sur le port 502)
//                              ^
//                     CarrouselSimulation.Tick
//
// A chaque tick physique (cadence = heartbeat.period_ms du pivot), on enchaine :
//     server.PullCommands();   // fil (M580) -> datastore   [sous server.Lock]
//     sim.Tick(store, dt);     // cinematique : decode cmd, avance, encode ret
//     server.PushReturns();    // datastore -> fil (M580)    [sous server.Lock]
//
// C'est le meme enchainement que le futur _PhysicsProcess Godot (brique 5). Ici, la seule
// horloge est un Stopwatch : `dt` est le temps REEL ecoule entre deux ticks (pas un pas fige),
// pour que la cinematique reste juste meme si un tick derape.

using System.Diagnostics;
using System.Net;
using CarrouselCore;
using CarrouselServer;

// --- Resolution du pivot : on remonte l'arborescence jusqu'a pivot/machine_carrousel.json,
//     comme les tests. Robuste quel que soit le repertoire de lancement. ---
static string FindPivot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        string candidate = Path.Combine(dir.FullName, "pivot", "machine_carrousel.json");
        if (File.Exists(candidate))
            return candidate;
        dir = dir.Parent;
    }
    throw new InvalidOperationException("pivot/machine_carrousel.json introuvable depuis " + AppContext.BaseDirectory);
}

var pivot = PivotModel.Load(FindPivot());
var store = new ModbusDataStore(pivot);
var sim = new CarrouselSimulation(pivot);

// Interface d'ecoute : loopback par defaut (test local pytest, pas de dialogue pare-feu Windows).
// "--any" pour ecouter sur toutes les interfaces, quand on veut brancher un vrai M580 distant.
IPAddress bind = args.Contains("--any") ? IPAddress.Any : IPAddress.Loopback;

using var server = new ModbusServer(pivot, store, bind);
server.Start();

Console.WriteLine(
    $"SimHost : serveur Modbus TCP sur {bind}:{pivot.Port} (unit {pivot.UnitId}), " +
    $"tick {pivot.HeartbeatPeriodMs} ms. Ctrl+C pour arreter.");

// --- Arret propre sur Ctrl+C : on annule l'arret brutal du process et on sort de la boucle. ---
using var stop = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;   // empeche la terminaison immediate : on veut disposer le serveur proprement
    stop.Set();
};

// --- Boucle physique cadencee. Le Stopwatch mesure le dt REEL entre deux iterations. ---
int periodMs = pivot.HeartbeatPeriodMs;
var clock = Stopwatch.StartNew();
double previous = clock.Elapsed.TotalSeconds;

while (!stop.IsSet)
{
    double now = clock.Elapsed.TotalSeconds;
    double dt = now - previous;
    previous = now;

    // Les trois memes appels que _PhysicsProcess (brique 5) : pull commandes, tick, push retours.
    server.PullCommands();
    sim.Tick(store, dt);
    server.PushReturns();

    // Cadence : on attend la fin de la periode (interruptible par Ctrl+C via le Wait sur le handle).
    stop.Wait(periodMs);
}

Console.WriteLine("SimHost : arret.");
