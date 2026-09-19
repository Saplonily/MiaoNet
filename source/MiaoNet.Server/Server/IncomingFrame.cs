using MiaoNet.Shared;

namespace MiaoNet.Server;

/// <summary>
/// A frame that arrived from a client.
/// <para>
/// A built-in packet is decoded on the spot; a net-mod frame is not, because the server never
/// loads a mod assembly. Both cases end up here so that the read loop has a single thing to hand
/// over, and the service then either dispatches it or routes it — never both.
/// </para>
/// </summary>
internal readonly struct IncomingFrame
{
    /// <summary>The envelope of a built-in packet. A relayed frame gets a fresh one instead.</summary>
    public PacketEnvelope Envelope { get; }

    /// <summary>Set for a net-mod frame, which is forwarded without being understood.</summary>
    public RelayedFrame? Relay { get; }

    /// <summary>Set for a built-in packet.</summary>
    public PacketID ID { get; }

    /// <summary>Set for a built-in packet.</summary>
    public IContextualPacket? Packet { get; }

    public bool IsRelay => Relay is not null;

    private IncomingFrame(PacketEnvelope envelope, RelayedFrame? relay, in PacketID id, IContextualPacket? packet)
    {
        Envelope = envelope;
        Relay = relay;
        ID = id;
        Packet = packet;
    }

    public static IncomingFrame FromPacket(PacketEnvelope envelope, in PacketID id, IContextualPacket packet)
        => new(envelope, null, in id, packet);

    public static IncomingFrame FromRelay(in RelayedFrame relay)
        => new(default, relay, relay.ID, null);
}
