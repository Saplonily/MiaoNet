namespace MiaoNet.Shared;

/// <summary>
/// How a net-mod packet picks its receivers.
/// <para>
/// The scope is a property of the packet type (<see cref="INetModPacket{TSelf}.Scope"/>),
/// it never varies from one send to the next. The server routes by this value alone
/// and never looks at the payload.
/// </para>
/// </summary>
public enum PacketScope : byte
{
    /// <summary>Every player that declared the sender's net-mod, in any channel.</summary>
    Global = 0,

    /// <summary>Every such player in the sender's channel.</summary>
    Channel = 1,

    /// <summary>Every such player on the sender's map.</summary>
    Map = 2,

    /// <summary>
    /// Exactly one explicitly addressed player, which the receiver may rely on being
    /// the only target. Addressing the sender itself is allowed.
    /// </summary>
    Player = 3,

    /// <summary>
    /// One or more explicitly addressed players. A receiver must not assume it is the
    /// only target. Addressing the sender itself is allowed.
    /// </summary>
    Players = 4
}

public static class PacketScopeExtensions
{
    /// <summary>
    /// Whether the receivers are derived from the sender's own position instead of
    /// being listed explicitly.
    /// </summary>
    public static bool IsBroadcast(this PacketScope scope)
        => scope is PacketScope.Global or PacketScope.Channel or PacketScope.Map;

    /// <summary>Whether <paramref name="scope"/> carries an explicit target list.</summary>
    public static bool IsExplicit(this PacketScope scope)
        => scope is PacketScope.Player or PacketScope.Players;
}
