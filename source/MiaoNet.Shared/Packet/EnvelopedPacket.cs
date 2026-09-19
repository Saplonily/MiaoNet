namespace MiaoNet.Shared;

/// <summary>
/// A decoded packet, together with the envelope it arrived in and the id it arrived under.
/// <para>
/// The id is carried instead of being re-derived from the packet's type: a net-mod packet is
/// dispatched under the id the sender used, which is a local id inside its own net-mod, and a
/// built-in packet under its registry id. Both end up in the same dispatch table.
/// </para>
/// </summary>
public readonly struct EnvelopedPacket
{
    public PacketEnvelope Envelope { get; }

    public PacketID ID { get; }

    public IContextualPacket Packet { get; }

    public EnvelopedPacket(PacketEnvelope envelope, in PacketID id, IContextualPacket packet)
    {
        Envelope = envelope;
        ID = id;
        Packet = packet;
    }

    public void Deconstruct(out PacketEnvelope envelope, out PacketID id, out IContextualPacket packet)
        => (envelope, id, packet) = (Envelope, ID, Packet);
}
