namespace MiaoNet.Shared;

/// <summary>
/// One frame waiting to be written.
/// <para>
/// It is either a packet that still has to be serialized, or — on the server — a net-mod frame
/// that arrived and is re-emitted byte for byte, which is why the two cases share one queue
/// even though only the first one is a packet.
/// </para>
/// </summary>
public readonly struct OutgoingFrame
{
    public PacketEnvelope Envelope { get; }

    /// <summary>Built-in registry id, or a net-mod's own packet id.</summary>
    public PacketID ID { get; }

    /// <summary>Present exactly when <see cref="ID"/> is extended.</summary>
    public PacketRoute? Route { get; }

    /// <summary>Null only for a relayed frame, which carries its bytes instead.</summary>
    public IContextualPacket? Packet { get; }

    /// <summary>
    /// The sender's batching hint: whether this frame may share a write with the next ones.
    /// </summary>
    public bool Batchable { get; }

#if MIAO_SERVER
    /// <summary>Only present when <see cref="Packet"/> is null.</summary>
    public RelayedFrame? Relay { get; }

    /// <summary>
    /// Only present for a relayed frame: the index the owning mod has in the receiving
    /// connection's own list. The frame on the wire carries this instead of the sender's
    /// index, so that a receiver never has to know how the sender ordered its mods.
    /// </summary>
    public byte RelayNetModIndex { get; }
#endif

    private OutgoingFrame(
        PacketEnvelope envelope,
        in PacketID id,
        PacketRoute? route,
        IContextualPacket? packet,
        bool batchable
#if MIAO_SERVER
        , RelayedFrame? relay = null, byte relayNetModIndex = 0
#endif
    )
    {
        Envelope = envelope;
        ID = id;
        Route = route;
        Packet = packet;
        Batchable = batchable;
#if MIAO_SERVER
        Relay = relay;
        RelayNetModIndex = relayNetModIndex;
#endif
    }

    /// <summary>A built-in packet, whose id and scope are fixed by its type.</summary>
    public static OutgoingFrame FromPacket(PacketEnvelope envelope, IContextualPacket packet)
        => new(envelope, PacketID.BuiltIn(PacketRegistry.GetPacketID(packet)), null, packet, packet.CanBatch);

    /// <summary>A net-mod packet, whose route comes from the send call.</summary>
    public static OutgoingFrame FromNetMod(
        PacketEnvelope envelope,
        in PacketID id,
        in PacketRoute route,
        IContextualPacket packet
    ) => new(envelope, id, route, packet, packet.CanBatch);

#if MIAO_SERVER
    /// <summary>A frame the server routed without decoding.</summary>
    /// <param name="relayNetModIndex">
    /// The index of the sending mod in the target's own list, which is what the target reads.
    /// </param>
    public static OutgoingFrame FromRelay(PacketEnvelope envelope, in RelayedFrame relay, byte relayNetModIndex)
        => new(envelope, relay.ID, relay.Route, null, relay.Batchable, relay, relayNetModIndex);
#endif
}
