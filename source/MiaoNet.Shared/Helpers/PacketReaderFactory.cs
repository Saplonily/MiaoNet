using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace MiaoNet.Shared;

/// <summary>
/// Turns a packet type's static <c>Deserialize</c> into a delegate, once per type.
/// <para>
/// This reflection is the only expensive part of making a packet type usable, and it does
/// not depend on any connection, so it is cached separately from the tables built out of
/// it. Registering a net-mod packet therefore costs the same as a built-in one, and
/// rebuilding a dispatch table costs nothing but dictionary fills.
/// </para>
/// </summary>
public static class PacketReaderFactory
{
    private static readonly ConcurrentDictionary<Type, RefBinaryPacketReadHandler> cache = new();

    public static RefBinaryPacketReadHandler Compile(Type type)
        => cache.GetOrAdd(type, static t => CompileCore(t));

    private static RefBinaryPacketReadHandler CompileCore(Type type)
    {
        Type interfaceType = typeof(IContextualPacket<>).MakeGenericType(type);
        if (!type.IsAssignableTo(interfaceType))
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.TypeMustAtLeaseImplContextualPacket
            ));
        }

        InterfaceMapping map = type.GetInterfaceMap(
            typeof(IContextualRefBinarySerializable<,>)
                .MakeGenericType(type, typeof(IPacketSerializationContext))
        );
        int readerIndex = Array.FindIndex(
            map.InterfaceMethods,
            m => m.Name is nameof(IContextualPacket<>.Deserialize)
        );
        SafeGuard.Assert(readerIndex is 0 or 1);

        return map.TargetMethods[readerIndex].CreateDelegate<RefBinaryPacketReadHandler>();
    }
}
