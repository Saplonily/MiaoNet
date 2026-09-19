namespace MiaoNet.Shared;

/// <summary>
/// Implemented by every net-mod packet type.
/// <para>
/// This is the only type-side requirement beyond <see cref="IContextualPacket{TSelf}"/>:
/// it declares the packet's scope, which the framework reads at registration time so
/// that a net-mod never has to pass a scope anywhere. A packet's scope never varies,
/// so it is declared once, on the type.
/// </para>
/// </summary>
/// <typeparam name="TSelf">The implementing packet type itself.</typeparam>
public interface INetModPacket<TSelf> where TSelf : INetModPacket<TSelf>
{
    /// <summary>
    /// Who receives this packet. <see cref="PacketScope.Player"/> and
    /// <see cref="PacketScope.Players"/> still vary per send in their <em>targets</em>,
    /// which the send call supplies.
    /// </summary>
    static abstract PacketScope Scope { get; }
}
