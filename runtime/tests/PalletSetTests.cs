// =============================================================================
// PalletSetTests — le modele pur des palettes teste en isolation (sans Godot ni pivot)
// =============================================================================
//
// PalletSet est un objet C# pur : on l'instancie avec des angles/params a la main et on Advance
// avec un `dt` injecte, exactement comme CylinderStateTests/ConveyorStateTests. C'est ici qu'est
// VERROUILLE le point dur de la brique 4b : l'accumulation circulaire (couture 0°/360°, chaine de
// 3 palettes, verin = obstacle fixe). Le test `Trois_palettes_s_accumulent_derriere_un_poste` est
// le prototype exige par l'amorce avant de figer l'algorithme.

using CarrouselCore;
using Xunit;

namespace CarrouselCore.Tests;

public class PalletSetTests
{
    // Parametres du pivot reel, pour rester au plus pres de la machine V1.
    private const double Speed = 20.0;    // deg/s
    private const double Dt = 0.1;        // s  -> move = 2°/tick
    private const double MinGap = 20.0;   // deg
    private static readonly double[] NoBlock = System.Array.Empty<double>();

    // Fait tourner la simulation `ticks` fois avec le convoyeur en marche.
    private static void Run(PalletSet p, int ticks, params double[] blocked)
    {
        for (int i = 0; i < ticks; i++)
            p.Advance(Dt, conveyorRunning: true, Speed, blocked);
    }

    // =====================================================================
    // Rotation libre (aucun blocage)
    // =====================================================================

    [Fact]
    public void Rotation_libre_ccw_avance_en_angle_croissant()
    {
        var p = new PalletSet(new[] { 0.0, 120.0, 240.0 }, MinGap, ccw: true);
        Run(p, 5, NoBlock);   // 5 x 2° = 10°
        Assert.Equal(new[] { 10.0, 130.0, 250.0 }, Round(p.AnglesDeg));
    }

    [Fact]
    public void Rotation_libre_cw_avance_en_angle_decroissant()
    {
        // Meme mouvement, sens horaire : l'angle DIMINUE (la conversion interne Flip doit le gerer).
        var p = new PalletSet(new[] { 0.0, 120.0, 240.0 }, MinGap, ccw: false);
        Run(p, 5, NoBlock);   // 10° de course, en angle decroissant
        Assert.Equal(new[] { 350.0, 110.0, 230.0 }, Round(p.AnglesDeg));
    }

    [Fact]
    public void Rotation_libre_franchit_la_couture_360()
    {
        // Une palette pres de 360 doit repasser proprement par 0 (pas de saut, pas de blocage).
        var p = new PalletSet(new[] { 355.0 }, MinGap, ccw: true);
        Run(p, 5, NoBlock);   // 355 + 10 = 365 -> 5
        Assert.Equal(new[] { 5.0 }, Round(p.AnglesDeg));
    }

    [Fact]
    public void Convoyeur_arrete_fige_les_palettes()
    {
        var p = new PalletSet(new[] { 0.0, 120.0, 240.0 }, MinGap, ccw: true);
        p.Advance(Dt, conveyorRunning: false, Speed, NoBlock);
        Assert.Equal(new[] { 0.0, 120.0, 240.0 }, Round(p.AnglesDeg));
    }

    // =====================================================================
    // Blocage a un poste (verin engage = obstacle fixe)
    // =====================================================================

    [Fact]
    public void Une_palette_s_arrete_pile_au_poste_bloque()
    {
        // Palette a 80°, poste bloque a 90° : elle avance puis s'arrete EXACTEMENT a 90°.
        var p = new PalletSet(new[] { 80.0 }, MinGap, ccw: true);
        Run(p, 20, 90.0);   // largement de quoi atteindre le poste
        Assert.Equal(new[] { 90.0 }, Round(p.AnglesDeg));
    }

    [Fact]
    public void Palette_deja_franchie_n_est_pas_rappelee_en_arriere()
    {
        // Palette a 92° (a peine passe le poste 90°) : elle n'est PAS ramenee (dette D-002).
        var p = new PalletSet(new[] { 92.0 }, MinGap, ccw: true);
        Run(p, 3, 90.0);   // 92 -> 98, poursuit sa route
        Assert.Equal(new[] { 98.0 }, Round(p.AnglesDeg));
    }

    // =====================================================================
    // Accumulation — LE point dur (prototype exige par l'amorce)
    // =====================================================================

    [Fact]
    public void Trois_palettes_s_accumulent_derriere_un_poste()
    {
        // Trois palettes derriere YV1 sorti (poste 90°) : la tete s'arrete a 90, les suivantes
        // s'empilent a min_gap -> 90 / 70 / 50. C'est le scenario DoD de la brique 4b.
        var p = new PalletSet(new[] { 80.0, 55.0, 30.0 }, MinGap, ccw: true);
        Run(p, 40, 90.0);   // 40 ticks : temps large pour converger
        Assert.Equal(new[] { 90.0, 70.0, 50.0 }, Round(p.AnglesDeg));
    }

    [Fact]
    public void Accumulation_a_la_couture_360()
    {
        // Chaine qui franchit 0° : poste bloque a 10°, palettes a 8 / 348 / 328.
        // Tete a 10, suivantes a 350 et 330 (ecarts de 20°, a cheval sur la couture).
        var p = new PalletSet(new[] { 8.0, 348.0, 328.0 }, MinGap, ccw: true);
        Run(p, 40, 10.0);
        Assert.Equal(new[] { 10.0, 350.0, 330.0 }, Round(p.AnglesDeg));
    }

    [Fact]
    public void Accumulation_puis_liberation_reprennent_la_rotation()
    {
        // Une fois le poste debloque, la pile repart : plus de contrainte, rotation libre.
        var p = new PalletSet(new[] { 80.0, 55.0, 30.0 }, MinGap, ccw: true);
        Run(p, 40, 90.0);                       // accumulation -> 90/70/50
        Run(p, 5, NoBlock);                     // poste libere : +10° pour tout le monde
        Assert.Equal(new[] { 100.0, 80.0, 60.0 }, Round(p.AnglesDeg));
    }

    // =====================================================================
    // Presence (capteurs B1/B2)
    // =====================================================================

    [Fact]
    public void Presence_vraie_dans_la_fenetre_fausse_dehors()
    {
        var p = new PalletSet(new[] { 87.0 }, MinGap, ccw: true);   // a 3° du poste 90
        Assert.True(p.PresentAt(90.0, 8.0));    // fenetre ±4° : 3 <= 4 -> present
        Assert.False(p.PresentAt(270.0, 8.0));  // loin de l'autre poste
    }

    [Fact]
    public void Presence_bord_de_fenetre_inclus()
    {
        var p = new PalletSet(new[] { 94.0 }, MinGap, ccw: true);   // pile a 4° du poste 90
        Assert.True(p.PresentAt(90.0, 8.0));    // bord inclus (<=)
        var q = new PalletSet(new[] { 94.1 }, MinGap, ccw: true);   // juste au-dela
        Assert.False(q.PresentAt(90.0, 8.0));
    }

    [Fact]
    public void Presence_gere_la_couture_360()
    {
        // Poste a 2°, palette a 359° : distance angulaire = 3° <= 4°, present malgre la couture.
        var p = new PalletSet(new[] { 359.0 }, MinGap, ccw: true);
        Assert.True(p.PresentAt(2.0, 8.0));
    }

    [Fact]
    public void Palette_bloquee_au_poste_est_presente()
    {
        // Coherence blocage/presence : une palette arretee AU poste declenche le capteur.
        var p = new PalletSet(new[] { 80.0 }, MinGap, ccw: true);
        Run(p, 20, 90.0);
        Assert.True(p.PresentAt(90.0, 8.0));
    }

    // =====================================================================
    // Robustesse constructeur
    // =====================================================================

    [Fact]
    public void Construction_defensive()
    {
        Assert.Throws<System.ArgumentException>(() => new PalletSet(System.Array.Empty<double>(), MinGap, true));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new PalletSet(new[] { 0.0 }, -1.0, true));
    }

    // Arrondi a 3 decimales pour comparer des doubles issus d'accumulations de `move` (evite les
    // faux echecs a 1e-13 pres sans masquer une vraie derive).
    private static double[] Round(IReadOnlyList<double> angles)
    {
        var outp = new double[angles.Count];
        for (int i = 0; i < angles.Count; i++)
            outp[i] = System.Math.Round(angles[i], 3);
        return outp;
    }
}
