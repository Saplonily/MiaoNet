using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

partial class MiaoNetContext
{
    /// <summary>
    /// The net-mods this process declares. Their order is what the wire's net-mod indices mean.
    /// </summary>
    internal static HandshakeData.NetMod[] BuildHandshakeNetMods()
        => MiaoNetNetMods.BuildHandshakeNetMods();

    /// <summary>
    /// Adds the declared net-mod packet handlers to the dispatch table, next to the built-in
    /// ones. The keys use this process's own indices, because that is what a received frame is
    /// translated into before it is dispatched.
    /// </summary>
    private static void RegisterNetModHandlers(PacketHandlerRegister register)
    {
        List<NetModHandlerBinding> bindings = [];
        MiaoNetNetMods.CollectHandlers(bindings);
        foreach (NetModHandlerBinding binding in bindings)
            register.RegisterNetMod(binding.NetModIndex, binding.LocalID, binding.Invoker);
    }

    /// <summary>
    /// The declared net-mods changed, which invalidates both the packet ids and the handlers of
    /// any session in flight. Such a session cannot be patched up: its handlers may point at code
    /// that was just unloaded, and the peers still believe the old declaration. Tearing it down is
    /// the only honest answer.
    /// </summary>
    private void OnNetModsChanged()
    {
        packetDispatcher = BuildPacketDispatcher();

        if (HasConnection)
            DisconnectByException(new NetModsChangedException());
    }
}

/// <summary>Raised when the set of net-mods changed while a session was running.</summary>
public sealed class NetModsChangedException : Exception
{
    public NetModsChangedException()
        : base("The set of net-mods changed, so the session had to be closed.")
    {
    }
}
