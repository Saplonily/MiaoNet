using System.Globalization;

namespace MiaoNet.Shared;

/// <summary>
/// Receives the packets of one net-mod packet type.
/// <para>
/// The envelope is the frame's own: a net-mod frame always carries a sender, because the server
/// stamps one while relaying and <see cref="PacketFraming.Decode"/> refuses a frame without it.
/// </para>
/// </summary>
public delegate void NetModPacketHandler<TPacket>(PacketEnvelope envelope, TPacket packet)
    where TPacket : IContextualPacket;

/// <summary>
/// Where a net-mod packet goes once one of its handles has been used.
/// The connected client installs one; without it, sending fails loud.
/// </summary>
public interface INetModTransport
{
    void Send(in PacketID id, in PacketRoute route, IContextualPacket packet);
}

/// <summary>
/// Declares one net-mod to MiaoNet. A mod builds this during its own <c>Load</c> and
/// registers it statically, so it never has to care when MiaoNet itself is loaded.
/// </summary>
public sealed class NetMod
{
    public string Name { get; }

    public Version Version { get; }

    /// <summary>
    /// Runs once, when the mod is registered. It must declare every packet type the mod
    /// ever sends, on every peer, in the same order: that order is what a local packet id
    /// means. Every declared type carries its handler, because this code is the same on
    /// every peer.
    /// </summary>
    public Action<INetModBuilder> Build { get; }

    public NetMod(string name, Version version, Action<INetModBuilder> build)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(build);

        Name = name;
        Version = version;
        Build = build;
    }

    /// <summary>
    /// A net-mod that exchanges no packet at all: it only declares a name and a version,
    /// which every other player can see in the handshake. Useful to let peers tell whether
    /// a mod is installed, without ever sending it anything.
    /// </summary>
    public NetMod(string name, Version version)
        : this(name, version, static _ => { })
    {
    }
}

/// <summary>Handed to <see cref="NetMod.Build"/> to declare a net-mod's packet types.</summary>
public interface INetModBuilder
{
    /// <summary>
    /// Allocates the next local packet id for <typeparamref name="TPacket"/> and returns
    /// the handle used to send it. The scope comes from the type itself.
    /// <para>
    /// The handler is mandatory because a net-mod's declaration is the same code on every
    /// peer: only <em>routing</em> differs from one peer to the next, and a type nobody
    /// handles would be pure weight on the wire. A peer that never receives this type still
    /// declares the very same handler — it is simply never invoked there.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The type is declared twice, or another net-mod already owns it.
    /// </exception>
    PacketHandle<TPacket> Packet<TPacket>(NetModPacketHandler<TPacket> handler)
        where TPacket : IContextualPacket<TPacket>, INetModPacket<TPacket>;
}

/// <summary>
/// The only way to send one net-mod packet type.
/// <para>
/// Because a handle can only exist for a registered type, "declare before send" is a
/// property of the type system rather than something checked at runtime.
/// </para>
/// </summary>
public sealed class PacketHandle<TPacket> where TPacket : IContextualPacket<TPacket>
{
    private readonly NetModPacketEntry packet;

    internal PacketHandle(NetModPacketEntry packet) => this.packet = packet;

    /// <summary>Offset of this type inside its net-mod, in declaration order.</summary>
    public byte LocalID => packet.LocalID;

    public PacketScope Scope => packet.Scope;

    public string NetModName => packet.Owner.Name;

    /// <summary>Broadcasts to every receiver the packet's own scope selects.</summary>
    public void Send(TPacket packet)
    {
        PacketScope scope = Scope;
        if (!scope.IsBroadcast())
            throw Mismatch("Send", "SendTo");

        SendCore(PacketRoute.Broadcast(scope), packet);
    }

    /// <summary>Sends to exactly one player, which may be the local player.</summary>
    public void SendTo(int targetPlayerID, TPacket packet)
    {
        if (Scope != PacketScope.Player)
            throw Mismatch("SendTo(int, ...)", "Send or SendTo(ids, ...)");

        SendCore(PacketRoute.Unicast(targetPlayerID), packet);
    }

    /// <summary>Sends to several players at once; a receiver must not assume it is alone.</summary>
    public void SendTo(IReadOnlyList<int> targetPlayerIDs, TPacket packet)
    {
        if (Scope != PacketScope.Players)
            throw Mismatch("SendTo(ids, ...)", "Send or SendTo(int, ...)");

        SendCore(PacketRoute.Multicast(targetPlayerIDs), packet);
    }

    private InvalidOperationException Mismatch(string used, string expected)
        => new(string.Format(
            CultureInfo.InvariantCulture, SR.NetModSendNotAllowed,
            NetModName, typeof(TPacket).Name, Scope, used, expected
        ));

    private void SendCore(in PacketRoute route, TPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        NetModPacketEntry entry = this.packet;
        if (entry.Owner.Removed)
        {
            throw new ObjectDisposedException(
                string.Format(CultureInfo.InvariantCulture, SR.HandleDisposed, entry.Owner.Name)
            );
        }

        route.CheckAgainst(entry.Scope);
        MiaoNetNetMods.Send(PacketID.Extended(entry.Owner.Index, entry.LocalID), route, packet);
    }
}

/// <summary>
/// The set of net-mods declared by this process, and the packet ids they own.
/// <para>
/// The registry is a value: every change builds a new immutable snapshot and swaps it in,
/// so a connection can keep using the snapshot it captured without being affected by a
/// later mod unload. Local packet ids are assigned in declaration order, which is the
/// only ordering contract, and it only has to hold inside one mod.
/// </para>
/// </summary>
public static class MiaoNetNetMods
{
    /// <summary>
    /// Indices go up to 254 because a packet id packs the index into the high byte.
    /// </summary>
    public const int MaxNetMods = 255;

    /// <summary>Local packet ids are one byte wide, so a mod owns at most 256 types.</summary>
    public const int MaxPacketsPerNetMod = 256;

    private static readonly object sync = new();
    private static volatile Snapshot snapshot = Snapshot.Empty;
    private static volatile INetModTransport? transport;

    /// <summary>
    /// Raised after the set of net-mods changed. A mod unloading while a session is
    /// running invalidates that session's packet ids and handlers, so the listener is
    /// expected to tear the session down rather than keep using the old snapshot.
    /// </summary>
    public static event Action? Changed;

    /// <summary>Whether a transport is installed, i.e. whether sending can work at all.</summary>
    public static bool IsConnected => transport is not null;

    /// <summary>
    /// Registers a net-mod and runs its <see cref="NetMod.Build"/> immediately, so packet
    /// ids exist and handles work as soon as this returns.
    /// </summary>
    /// <returns>Disposing it unregisters the net-mod, which <c>Unload</c> must do.</returns>
    public static IDisposable Register(NetMod netMod)
    {
        ArgumentNullException.ThrowIfNull(netMod);

        NetModEntry entry;
        lock (sync)
        {
            Snapshot current = snapshot;

            if (current.ByName.ContainsKey(netMod.Name))
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture, SR.NetModAlreadyRegistered, netMod.Name
                ));
            }

            if (current.Mods.Count >= MaxNetMods)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture, SR.NetModIndexSpaceExhausted, MaxNetMods
                ));
            }

            entry = new NetModEntry(netMod.Name, netMod.Version);

            // The declaration runs here and nowhere else, so by the time the mod becomes
            // visible to anybody its packet types, ids and handlers are all decided.
            netMod.Build(new Builder(entry, current));

            PublishWith(current.Mods.Append(entry));
        }

        Changed?.Invoke();
        return new Token(entry);
    }

    /// <summary>The net-mods declared right now, in local index order.</summary>
    public static IReadOnlyList<NetModInfo> Current
    {
        get
        {
            Snapshot current = snapshot;
            NetModInfo[] result = new NetModInfo[current.Mods.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = new(current.Mods[i].Name, current.Mods[i].Version);
            return result;
        }
    }

    /// <summary>Builds the handshake net-mod list, whose order defines the indices.</summary>
    public static HandshakeData.NetMod[] BuildHandshakeNetMods()
    {
        Snapshot current = snapshot;
        HandshakeData.NetMod[] result = new HandshakeData.NetMod[current.Mods.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = new(current.Mods[i].Version, current.Mods[i].Name);
        return result;
    }

    /// <summary>Translates a net-mod name into the local index used on the wire.</summary>
    public static bool TryGetIndex(string name, out byte index)
    {
        if (snapshot.ByName.TryGetValue(name, out NetModEntry? entry))
        {
            index = entry.Index;
            return true;
        }

        index = 0;
        return false;
    }

    /// <summary>Resolves the id a packet object is sent under.</summary>
    public static bool TryGetPacket(
        Type packetType,
        out byte netModIndex,
        out byte localID,
        out PacketScope scope
    )
    {
        ArgumentNullException.ThrowIfNull(packetType);

        if (snapshot.ByPacketType.TryGetValue(packetType, out NetModEntry? entry)
            && entry.ByType.TryGetValue(packetType, out NetModPacketEntry? packet))
        {
            netModIndex = entry.Index;
            localID = packet.LocalID;
            scope = packet.Scope;
            return true;
        }

        netModIndex = 0;
        localID = 0;
        scope = default;
        return false;
    }

    /// <summary>Resolves the reader of a packet that arrived from a peer.</summary>
    public static bool TryGetPacketInfo(
        byte netModIndex,
        byte localID,
        out PacketScope scope,
        out RefBinaryPacketReadHandler reader
    )
    {
        Snapshot current = snapshot;
        if (netModIndex < current.Mods.Count)
        {
            NetModEntry mod = current.Mods[netModIndex];
            if (localID < mod.Packets.Count)
            {
                NetModPacketEntry packet = mod.Packets[localID];
                scope = packet.Scope;
                reader = packet.Reader;
                return true;
            }
        }

        scope = default;
        reader = null!;
        return false;
    }

    /// <summary>Installs the transport that a connected client sends through.</summary>
    public static void SetTransport(INetModTransport? value) => transport = value;

    internal static void Send(in PacketID id, in PacketRoute route, IContextualPacket packet)
    {
        INetModTransport? current = transport;
        if (current is null)
            throw new InvalidOperationException(SR.NetModNotConnected);

        current.Send(in id, in route, packet);
    }

    /// <summary>
    /// The handlers to install in a dispatch table, in local index order.
    /// </summary>
    internal static void CollectHandlers(List<NetModHandlerBinding> results)
    {
        Snapshot current = snapshot;
        for (int i = 0; i < current.Mods.Count; i++)
        {
            NetModEntry mod = current.Mods[i];
            foreach (NetModPacketEntry packet in mod.Packets)
                results.Add(new(mod.Index, packet.LocalID, packet.Invoker));
        }
    }

    private static void Unregister(NetModEntry entry)
    {
        lock (sync)
        {
            Snapshot current = snapshot;
            if (entry.Removed)
                return;

            // Old handles must stop working: their ids may be handed out again.
            entry.Removed = true;
            PublishWith(current.Mods.Where(m => !ReferenceEquals(m, entry)));
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Publishes a new snapshot. Indices are positions, so they are rewritten here; a
    /// handle holds its entry rather than a copy of the index and therefore stays correct.
    /// </summary>
    private static void PublishWith(IEnumerable<NetModEntry> mods)
    {
        List<NetModEntry> list = [.. mods];
        Dictionary<string, NetModEntry> byName = new(StringComparer.Ordinal);
        Dictionary<Type, NetModEntry> byPacketType = new();

        for (int i = 0; i < list.Count; i++)
        {
            NetModEntry mod = list[i];
            mod.Index = (byte)i;
            byName.Add(mod.Name, mod);

            foreach (NetModPacketEntry packet in mod.Packets)
                byPacketType.Add(packet.PacketType, mod);
        }

        snapshot = new Snapshot(list, byName, byPacketType);
    }

    private sealed class Builder : INetModBuilder
    {
        private readonly NetModEntry entry;
        private readonly Snapshot current;

        public Builder(NetModEntry entry, Snapshot current)
        {
            this.entry = entry;
            this.current = current;
        }

        public PacketHandle<TPacket> Packet<TPacket>(NetModPacketHandler<TPacket> handler)
            where TPacket : IContextualPacket<TPacket>, INetModPacket<TPacket>
        {
            ArgumentNullException.ThrowIfNull(handler);

            Type type = typeof(TPacket);

            if (current.ByPacketType.ContainsKey(type) || entry.ByType.ContainsKey(type))
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture, SR.NetModPacketTypeAlreadyRegistered, entry.Name, type.FullName
                ));
            }

            if (entry.Packets.Count >= MaxPacketsPerNetMod)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture, SR.NetModPacketIDSpaceExhausted,
                    entry.Name, MaxPacketsPerNetMod
                ));
            }

            NetModPacketEntry packet = new(
                entry,
                (byte)entry.Packets.Count,
                TPacket.Scope,
                type,
                PacketReaderFactory.Compile(type),
                new PacketInvoker<TPacket>(handler)
            );
            entry.Packets.Add(packet);
            entry.ByType.Add(type, packet);
            return new PacketHandle<TPacket>(packet);
        }
    }

    private sealed class Token : IDisposable
    {
        private NetModEntry? entry;

        public Token(NetModEntry entry) => this.entry = entry;

        public void Dispose()
        {
            NetModEntry? current = Interlocked.Exchange(ref entry, null);
            if (current is not null)
                Unregister(current);
        }
    }

    private sealed class Snapshot
    {
        public static readonly Snapshot Empty = new([], new(StringComparer.Ordinal), new());

        public readonly List<NetModEntry> Mods;
        public readonly Dictionary<string, NetModEntry> ByName;
        public readonly Dictionary<Type, NetModEntry> ByPacketType;

        public Snapshot(
            List<NetModEntry> mods,
            Dictionary<string, NetModEntry> byName,
            Dictionary<Type, NetModEntry> byPacketType
        )
        {
            Mods = mods;
            ByName = byName;
            ByPacketType = byPacketType;
        }
    }
}

/// <summary>Name and version of a registered net-mod, in local index order.</summary>
public readonly record struct NetModInfo(string Name, Version Version);

/// <summary>One net-mod packet that declared a handler, in local index order.</summary>
internal readonly record struct NetModHandlerBinding(byte NetModIndex, byte LocalID, IPacketInvoker Invoker);

/// <summary>The type-erased form of <see cref="NetModPacketHandler{TPacket}"/>.</summary>
internal interface IPacketInvoker
{
    void Invoke(PacketEnvelope envelope, IContextualPacket packet);
}

internal sealed class PacketInvoker<TPacket> : IPacketInvoker where TPacket : IContextualPacket
{
    private readonly NetModPacketHandler<TPacket> handler;

    public PacketInvoker(NetModPacketHandler<TPacket> handler) => this.handler = handler;

    public void Invoke(PacketEnvelope envelope, IContextualPacket packet)
        => handler(envelope, (TPacket)packet);
}

internal sealed class NetModEntry
{
    public string Name { get; }

    public Version Version { get; }

    /// <summary>Position in the current snapshot; rewritten by every publish.</summary>
    public byte Index { get; internal set; }

    /// <summary>Set when the mod unregistered, which invalidates its handles.</summary>
    public bool Removed { get; internal set; }

    public List<NetModPacketEntry> Packets { get; } = [];

    public Dictionary<Type, NetModPacketEntry> ByType { get; } = [];

    public NetModEntry(string name, Version version)
    {
        Name = name;
        Version = version;
    }
}

internal sealed class NetModPacketEntry
{
    public NetModEntry Owner { get; }

    public byte LocalID { get; }

    public PacketScope Scope { get; }

    public Type PacketType { get; }

    public RefBinaryPacketReadHandler Reader { get; }

    /// <summary>
    /// The handler of this type, installed in every dispatch table of this process.
    /// </summary>
    public IPacketInvoker Invoker { get; }

    public NetModPacketEntry(
        NetModEntry owner,
        byte localID,
        PacketScope scope,
        Type packetType,
        RefBinaryPacketReadHandler reader,
        IPacketInvoker invoker
    )
    {
        Owner = owner;
        LocalID = localID;
        Scope = scope;
        PacketType = packetType;
        Reader = reader;
        Invoker = invoker;
    }
}
