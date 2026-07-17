// =============================================================================
// ForceSetTests — le coeur de FORCAGE des commandes, teste sans Godot (sprint 6)
// =============================================================================
//
// Miroir de FaultSetTests (partie pure) : mutations, dispatch, lectures de ForceSet, sans
// pivot ni simulation. L'INJECTION du forcage dans CarrouselSimulation (piloter sans/malgre
// le PLC, composition avec un defaut) est verifiee dans CarrouselSimulationTests, la ou la
// vraie chaine pivot -> datastore -> sim est branchee.

using CarrouselCore;
using Xunit;

namespace CarrouselCore.Tests;

public class ForceSetTests
{
    [Fact]
    public void SetForce_Auto_efface_le_forcage()
    {
        var fs = new ForceSet();
        fs.SetForce("cylinder_1", "cmd_extend", ForceMode.ForceHigh);
        Assert.Equal(ForceMode.ForceHigh, fs.GetForce("cylinder_1", "cmd_extend"));
        Assert.True(fs.HasAnyForce("cylinder_1"));

        fs.SetForce("cylinder_1", "cmd_extend", ForceMode.Auto);
        Assert.Equal(ForceMode.Auto, fs.GetForce("cylinder_1", "cmd_extend"));
        Assert.False(fs.HasAnyForce("cylinder_1"));
    }

    [Fact]
    public void GetForce_rend_Auto_par_defaut()
    {
        var fs = new ForceSet();
        Assert.Equal(ForceMode.Auto, fs.GetForce("cylinder_1", "cmd_extend"));
        Assert.False(fs.HasAnyForce("cylinder_1"));
    }

    [Fact]
    public void Apply_dispatche_le_forcage()
    {
        var fs = new ForceSet();

        fs.Apply(new ForceCommand("conveyor", "cmd_run", ForceMode.ForceHigh));
        Assert.Equal(ForceMode.ForceHigh, fs.GetForce("conveyor", "cmd_run"));

        // Apply(Auto) revient au nominal (symetrique de SetForce(Auto)).
        fs.Apply(new ForceCommand("conveyor", "cmd_run", ForceMode.Auto));
        Assert.Equal(ForceMode.Auto, fs.GetForce("conveyor", "cmd_run"));
    }

    [Fact]
    public void ClearComponent_efface_les_forcages_du_composant_seulement()
    {
        var fs = new ForceSet();
        fs.SetForce("cylinder_1", "cmd_extend", ForceMode.ForceHigh);
        fs.SetForce("cylinder_2", "cmd_extend", ForceMode.ForceLow);   // autre composant : preserve

        fs.ClearComponent("cylinder_1");

        Assert.False(fs.HasAnyForce("cylinder_1"));
        Assert.Equal(ForceMode.Auto, fs.GetForce("cylinder_1", "cmd_extend"));
        // cylinder_2 intact.
        Assert.Equal(ForceMode.ForceLow, fs.GetForce("cylinder_2", "cmd_extend"));
    }

    [Fact]
    public void ActiveForces_liste_les_forcages_actifs()
    {
        var fs = new ForceSet();
        fs.SetForce("cylinder_1", "cmd_extend", ForceMode.ForceHigh);
        fs.SetForce("conveyor", "cmd_run", ForceMode.ForceLow);

        var forces = fs.ActiveForces;
        Assert.Equal(2, forces.Count);
        Assert.Contains(("cylinder_1", "cmd_extend", ForceMode.ForceHigh), forces);
        Assert.Contains(("conveyor", "cmd_run", ForceMode.ForceLow), forces);
    }
}
