namespace MiaoNet.Shared;

/// <summary>
/// The flags byte of a packet frame (the fourth byte of the header).
/// <para>
/// The low three bits are the envelope semantics and were the whole byte before net-mod
/// packets existed; the higher bits belong to the frame itself, so a relay can route a
/// packet it cannot decode.
/// </para>
/// </summary>
[Flags]
public enum PacketFrameFlags : byte
{
    None = 0,

    /// <summary>The envelope carries a sender player id.</summary>
    HasSender = 1 << 0,

    /// <summary>The envelope carries a request id.</summary>
    HasRequestID = 1 << 1,

    /// <summary>The envelope marks this packet as the response to a request.</summary>
    IsResponse = 1 << 2,

    /// <summary>
    /// The header id is a net-mod local id and is followed by the sender-local net-mod
    /// index and a route block. The payload is opaque to the server.
    /// </summary>
    Extended = 1 << 3,

    /// <summary>
    /// The sender declares that this frame may share a TCP write with the frames that
    /// follow it; the server otherwise flushes immediately.
    /// </summary>
    Batchable = 1 << 4,

    /// <summary>Every bit that belongs to <see cref="PacketEnvelope"/>.</summary>
    EnvelopeMask = HasSender | HasRequestID | IsResponse
}
