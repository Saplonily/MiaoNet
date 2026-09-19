using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;

namespace MiaoNet.Shared;

public delegate IContextualPacket RefBinaryPacketReadHandler(ref RefBinaryReader reader, IPacketSerializationContext context);

/// <summary>
/// The built-in packet types, frozen at compile time from <see cref="PacketRegistryAttribute"/>.
/// <para>
/// Net-mod packets are deliberately absent: their ids live in <see cref="MiaoNetNetMods"/> and
/// are resolved through the sender's net-mod list. Keeping this table closed is what makes a
/// packet of an unregistered type impossible to write or read, instead of silently travelling
/// as some other type.
/// </para>
/// </summary>
public static class PacketRegistry
{
    public const byte MaxPacketCount = byte.MaxValue;

    private static readonly FrozenDictionary<byte, RefBinaryPacketReadHandler> idToReader;
    private static readonly FrozenDictionary<Type, byte> typeToID;

    static PacketRegistry()
    {
        var asm = typeof(PacketRegistry).Assembly;

        var infoAttr = asm.GetCustomAttribute<PacketRegistryAttribute>()!;

        if (infoAttr.Types.Length > MaxPacketCount)
            throw new InvalidOperationException(SR.TooManyPacketsRegistered);

        List<(byte id, Type type, RefBinaryPacketReadHandler reader)> list =
            infoAttr.Types.Select((type, id) =>
            {
                // Only the reflection is expensive, and it happens once per type.
                RefBinaryPacketReadHandler reader = PacketReaderFactory.Compile(type);
                return ((byte)(id + 1), type, reader); // 0 reserved
            }).ToList();

        idToReader = list.ToFrozenDictionary(t => t.id, t => t.reader);
        typeToID = list.ToFrozenDictionary(t => t.type, t => t.id);
    }

    public static RefBinaryPacketReadHandler GetPacketReader(byte id)
    {
        if (!idToReader.TryGetValue(id, out var handler))
            throw new KeyNotFoundException(string.Format(CultureInfo.InvariantCulture, SR.PacketNotFoundByID, id));
        return handler;
    }

    /// <summary>The registry id of a built-in packet type. Exact match, no type hierarchy.</summary>
    public static byte GetBuiltInID(Type packetType)
    {
        ArgumentNullException.ThrowIfNull(packetType);

        if (typeToID.TryGetValue(packetType, out byte id))
            return id;

        throw new KeyNotFoundException(string.Format(
            CultureInfo.InvariantCulture, SR.TypeIsNotRegisteredAsAPacket, packetType.FullName
        ));
    }

    public static byte GetPacketID(IContextualPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return GetBuiltInID(packet.GetType());
    }
}
