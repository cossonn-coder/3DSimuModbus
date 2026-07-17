// =============================================================================
// CarrouselSimulation — la boucle de simulation (« le cerveau »), brique 4
// =============================================================================
//
// Role : a chaque tick physique, transformer les COMMANDES du PLC (zone cmd) en RETOURS
// physiquement plausibles (zone ret), en faisant avancer la cinematique scriptee de `dt`.
// C'est la piece qui debloque la chaine reelle bout-en-bout : sans elle, le serveur ne
// renvoyait que ce qu'on lui donnait (transport nu). Objet C# PUR (CarrouselCore) : il ne
// touche NI le scene tree Godot NI le buffer serveur — seulement le ModbusDataStore, et
// seulement depuis le thread physique (frontiere Arch A, cf. docs/memory.md).
//
// --- Composition root --------------------------------------------------------------------
// Cette classe est le point ou le pivot (adresses + params) rencontre les modeles purs :
// au constructeur, elle lit les params machine et RESOUT une fois pour toutes les `Signal`
// (offsets + masques de bits) ; ensuite, chaque Tick ne fait que decoder/encoder via ces
// Signal. Les modeles purs (CylinderState, ConveyorState) ignorent tout du Modbus : ils ne
// manipulent que des grandeurs physiques. La traduction bit <-> etat vit ICI.
//
// --- Perimetre brique 4a -----------------------------------------------------------------
// 4a gere : heartbeat + verins (S11/S12/S21/S22) + retour convoyeur (KM1_AUX). Les bits de
// presence B1/B2 restent a 0 (pas encore de palettes) : c'est la brique 4b qui les remplira,
// de facon additive, en ajoutant un PalletSet et en etendant Tick. Aucun des 4 scenarios
// pytest full-chain ne depend de B1/B2 : 4a suffit a tous les debloquer.
//
// --- Sequence d'un Tick (D-e : reconstruction complete de ret) ---------------------------
//   1. snapshot des commandes (debut de tick)      -> coherence intra-scan
//   2. decodage des bits cmd (cmd_run, cmd_extend)
//   3. avancement de la cinematique de `dt`
//   4. increment heartbeat (rollover ushort naturel)
//   5. reconstruction d'un ushort[] ret NEUF, bits encodes un a un
//   6. publication d'un bloc (fin de tick)         -> le PLC voit toujours un jeu coherent

namespace CarrouselCore;

/// <summary>
/// Boucle de simulation cinematique (brique 4). <see cref="Tick"/> lit les commandes du datastore,
/// avance les modeles purs de <c>dt</c>, puis publie les retours (heartbeat + fins de course +
/// retour convoyeur). Pur, deterministe, sans dependance Godot.
/// </summary>
public sealed class CarrouselSimulation
{
	// Regroupe un verin (etat physique) avec les trois Signal qui le relient au bus : c'est le
	// « cablage » entre un modele pur et ses adresses Modbus, resolu une fois au constructeur.
	private sealed class CylinderUnit
	{
		// Id du composant dans le pivot (cylinder_1/cylinder_2) : sert de cle pour interroger le
		// FaultSet (GetPhysical) — c'est le meme identifiant que celui expose au menu de l'UI.
		public string Id { get; }
		public CylinderState State { get; }
		public Signal CmdExtend { get; }
		public Signal RetRetracted { get; }
		public Signal RetExtended { get; }

		// Angle du poste que ce verin bloque (deg). Quand le verin est ENGAGE, ce poste devient un
		// obstacle fixe pour les palettes (brique 4b) : c'est ce que Tick transmet a PalletSet.
		public double StationAngleDeg { get; }

		private CylinderUnit(string id, CylinderState state, Signal cmdExtend, Signal retRetracted, Signal retExtended, double stationAngleDeg)
		{
			Id = id;
			State = state;
			CmdExtend = cmdExtend;
			RetRetracted = retRetracted;
			RetExtended = retExtended;
			StationAngleDeg = stationAngleDeg;
		}

		public static CylinderUnit FromPivot(PivotModel pivot, string compId)
		{
			var c = pivot.GetComponent(compId);
			// Les temps du pivot sont en MILLISECONDES ; les modeles raisonnent en SECONDES
			// (comme `dt`). Conversion unique ici, jamais de melange d'unites plus loin.
			var state = new CylinderState(
				travelTimeS: c.GetParam("travel_time_ms") / 1000.0,
				retractedThreshold: c.GetParam("retracted_threshold"),
				extendedThreshold: c.GetParam("extended_threshold"),
				blockThreshold: c.GetParam("block_threshold"));

			return new CylinderUnit(
				c.Id,
				state,
				pivot.GetSignal(compId, "cmd_extend"),
				pivot.GetSignal(compId, "ret_retracted"),
				pivot.GetSignal(compId, "ret_extended"),
				c.GetParam("station_angle_deg"));
		}
	}

	// Regroupe un capteur de presence (B1/B2) avec le poste qu'il surveille et sa fenetre : c'est
	// le « cablage » entre PalletSet.PresentAt et l'adresse du bit ret_active, resolu au constructeur.
	private sealed class PresenceUnit
	{
		public Signal RetActive { get; }
		public double StationAngleDeg { get; }
		public double WindowDeg { get; }

		private PresenceUnit(Signal retActive, double stationAngleDeg, double windowDeg)
		{
			RetActive = retActive;
			StationAngleDeg = stationAngleDeg;
			WindowDeg = windowDeg;
		}

		public static PresenceUnit FromPivot(PivotModel pivot, string compId)
		{
			var c = pivot.GetComponent(compId);
			return new PresenceUnit(
				pivot.GetSignal(compId, "ret_active"),
				c.GetParam("station_angle_deg"),
				c.GetParam("window_deg"));
		}
	}

	private readonly Signal _heartbeatSig;

	private readonly Signal _cmdRun;
	private readonly Signal _km1Aux;
	private readonly ConveyorState _conveyor;

	// Id du composant convoyeur dans le pivot (= "conveyor", tag KM1). Cle pour interroger le
	// FaultSet (defaut physique ConveyorSlip) — memorise pour ne pas coder "conveyor" en dur.
	private readonly string _conveyorId;

	// Injection de defauts (sprint 5). Objet mutable partage : l'IHM le mute entre deux ticks,
	// le Tick le lit en tete. Pas de verrou (aucun thread serveur n'y touche, cf. FaultSet).
	// Un FaultSet vide => comportement STRICTEMENT nominal (aucun test full-chain perturbe).
	public FaultSet Faults { get; } = new();

	// Forcage des commandes (sprint 6). Meme contrat de partage que Faults : l'IHM le mute entre
	// deux ticks, le Tick le lit en tete (avant decodage). Pas de verrou (aucun thread serveur n'y
	// touche, cf. ForceSet). Un ForceSet vide => comportement STRICTEMENT nominal (aucun full-chain
	// perturbe : la valeur PLC passe telle quelle).
	public ForceSet Forces { get; } = new();

	// Table des signaux `ret` TOR indexes par (composant, nom), construite une fois au constructeur.
	// Sert a l'etape « capteur-bloque » du Tick : resoudre un (compId, sigName) actif en Signal pour
	// forcer son bit APRES l'encodage nominal. On ne garde QUE cette projection du pivot, pas le pivot.
	private readonly Dictionary<(string ComponentId, string SignalName), Signal> _retTorSignals = new();

	// Table des signaux `cmd` TOR indexes par (composant, nom), miroir exact de _retTorSignals mais
	// cote commande. Sert a l'etape « forcage » du Tick : resoudre un (compId, sigName) force en
	// Signal pour substituer son bit dans la copie snapshot AVANT decodage. Parcours generique de
	// TOUS les composants (cmd_run, cmd_extend, et n'importe quel bit `cmd` d'une machine future).
	private readonly Dictionary<(string ComponentId, string SignalName), Signal> _cmdTorSignals = new();

	private readonly CylinderUnit _cyl1;
	private readonly CylinderUnit _cyl2;

	// Palettes (brique 4b) : modele pur anime chaque tick. Sa vitesse est celle du convoyeur
	// (KM1.speed_deg_per_s) ; ses capteurs de presence pilotent B1/B2.
	private readonly PalletSet _pallets;
	private readonly double _palletSpeedDegPerS;
	private readonly PresenceUnit _b1;
	private readonly PresenceUnit _b2;

	// Compteur heartbeat : ushort => rollover a 65535 -> 0 automatique (spec : rollover libre).
	private ushort _heartbeat;

	/// <summary>
	/// Resout les signaux et params depuis le pivot, et construit les modeles purs. Aucune I/O,
	/// aucun tick : l'etat cinematique demarre au repos (verins rentres, convoyeur arrete).
	/// </summary>
	public CarrouselSimulation(PivotModel pivot)
	{
		ArgumentNullException.ThrowIfNull(pivot);

		_heartbeatSig = pivot.Heartbeat;

		var km1 = pivot.GetComponent("KM1");
		_conveyorId = km1.Id;
		_cmdRun = pivot.GetSignal("KM1", "cmd_run");
		_km1Aux = pivot.GetSignal("KM1", "ret_running");
		_conveyor = new ConveyorState(feedbackDelayS: km1.GetParam("feedback_delay_ms") / 1000.0);

		_cyl1 = CylinderUnit.FromPivot(pivot, "cylinder_1");
		_cyl2 = CylinderUnit.FromPivot(pivot, "cylinder_2");

		// Palettes : positions/ecart/sens viennent de kinematics ; la vitesse est celle du convoyeur.
		var k = pivot.Kinematics;
		_pallets = new PalletSet(k.InitialPositionsDeg, k.MinGapDeg, k.Ccw);
		_palletSpeedDegPerS = km1.GetParam("speed_deg_per_s");
		_b1 = PresenceUnit.FromPivot(pivot, "presence_station_1");
		_b2 = PresenceUnit.FromPivot(pivot, "presence_station_2");

		// Index des signaux `ret` TOR (compId, name) -> Signal, pour le masque capteur-bloque du
		// Tick. Parcours generique de TOUS les composants du pivot : couvre S11..S22, B1/B2 et
		// ret_running sans code specifique, et absorbera les signaux d'une machine future inconnue.
		// Index `cmd` TOR construit dans le MEME parcours (miroir de _retTorSignals, cf. sprint 6).
		foreach (var comp in pivot.Components.Values)
			foreach (var sig in comp.Signals.Values)
			{
				if (sig.IsTor && sig.Zone == "ret")
					_retTorSignals[(comp.Id, sig.Name)] = sig;
				else if (sig.IsTor && sig.Zone == "cmd")
					_cmdTorSignals[(comp.Id, sig.Name)] = sig;
			}
	}

	// --- Accces lecture seule a l'etat interne (pour la 3D brique 5 et les tests) ---
	// On expose les modeles : la 3D lira Position, les tests liront les seuils. Par convention
	// les appelants NE font que lire (ne rappellent pas Advance) : la seule horloge est le Tick.

	/// <summary>Verin YV1 (poste 90°).</summary>
	public CylinderState Cylinder1 => _cyl1.State;

	/// <summary>Verin YV2 (poste 270°).</summary>
	public CylinderState Cylinder2 => _cyl2.State;

	/// <summary>Retour de marche du convoyeur (contact KM1_AUX).</summary>
	public ConveyorState Conveyor => _conveyor;

	/// <summary>Etat des palettes (positions angulaires) — lu par la 3D (brique 5) et les tests.</summary>
	public PalletSet Pallets => _pallets;

	/// <summary>Valeur courante du compteur heartbeat (dernier publie).</summary>
	public ushort Heartbeat => _heartbeat;

	/// <summary>
	/// Un pas de simulation : snapshot cmd -> avance la cinematique de <paramref name="dtSeconds"/>
	/// -> incremente le heartbeat -> reconstruit et publie la zone ret.
	/// </summary>
	public void Tick(ModbusDataStore store, double dtSeconds)
	{
		ArgumentNullException.ThrowIfNull(store);

		// 1. Snapshot des commandes en debut de tick : la sim travaille sur une photo figee,
		//    insensible a une ecriture PLC qui arriverait en cours de calcul (coherence intra-scan).
		ushort[] cmd = store.SnapshotCommands();

		// 1bis. Forcage (sprint 6) : on substitue les bits `cmd` forces dans la COPIE snapshot (jamais
		//       le datastore, invariant identique au masque capteur `ret`). Symetrique de
		//       Faults.ActiveStucks, mais cote `cmd` et AVANT decodage : tout le pipeline en aval
		//       (run/extend, physique, KM1_AUX) voit la commande EFFECTIVE. Substituer a 1 un bit que
		//       le PLC laisse a 0 = piloter sans/malgre le PLC ; a 0 un bit a 1 = neutraliser la commande.
		foreach (var (compId, sigName, mode) in Forces.ActiveForces)
		{
			// Defensif : un forcage sur un signal absent de l'index (compId/nom errone) est ignore
			// plutot que de faire planter le tick — l'UI ne propose que des signaux `cmd` valides.
			if (_cmdTorSignals.TryGetValue((compId, sigName), out var sig))
				sig.WriteBit(ref cmd[sig.WordRel], mode == ForceMode.ForceHigh);
		}

		// 2. Decodage des bits de commande via les Signal (offset relatif + masque). Jamais
		//    d'adresse ni de masque en dur : tout passe par le pivot (decision D-b).
		bool run = _cmdRun.ReadBit(cmd[_cmdRun.WordRel]);
		bool extend1 = _cyl1.CmdExtend.ReadBit(cmd[_cyl1.CmdExtend.WordRel]);
		bool extend2 = _cyl2.CmdExtend.ReadBit(cmd[_cyl2.CmdExtend.WordRel]);

		// 3. Avancement de la cinematique scriptee de dt (chaque modele est autonome).
		//    Le contact KM1_AUX suit TOUJOURS la commande (meme si le convoyeur patine : voir 3bis).
		_conveyor.Advance(dtSeconds, run);
		// Verins : un defaut physique detourne l'avancement (position gelee, ou commande forcee faux).
		AdvanceCylinder(_cyl1, dtSeconds, extend1);
		AdvanceCylinder(_cyl2, dtSeconds, extend2);

		// 3bis. Palettes (brique 4b) : un verin ENGAGE (position > block_threshold) transforme son
		//       poste en obstacle fixe. On collecte les postes bloques APRES l'avance des verins
		//       (etat courant), puis on avance les palettes ; leur rotation suit le convoyeur REEL
		//       (IsRunning = KM1_AUX), pas la commande brute, coherent avec le comportement physique.
		//       Defaut ConveyorSlip : le convoyeur « patine » — KM1_AUX reste a 1 (marche confirmee)
		//       mais les palettes n'avancent plus. On force donc conveyorRunning=false ICI seulement,
		//       sans toucher a _conveyor (dont l'etat reste la marche reelle vue par le PLC).
		bool slip = Faults.GetPhysical(_conveyorId) == PhysicalFault.ConveyorSlip;
		bool palletsRunning = _conveyor.IsRunning && !slip;
		double[] blocked = CollectBlockedStations();
		_pallets.Advance(dtSeconds, palletsRunning, _palletSpeedDegPerS, blocked);

		// 3ter. Gel `ret` (RetFrozen) : la physique ci-dessus a bien tourne (la 3D bougera), mais on
		//       n'incremente PAS le heartbeat et on ne publie PAS — le datastore conserve ses derniers
		//       `ret`. Le PLC voit une image totalement figee (detection de sim morte). Tout-ou-rien.
		if (Faults.RetFrozen)
			return;

		// 4. Heartbeat : +1 par tick, rollover ushort naturel (preuve de vie pour le PLC).
		_heartbeat++;

		// 5. Reconstruction COMPLETE de ret (D-e) : un tableau neuf, tous les bits repositionnes.
		//    Le heartbeat est un mot ENTIER (word 0) ; les TOR (word 1) sont ecrits bit a bit.
		ushort[] ret = new ushort[store.ReturnWordCount];
		ret[_heartbeatSig.WordRel] = _heartbeat;

		_km1Aux.WriteBit(ref ret[_km1Aux.WordRel], _conveyor.IsRunning);
		WriteCylinder(ret, _cyl1);
		WriteCylinder(ret, _cyl2);

		// B1/B2 (brique 4b) : presence d'une palette dans la fenetre de chaque poste.
		WritePresence(ret, _b1);
		WritePresence(ret, _b2);

		// 5bis. Capteurs bloques : APRES l'encodage nominal, on force chaque bit `ret` TOR marque
		//       stuck a 0 (Low) ou 1 (High). C'est un MASQUE cote sim — on ne force jamais le
		//       datastore lui-meme (invariant D-016). Generique : couvre S11..S22, B1/B2, KM1_AUX.
		foreach (var (compId, sigName, mode) in Faults.ActiveStucks)
		{
			// Defensif : un stuck sur un signal absent de l'index (compId/nom errone) est ignore
			// plutot que de faire planter le tick — l'UI ne propose que des signaux valides.
			if (_retTorSignals.TryGetValue((compId, sigName), out var sig))
				sig.WriteBit(ref ret[sig.WordRel], mode == StuckMode.High);
		}

		// 6. Publication d'un bloc en fin de tick : le PLC lira un jeu de retours coherent.
		store.PublishReturns(ret);
	}

	/// <summary>
	/// Avance un verin en tenant compte de son defaut physique (sprint 5) :
	/// <list type="bullet">
	/// <item><see cref="PhysicalFault.CylinderStuckMidStroke"/> : on n'appelle PAS Advance — la
	/// position reste figee a sa valeur courante (verin coince mecaniquement).</item>
	/// <item><see cref="PhysicalFault.CylinderStuckRetracted"/> : Advance avec commande forcee a
	/// faux — l'electrovanne est comme non alimentee, le ressort ramene/maintient la tige rentree.</item>
	/// <item>sinon : comportement nominal (Advance avec la vraie commande).</item>
	/// </list>
	/// </summary>
	private void AdvanceCylinder(CylinderUnit u, double dt, bool cmdExtend)
	{
		switch (Faults.GetPhysical(u.Id))
		{
			case PhysicalFault.CylinderStuckMidStroke:
				break;   // position gelee : aucun avancement
			case PhysicalFault.CylinderStuckRetracted:
				u.State.Advance(dt, false);   // commande effective forcee faux
				break;
			default:
				u.State.Advance(dt, cmdExtend);
				break;
		}
	}

	/// <summary>Encode les deux fins de course d'un verin dans la zone ret.</summary>
	private static void WriteCylinder(ushort[] ret, CylinderUnit u)
	{
		u.RetRetracted.WriteBit(ref ret[u.RetRetracted.WordRel], u.State.IsRetracted);
		u.RetExtended.WriteBit(ref ret[u.RetExtended.WordRel], u.State.IsExtended);
	}

	/// <summary>
	/// Postes actuellement bloques : l'angle de chaque verin ENGAGE (un verin rentre ne bloque rien).
	/// Ces angles deviennent des obstacles fixes pour <see cref="PalletSet.Advance"/>.
	/// </summary>
	private double[] CollectBlockedStations()
	{
		var blocked = new List<double>(2);
		// Defaut BlockerIneffective (sprint 6) : la tige sort NORMALEMENT (S11/S12 nominaux, AdvanceCylinder
		// inchange), mais le poste est EXCLU du blocage — un verin engage n'ajoute son poste que si ce
		// defaut n'est pas actif. Signature : les palettes traversent une tige pourtant levee.
		if (_cyl1.State.IsEngaged && Faults.GetPhysical(_cyl1.Id) != PhysicalFault.BlockerIneffective)
			blocked.Add(_cyl1.StationAngleDeg);
		if (_cyl2.State.IsEngaged && Faults.GetPhysical(_cyl2.Id) != PhysicalFault.BlockerIneffective)
			blocked.Add(_cyl2.StationAngleDeg);
		return blocked.ToArray();
	}

	/// <summary>Encode le bit de presence d'un poste (B1/B2) : palette dans la fenetre du capteur.</summary>
	private void WritePresence(ushort[] ret, PresenceUnit u)
	{
		bool present = _pallets.PresentAt(u.StationAngleDeg, u.WindowDeg);
		u.RetActive.WriteBit(ref ret[u.RetActive.WordRel], present);
	}
}
