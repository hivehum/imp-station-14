using Robust.Shared.Prototypes; // EE
using Robust.Shared.Serialization; // EE
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

// IMP EDIT - this is a port of a change from Einstein Engines. the upstream file for this was at Content.Server/GameTicking/Prototypes/LobbyBackgroundPrototype.cs
namespace Content.Shared.GameTicking.Prototypes; // EE

/// <summary>
/// Prototype for a lobby background the game can choose.
/// </summary>
[Prototype("lobbyBackground")]
[NetSerializable, Serializable] // EE
public sealed partial class LobbyBackgroundPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; set; } = default!;

    /// <summary>
    /// The sprite to use as the background. This should ideally be 1920x1080.
    /// </summary>
    [DataField("background", required: true)]
    public ResPath Background = default!;

    /// <summary>
    /// EE edit below - Lobby Background Credits
    /// </summary>
    [DataField]
    public string? Name;

    [DataField]
    public string? Artist;
}
