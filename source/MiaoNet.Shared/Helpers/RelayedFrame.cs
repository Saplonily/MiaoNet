#if MIAO_SERVER
namespace MiaoNet.Shared;

/// <summary>
/// A net-mod frame the server parsed but deliberately does not understand.
/// <para>
/// It is not an <see cref="IContextualPacket"/> and must not be one: it has no registry id,
/// it is never deserialized, and it never reaches a packet handler. The server keeps only
/// what it needs to choose the receiving connections, and re-emits the payload bytes as
/// they arrived.
/// </para>
/// </summary>
public readonly struct RelayedFrame
{
    /// <summary>
    /// The sender-local net-mod index together with the mod's own packet id, exactly as
    /// they arrived. The sender's index is what a receiver translates, so it is forwarded
    /// verbatim rather than translated here.
    /// </summary>
    public PacketID ID { get; }

    public PacketRoute Route { get; }

    /// <summary>
    /// The sender's own batching hint, preserved across the relay: it describes the packet,
    /// not the hop it is currently travelling.
    /// </summary>
    public bool Batchable { get; }

    /// <summary>The frame's bytes after the route and envelope, untouched.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    public RelayedFrame(PacketID id, PacketRoute route, bool batchable, ReadOnlyMemory<byte> payload)
    {
        ID = id;
        Route = route;
        Batchable = batchable;
        Payload = payload;
    }
}
#endif
