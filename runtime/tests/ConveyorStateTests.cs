// =============================================================================
// ConveyorStateTests — recopie retardee du contact auxiliaire KM1_AUX (sans Godot)
// =============================================================================
//
// Delai calque sur le pivot (0.05 s = 50 ms), pose en dur ici : on verrouille la LOGIQUE de
// temporisation. On avance par `dt` fixes et on observe IsRunning.

using CarrouselCore;
using Xunit;

namespace CarrouselCore.Tests;

public class ConveyorStateTests
{
    private static ConveyorState NewConveyor() => new(feedbackDelayS: 0.05);

    [Fact]
    public void Etat_initial_arrete()
    {
        Assert.False(NewConveyor().IsRunning);
    }

    [Fact]
    public void Recopie_apres_le_delai()
    {
        var k = NewConveyor();
        // Un seul tick de 0.02 s < 0.05 s : le contact n'a pas encore ferme.
        k.Advance(0.02, cmdRun: true);
        Assert.False(k.IsRunning);

        // Cumul 0.02 + 0.04 = 0.06 s >= 0.05 s : le contact ferme.
        k.Advance(0.04, cmdRun: true);
        Assert.True(k.IsRunning);
    }

    [Fact]
    public void Retombe_apres_le_delai_a_l_arret()
    {
        var k = NewConveyor();
        k.Advance(0.1, cmdRun: true);    // largement au-dela du delai -> tourne
        Assert.True(k.IsRunning);

        k.Advance(0.02, cmdRun: false);  // < delai : encore colle
        Assert.True(k.IsRunning);
        k.Advance(0.04, cmdRun: false);  // cumul >= delai : retombe
        Assert.False(k.IsRunning);
    }

    [Fact]
    public void A_coup_bref_de_commande_ne_bascule_pas()
    {
        // La commande passe a 1 brievement (< delai) puis revient a 0 : le contact ne doit
        // jamais fermer (le compteur de temporisation se remet a zero des que cmd rejoint la sortie).
        var k = NewConveyor();
        k.Advance(0.02, cmdRun: true);   // amorce la temporisation
        k.Advance(0.02, cmdRun: false);  // cmd rejoint la sortie (false) -> compteur remis a zero
        k.Advance(0.02, cmdRun: true);   // repart de zero : 0.02 < 0.05
        Assert.False(k.IsRunning);
    }

    [Fact]
    public void Delai_negatif_echoue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConveyorState(-0.01));
    }
}
