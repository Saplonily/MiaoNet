namespace MiaoNet.Shared;

public interface IPacketHandlerRegister
{
    public void Register<TPacket>(PacketHandler<TPacket> handler) where TPacket : IContextualPacket;
}

#if MIAO_CLIENT || MIAO_MOCKCLIENT || INSPECTOR
public sealed class PacketHandlerRegister : IPacketHandlerRegister
{
    /// <summary>
    /// Keyed by <see cref="PacketID.Key"/>, so built-in and net-mod packets share one table
    /// and one lookup instead of two parallel dispatch mechanisms.
    /// </summary>
    public Dictionary<ushort, PacketHandler<IContextualPacket>> Dictionary { get; set; } = new();

    public void Register<TPacket>(PacketHandler<TPacket> handler) where TPacket : IContextualPacket
    {
        ushort key = PacketRegistry.GetBuiltInID(typeof(TPacket));

        void HandlePacket(PacketEnvelope envelope, IContextualPacket p) => handler(envelope, (TPacket)p);

        Dictionary.Add(key, HandlePacket);
    }

    /// <summary>
    /// Registers one net-mod packet type. The key uses this process's index for the mod, which
    /// is what a received frame is translated into before it is dispatched.
    /// </summary>
    internal void RegisterNetMod(byte netModIndex, byte localID, IPacketInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(invoker);

        ushort key = PacketID.Extended(netModIndex, localID).Key;
        Dictionary.Add(key, (envelope, p) => invoker.Invoke(envelope, p));
    }
}
#elif MIAO_SERVER
public sealed class PacketHandlerRegister : IPacketHandlerRegister
{
    // The server registers built-in packets only: a net-mod packet is never decoded here, so
    // it never reaches this table.
    public Dictionary<ushort, PacketHandler<IContextualPacket>> Dictionary { get; set; } = new();

    public void Register<TPacket>(PacketHandler<TPacket> handler) where TPacket : IContextualPacket
    {
        ushort key = PacketRegistry.GetBuiltInID(typeof(TPacket));

        Task HandlePacketAsync(Server.MiaoClientConnection c, PacketEnvelope envelope, IContextualPacket p)
            => handler(c, envelope, (TPacket)p);

        Dictionary.Add(key, HandlePacketAsync);
    }
}
#endif
