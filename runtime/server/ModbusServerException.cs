// =============================================================================
// ModbusServerException — echec de demarrage/exploitation du serveur Modbus TCP
// =============================================================================
//
// Pourquoi un TYPE DEDIE (et pas une Exception generique) ?
// Le point dur historique (dette D-013) : si le port 502 est deja pris (un reliquat
// de SimHost, un autre serveur Modbus...), l'ancien Start() laissait croire que le
// serveur ecoutait alors qu'il n'en etait rien — echec MUET. On veut desormais un
// echec BRUYANT et, surtout, RECONNAISSABLE : l'appelant Godot (S3.2) doit pouvoir
// catcher SPECIFIQUEMENT ce cas pour afficher un bandeau « serveur KO » — distinct
// d'une PivotException (pivot mal forme) ou d'une exception systeme quelconque.
//
// Un type propre = un `catch (ModbusServerException)` cible cote HUD, sans avoir a
// deviner le message. Le message reste en francais et cite bind:port pour le diag.

namespace CarrouselServer;

/// <summary>
/// Erreur du serveur Modbus TCP (echec d'ecoute : port deja utilise, interface
/// indisponible...). Type distinct pour que l'appelant le catche specifiquement.
/// </summary>
public sealed class ModbusServerException : System.Exception
{
    /// <summary>
    /// Construit l'erreur avec un message francais explicite. <paramref name="inner"/>
    /// conserve l'exception d'origine (ex. la <c>SocketException</c> du bind) pour le
    /// diagnostic sans la faire remonter telle quelle a l'appelant.
    /// </summary>
    public ModbusServerException(string message, System.Exception? inner = null)
        : base(message, inner)
    {
    }
}
