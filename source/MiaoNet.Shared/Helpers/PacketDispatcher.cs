using System.Collections.Frozen;

namespace MiaoNet.Shared;

#if MIAO_CLIENT || MIAO_MOCKCLIENT || INSPECTOR
public delegate void PacketHandler<TPacket>(PacketEnvelope envelope, TPacket packet)
    where TPacket : IContextualPacket;
#elif MIAO_SERVER
public delegate Task PacketHandler<TPacket>(Server.MiaoClientConnection connection, PacketEnvelope envelope, TPacket packet)
    where TPacket : IContextualPacket;
#endif

#if MIAO_CLIENT || MIAO_MOCKCLIENT || INSPECTOR
public sealed class PacketDispatcher
{
    private readonly FrozenDictionary<ushort, PacketHandler<IContextualPacket>> dictionary;

    public PacketDispatcher(PacketHandlerRegister register)
    {
        dictionary = register.Dictionary.ToFrozenDictionary();
    }

    /// <summary>
    /// Looks the packet up by its own id. There is no fallback to a handler registered for a
    /// base type: an id that nobody registered is not dispatched to somebody else.
    /// </summary>
    public bool DispatchPacket(PacketEnvelope envelope, in PacketID id, IContextualPacket packet)
    {
        if (!dictionary.TryGetValue(id.Key, out PacketHandler<IContextualPacket>? handler))
            return false;

        handler(envelope, packet);
        return true;
    }
}
#elif MIAO_SERVER
public sealed class PacketDispatcher
{
    private readonly FrozenDictionary<ushort, PacketHandler<IContextualPacket>> dictionary;

    public PacketDispatcher(PacketHandlerRegister register)
    {
        dictionary = register.Dictionary.ToFrozenDictionary();
    }

    public async ValueTask<bool> DispatchPacketAsync(
        Server.MiaoClientConnection connection,
        PacketEnvelope envelope,
        PacketID id,
        IContextualPacket packet
    )
    {
        if (!dictionary.TryGetValue(id.Key, out PacketHandler<IContextualPacket>? handler))
            return false;

        await handler(connection, envelope, packet);
        return true;
    }
}
#endif
