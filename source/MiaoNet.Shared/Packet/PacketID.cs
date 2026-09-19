namespace MiaoNet.Shared;

/// <summary>
/// Identifies a packet on the wire.
/// <para>
/// A built-in packet uses the id that <see cref="PacketRegistry"/> allocated for its type
/// at compile time. A net-mod packet uses a <em>local</em> id inside its own namespace,
/// together with the mod's index in the receiver's own net-mod list. Indices on the wire
/// are sender-local; the receiver translates them through the sender's handshake net-mod
/// list before it needs a <see cref="PacketID"/>.
/// </para>
/// </summary>
public readonly struct PacketID : IEquatable<PacketID>
{
    /// <summary>Built-in packet id (never 0, see <see cref="PacketRegistry"/>).</summary>
    public byte Value { get; }

    /// <summary>The owning net-mod's local index. Only meaningful when <see cref="IsExtended"/>.</summary>
    public byte NetModIndex { get; }

    /// <summary>Whether this id belongs to a net-mod instead of the built-in registry.</summary>
    public bool IsExtended { get; }

    private PacketID(byte value, byte netModIndex, bool isExtended)
    {
        Value = value;
        NetModIndex = netModIndex;
        IsExtended = isExtended;
    }

    public static PacketID BuiltIn(byte id)
    {
        // 0 is reserved, see PacketRegistry.
        if (id is 0)
            throw new ArgumentOutOfRangeException(nameof(id), SR.BuiltInPacketIDZero);

        return new(id, 0, false);
    }

    public static PacketID Extended(byte netModIndex, byte localID)
        => new(localID, netModIndex, true);

    /// <summary>
    /// The key of the single dispatch table that holds built-in and net-mod handlers
    /// alike: built-in ids occupy <c>0x0001..0x00FF</c>, and a net-mod packet occupies
    /// <c>((NetModIndex + 1) &lt;&lt; 8) | localId</c>, so the two spaces cannot collide.
    /// </summary>
    public ushort Key => IsExtended
        ? (ushort)(((NetModIndex + 1) << 8) | Value)
        : Value;

    public bool Equals(PacketID other)
        => Value == other.Value && NetModIndex == other.NetModIndex && IsExtended == other.IsExtended;

    public override bool Equals(object? obj) => obj is PacketID other && Equals(other);

    public override int GetHashCode() => Key;

    public override string ToString()
        => IsExtended
            ? $"net-mod[{NetModIndex}]:{Value}"
            : $"builtin:{Value}";
}
