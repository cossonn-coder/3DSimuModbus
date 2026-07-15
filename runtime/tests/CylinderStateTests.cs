// =============================================================================
// CylinderStateTests — cinematique du verin monostable, testee en isolation (sans Godot)
// =============================================================================
//
// On injecte des `dt` fixes et on observe position + seuils. Params calques sur le pivot reel
// (travel 0.5 s, seuils 2 %/98 %/10 %) mais poses en dur ICI : ces tests verrouillent la LOGIQUE
// du modele, pas le mapping (celui-ci est teste dans PivotModelTests).

using CarrouselCore;
using Xunit;

namespace CarrouselCore.Tests;

public class CylinderStateTests
{
    private static CylinderState NewCylinder() => new(travelTimeS: 0.5, retractedThreshold: 0.02, extendedThreshold: 0.98, blockThreshold: 0.10);

    [Fact]
    public void Etat_initial_rentre()
    {
        var c = NewCylinder();
        Assert.Equal(0.0, c.Position);
        Assert.True(c.IsRetracted);
        Assert.False(c.IsExtended);
        Assert.False(c.IsEngaged);
    }

    [Fact]
    public void Sortie_complete_apres_travel_time()
    {
        var c = NewCylinder();
        // 0.5 s de course a 0.1 s/tick => 5 ticks pour atteindre 1.0.
        for (int i = 0; i < 5; i++)
            c.Advance(0.1, cmdExtend: true);

        Assert.Equal(1.0, c.Position, precision: 6);
        Assert.True(c.IsExtended);
        Assert.False(c.IsRetracted);
        Assert.True(c.IsEngaged);
    }

    [Fact]
    public void Ne_depasse_jamais_un()
    {
        var c = NewCylinder();
        for (int i = 0; i < 20; i++)   // bien au-dela de la course
            c.Advance(0.1, cmdExtend: true);
        Assert.Equal(1.0, c.Position, precision: 6);   // clampe, pas 2.0
    }

    [Fact]
    public void Rappel_ressort_ramene_a_zero()
    {
        var c = NewCylinder();
        for (int i = 0; i < 5; i++) c.Advance(0.1, cmdExtend: true);   // sorti
        for (int i = 0; i < 5; i++) c.Advance(0.1, cmdExtend: false);  // commande retiree -> ressort

        Assert.Equal(0.0, c.Position, precision: 6);
        Assert.True(c.IsRetracted);
        Assert.False(c.IsExtended);
    }

    [Fact]
    public void Engage_avant_fin_de_course()
    {
        // A 10 % la palette est deja bloquee (IsEngaged), bien avant la fin de course (98 %).
        var c = NewCylinder();
        c.Advance(0.1, cmdExtend: true);   // 0.1 s / 0.5 s = 0.2 (20 %)
        Assert.True(c.IsEngaged);
        Assert.False(c.IsExtended);
    }

    [Fact]
    public void Inversion_mi_course_repart_du_point_courant()
    {
        var c = NewCylinder();
        // 0.25 s de sortie => position 0.5 (mi-course).
        c.Advance(0.25, cmdExtend: true);
        Assert.Equal(0.5, c.Position, precision: 6);

        // Inversion : on retire la commande. La tige doit REDESCENDRE depuis 0.5, pas sauter.
        c.Advance(0.1, cmdExtend: false);
        Assert.Equal(0.3, c.Position, precision: 6);   // 0.5 - 0.1/0.5 = 0.5 - 0.2

        // Re-inversion immediate : elle repart vers le haut depuis 0.3.
        c.Advance(0.1, cmdExtend: true);
        Assert.Equal(0.5, c.Position, precision: 6);
    }

    [Fact]
    public void Travel_time_invalide_echoue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CylinderState(0.0, 0.02, 0.98, 0.10));
    }
}
