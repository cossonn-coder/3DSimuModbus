// =============================================================================
// HealthHud — overlay 2D LECTURE SEULE de la sante du demonstrateur (S3.2)
// =============================================================================
//
// Role : rendre VISIBLE a l'ecran l'etat de sante du demonstrateur, pour qu'un
// souci ne se traduise plus jamais par une « maquette figee sans explication »
// (dette D-013, vecue en demo sprint 2). Deux elements distincts :
//
//   1. BANDEAU D'ECHEC (rouge, en haut) : n'apparait QUE si le serveur Modbus n'a
//      pas pu ecouter (port 502 occupe). C'est la reponse directe a D-013 : la
//      maquette peut rester figee, mais l'utilisateur SAIT desormais pourquoi.
//      Ce bandeau prime sur tout et ne peut PAS etre masque par le toggle (une
//      panne ne doit jamais pouvoir etre cachee).
//
//   2. PANNEAU SANTE (discret, coin haut-gauche) : etat du serveur, heartbeat qui
//      defile, activite PLC (derniere trame cmd recue). Masquable via ShowHud.
//
// --- Frontiere Arch A (thread-safety) --------------------------------------------------
// Ce noeud est de la GLUE Godot pure. Son _Process tourne sur le THREAD PRINCIPAL Godot.
// Il ne fait QUE LIRE des proprietes deja rendues thread-safe cote S3.1 :
//   - _sim.Heartbeat            (ushort, ecrit par le tick physique, meme thread)
//   - _server.IsListening       (bool, ecrit a Start sur le meme thread)
//   - _server.LastClientWriteUtc(compose atomiquement via Interlocked cote serveur)
// AUCUNE lecture du datastore, AUCUN acces au scene tree 3D : la frontiere Arch A tient.
//
// --- S5.4 : le HUD PILOTE desormais deux defauts de comm GLOBAUX (pas seulement observer) ----
// Nouveaute : deux boutons injectent un defaut de communication, tous deux actionnes sur le
// THREAD PRINCIPAL Godot (les signaux d'UI Godot firent sur ce thread) — donc sans partage
// inter-thread avec le thread serveur FluentModbus :
//   - « Geler retours » -> _sim.Faults.RetFrozen (la sim cesse de publier ret + heartbeat ;
//     le M580 lit toujours, mais des donnees figees => son watchdog reagit) ;
//   - « Couper TCP »    -> _server.Disconnect()/Reconnect() (perte de l'esclave cote I/O Scanner).
// Le HUD pilote donc le SERVEUR et le FaultSet, mais TOUJOURS PAS le datastore ni le scene tree :
// la frontiere Arch A (« la glue ne touche pas les internes thread-safe ») reste respectee. Un
// re-bind qui echoue a la reconnexion est REMONTE via le bandeau (D-013), jamais avale.
//
// --- Cadence de rafraichissement -------------------------------------------------------
// Le texte est rafraichi a BASSE cadence (~5 Hz) via un accumulateur sur _Process(delta),
// et NON a 60 Hz : un compteur qui change 60 fois/s scintille et fatigue l'oeil sans rien
// apporter (la lisibilite humaine se contente de 5 Hz). Le moteur, lui, tourne toujours a
// 60 Hz — seul l'affichage du texte est ralenti.

using CarrouselCore;
using CarrouselServer;
using Godot;

/// <summary>
/// Overlay 2D lecture seule affichant la sante du demonstrateur : bandeau d'echec de bind
/// (solde D-013) + panneau santé (serveur, heartbeat, activité PLC). Cree par code depuis
/// <see cref="CarrouselScene"/> et alimente par references lecture seule (<see cref="Configure"/>).
/// </summary>
public partial class HealthHud : CanvasLayer
{
    // Toggle du PANNEAU SANTE uniquement. Le bandeau d'echec, lui, s'affiche quoi qu'il
    // arrive : masquer une panne serait pire que le mal. Lu par Godot avant _Ready (valeur
    // figee dans l'inspecteur/.tscn), d'ou [Export].
    [Export] public bool ShowHud { get; set; } = true;

    // --- References lecture seule (Arch A : on LIT, on n'ecrit jamais) -------------------
    // Injectees par CarrouselScene apres AddChild via Configure(). Le serveur peut avoir
    // echoue a demarrer (_server existe quand meme, IsListening == false) ; la simulation
    // existe toujours (creee avant le Start dans _Ready).
    private ModbusServer _server = null!;
    private CarrouselSimulation _sim = null!;
    private int _port;   // pour l'affichage « à l'écoute :<port> » — vient du pivot, jamais en dur

    // --- Noeuds d'interface, construits une fois a _Ready --------------------------------
    private Panel _errorBanner = null!;
    private Label _errorLabel = null!;
    private Panel _healthPanel = null!;
    private Label _healthLabel = null!;

    // --- Contrometrie de comm (S5.4) : deux toggles injectant un defaut de communication ------
    // Toujours visibles (non masques par ShowHud) : un defaut de comm ACTIF ne doit jamais etre
    // cache, au meme titre que le bandeau d'echec (invariant « une panne ne se cache pas »). Leur
    // etat enfonce = defaut actif, ce qui rend l'injection lisible sans lire le M580.
    private Panel _commPanel = null!;
    private CheckButton _freezeButton = null!;   // « Geler retours » -> RetFrozen
    private CheckButton _cutButton = null!;       // « Couper TCP »   -> Disconnect/Reconnect

    // Accumulateur du rafraichissement basse cadence (secondes). Voir _Process.
    private double _refreshAccumulator;
    private const double RefreshPeriodS = 0.2;   // ~5 Hz

    // Etat d'echec du bind, pousse par CarrouselScene (ShowBindFailure). Distinct de
    // !IsListening car il porte AUSSI le message d'erreur a afficher.
    private bool _serverFailed;

    /// <summary>
    /// Construit l'interface (bandeau + panneau) une seule fois. Les noeuds existent donc
    /// des la fin de l'AddChild cote CarrouselScene, ce qui rend surs les appels ulterieurs
    /// a <see cref="Configure"/> et <see cref="ShowBindFailure"/> (AddChild declenche _Ready
    /// de facon synchrone).
    /// </summary>
    public override void _Ready()
    {
        BuildErrorBanner();
        BuildHealthPanel();
        BuildCommControls();
    }

    /// <summary>
    /// Injecte les references lecture seule dont le HUD a besoin pour s'auto-rafraichir.
    /// Appelee par <see cref="CarrouselScene"/> juste apres l'AddChild du HUD. Le HUD tire
    /// ensuite lui-meme les valeurs a basse cadence — CarrouselScene n'a rien a pousser par frame.
    /// </summary>
    /// <param name="server">Serveur Modbus (source de IsListening / LastClientWriteUtc).</param>
    /// <param name="sim">Simulation (source du heartbeat).</param>
    /// <param name="port">Port d'ecoute resolu du pivot, pour l'affichage.</param>
    public void Configure(ModbusServer server, CarrouselSimulation sim, int port)
    {
        _server = server;
        _sim = sim;
        _port = port;
    }

    /// <summary>
    /// Active le bandeau rouge d'echec de bind avec le message de la
    /// <see cref="ModbusServerException"/>. Appelee par CarrouselScene sur le chemin d'echec.
    /// C'est la manifestation visible qui SOLDE D-013 : plus jamais de maquette figee muette.
    /// </summary>
    public void ShowBindFailure(string message)
    {
        _serverFailed = true;
        _errorLabel.Text = "serveur Modbus KO — " + message;
        _errorBanner.Visible = true;
    }

    /// <summary>
    /// Boucle d'affichage. Tourne a 60 Hz (thread principal Godot) mais ne rafraichit le
    /// TEXTE qu'a ~5 Hz (accumulateur) pour eviter le scintillement. Lecture seule stricte.
    /// </summary>
    public override void _Process(double delta)
    {
        // Le toggle ne concerne QUE le panneau santé ; le bandeau d'echec reste visible.
        _healthPanel.Visible = ShowHud;

        _refreshAccumulator += delta;
        if (_refreshAccumulator < RefreshPeriodS)
            return;
        _refreshAccumulator = 0;

        if (ShowHud)
            RefreshHealthText();
    }

    /// <summary>
    /// Recompose le texte du panneau santé a partir des trois sources lecture seule.
    /// Regle D-Q3 : ne jamais mentir. L'activite PLC vient de LastClientWriteUtc, fiabilisee
    /// en S3.1 (event RegistersChanged arme) — on affiche donc une vraie fraicheur, pas « n/d ».
    /// </summary>
    private void RefreshHealthText()
    {
        // Ligne serveur : etat d'ecoute reel. En cas d'echec de bind, on l'affiche sans detour.
        string serverLine = _serverFailed
            ? "serveur : ÉCHEC (port occupé)"
            : _server.IsListening
                ? $"serveur : à l'écoute :{_port}"
                : "serveur : arrêté";

        // Ligne heartbeat : doit defiler tant que la sim tourne (preuve de vie cote PLC).
        string heartbeatLine = $"heartbeat : {_sim.Heartbeat}";

        // Ligne PLC : fraicheur de la derniere ecriture cmd (FC16) recue d'un client.
        System.DateTime? last = _server.LastClientWriteUtc;
        string plcLine;
        if (last is null)
        {
            plcLine = "PLC : aucune trame reçue";
        }
        else
        {
            double ms = (System.DateTime.UtcNow - last.Value).TotalMilliseconds;
            plcLine = $"PLC : dernière trame il y a {ms:F0} ms";
        }

        // Ligne comm (S5.4) : etat combine des deux defauts de comm globaux. On distingue « TCP
        // coupe » (plus d'ecoute, mais pas un echec de bind) de « ret fige » (TCP vivant, donnees
        // immobiles). Les deux peuvent coexister. Regle D-Q3 : ne jamais mentir sur l'etat comm.
        _healthLabel.Text = serverLine + "\n" + heartbeatLine + "\n" + plcLine + "\n" + CommStatusLine();
    }

    /// <summary>
    /// Compose la ligne d'etat comm (S5.4) a partir des deux defauts globaux : gel <c>ret</c>
    /// (<see cref="CarrouselSimulation"/>.<c>Faults.RetFrozen</c>) et coupure TCP (deduite de
    /// <c>!IsListening</c> hors echec de bind). Les deux sont independants et cumulables ; l'echec
    /// de bind au demarrage prime (le serveur n'a jamais ecoute — il n'y a pas de comm a qualifier).
    /// </summary>
    private string CommStatusLine()
    {
        if (_serverFailed)
            return "comm : serveur KO";

        bool retFrozen = _sim.Faults.RetFrozen;
        bool tcpCut = !_server.IsListening;   // hors _serverFailed (traite ci-dessus) => coupure volontaire

        if (tcpCut && retFrozen)
            return "comm : TCP coupé + ret figé";
        if (tcpCut)
            return "comm : TCP coupé (perte de l'esclave)";
        if (retFrozen)
            return "comm : ret figé (heartbeat immobile)";
        return "comm : nominal";
    }

    // --- Handlers des deux toggles de comm (S5.4) ----------------------------------------------

    /// <summary>
    /// « Geler retours » : branche l'interrupteur UI sur le defaut deja modelise cote sim (S5.1).
    /// Le gel est enforce DANS la simulation (elle cesse d'incrementer le heartbeat et de publier
    /// ret) — ici on ne fait QUE positionner le drapeau, sur le thread principal. Le M580 continue
    /// de lire un fil vivant mais fige : c'est son watchdog applicatif qui doit reagir.
    /// </summary>
    private void OnFreezeToggled(bool pressed)
    {
        _sim.Faults.RetFrozen = pressed;
    }

    /// <summary>
    /// « Couper TCP » : defaut de TRANSPORT (couche serveur, distincte du gel de donnees). Enfonce
    /// => <see cref="ModbusServer.Disconnect"/> ferme l'ecoute (l'I/O Scanner perd l'esclave) ;
    /// relache (« Reparer ») => <see cref="ModbusServer.Reconnect"/> re-ouvre le port. Un re-bind
    /// qui echoue (port repris) leve une <see cref="ModbusServerException"/> : on la REMONTE via le
    /// bandeau (D-013, jamais avalee) et on RE-ENFONCE le bouton sans re-emettre le signal
    /// (SetPressedNoSignal) — on ne pretend pas avoir retabli une comm qui ne l'est pas.
    /// </summary>
    private void OnCutToggled(bool pressed)
    {
        if (pressed)
        {
            _server.Disconnect();
            return;
        }

        try
        {
            _server.Reconnect();
        }
        catch (ModbusServerException ex)
        {
            ShowBindFailure(ex.Message);
            _cutButton.SetPressedNoSignal(true);   // reste « coupe » : la reparation a echoue
        }
    }

    // --- Construction de l'interface -----------------------------------------------------

    /// <summary>
    /// Bandeau d'echec : Panel rouge pleine largeur en haut, invisible par defaut. On surcharge
    /// le stylebox « panel » plutot que d'utiliser un ColorRect pour rester coherent avec le
    /// panneau santé et poser le Label par-dessus proprement.
    /// </summary>
    private void BuildErrorBanner()
    {
        _errorBanner = new Panel { Visible = false };
        _errorBanner.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _errorBanner.OffsetBottom = 48;   // hauteur du bandeau
        _errorBanner.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.72f, 0.10f, 0.10f) });

        _errorLabel = new Label();
        _errorLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _errorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _errorLabel.VerticalAlignment = VerticalAlignment.Center;
        _errorLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _errorBanner.AddChild(_errorLabel);

        AddChild(_errorBanner);
    }

    /// <summary>
    /// Panneau santé : Panel sombre semi-opaque dans le coin haut-gauche, avec un Label
    /// multi-lignes. Position/taille en dur PUREMENT COSMETIQUES (mobilier d'ecran hors pivot,
    /// comme la camera d'inspection) — aucune de ces valeurs n'est une adresse ou un temps de sim.
    /// </summary>
    private void BuildHealthPanel()
    {
        _healthPanel = new Panel
        {
            Position = new Vector2(8, 8),
            Size = new Vector2(300, 104),   // S5.4 : +1 ligne (etat comm) => panneau un peu plus haut
        };
        _healthPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.55f) });

        _healthLabel = new Label
        {
            Position = new Vector2(10, 8),
            Text = "serveur : …\nheartbeat : …\nPLC : …",
        };
        _healthLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.95f, 1f));
        _healthPanel.AddChild(_healthLabel);

        AddChild(_healthPanel);
    }

    /// <summary>
    /// Contrometrie de comm (S5.4) : un petit panneau sous le panneau santé, avec deux CheckButton
    /// (toggles) pour injecter les deux defauts de communication globaux. TOUJOURS visible (pas de
    /// gate ShowHud) : un defaut de comm actif reste toujours a l'ecran, comme le bandeau d'echec.
    /// Position/taille en dur PUREMENT COSMETIQUES (mobilier d'ecran hors pivot). Les CheckButton
    /// sont en mode toggle : leur signal Toggled fournit directement l'etat enfonce/relache.
    /// </summary>
    private void BuildCommControls()
    {
        _commPanel = new Panel
        {
            Position = new Vector2(8, 120),   // juste sous le panneau santé (haut-gauche)
            Size = new Vector2(300, 78),
        };
        _commPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.55f) });

        // VBox pour empiler les deux toggles proprement, avec une petite marge interne.
        var box = new VBoxContainer { Position = new Vector2(10, 8), Size = new Vector2(280, 62) };

        _freezeButton = new CheckButton { Text = "Geler retours (ret figé)" };
        _freezeButton.Toggled += OnFreezeToggled;

        _cutButton = new CheckButton { Text = "Couper TCP (perte esclave)" };
        _cutButton.Toggled += OnCutToggled;

        box.AddChild(_freezeButton);
        box.AddChild(_cutButton);
        _commPanel.AddChild(box);

        AddChild(_commPanel);
    }
}
