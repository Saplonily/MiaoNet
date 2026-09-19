using System.Buffers.Binary;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

/// <summary>
/// Covers the net-mod packet path: the frame layout, the registration semantics, and the
/// fact that a net-mod packet is decoded through the very same machinery as a built-in one.
/// <para>
/// The net-mod registry is process-global state that owns packet types, so these tests
/// cannot share the process with each other.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class NetModPacketTests
{
    private const string ModName = "miao.test.netmod";

    [TestMethod]
    public void BuiltInFrameLayoutIsPinned()
    {
        // PacketPing carries no payload, so its frame is nothing but the header.
        ByteArrayBufferWriter output = new(64);
        PacketFraming.WriteFrame(output, default, new PacketPing(), new TestSerializationContext());

        byte id = PacketRegistry.GetBuiltInID(typeof(PacketPing));
        CollectionAssert.AreEqual(new byte[] { 0, 0, id, 0 }, output.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void ExtendedFrameWireLayoutIsPinned()
    {
        using Fixture fixture = new();
        PacketHandle<MapPing> ping = fixture.Ping;
        byte index = fixture.Index;

        ByteArrayBufferWriter output = new(64);
        PacketFraming.WriteFrame(
            output,
            PacketEnvelope.FromSender(42),
            PacketID.Extended(index, ping.LocalID),
            PacketRoute.Broadcast(PacketScope.Map),
            new MapPing { Value = 7 },
            fixture.Context
        );

        byte[] expected = BuildExtendedFrame(
            ping.LocalID, index, PacketScope.Map, [], sender: 42, batchable: true, [0x07]
        );

        CollectionAssert.AreEqual(expected, output.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void ExtendedFrameRoundTripsAndDecodes()
    {
        using Fixture fixture = new();

        ByteArrayBufferWriter output = new(64);
        PacketFraming.WriteFrame(
            output,
            PacketEnvelope.FromSender(42),
            PacketID.Extended(fixture.Index, fixture.Ping.LocalID),
            PacketRoute.Broadcast(PacketScope.Map),
            new MapPing { Value = 7 },
            fixture.Context
        );

        Assert.IsTrue(PacketFraming.TryReadFrame(output.WrittenSpan, out ParsedFrame parsed, out int consumed));
        Assert.AreEqual(output.WrittenCount, consumed);
        Assert.AreEqual(output.WrittenCount, parsed.TotalSize);

        Assert.IsTrue(parsed.IsExtended);
        Assert.AreEqual(fixture.Index, parsed.NetModIndex);
        Assert.AreEqual(fixture.Ping.LocalID, parsed.ID);
        Assert.AreEqual(PacketScope.Map, parsed.Route!.Value.Scope);
        Assert.AreEqual(42, parsed.Envelope.SenderPlayerID);

        IContextualPacket packet = PacketFraming.DecodeFrame(output.WrittenSpan, parsed, fixture.Context, out PacketID id);

        Assert.IsInstanceOfType<MapPing>(packet);
        Assert.AreEqual(7, ((MapPing)packet).Value);
        Assert.IsTrue(id.IsExtended);
        Assert.AreEqual(fixture.Index, id.NetModIndex);
        Assert.AreEqual(fixture.Ping.LocalID, id.Value);
    }

    [TestMethod]
    public void BatchableIsDeclaredOnTheWire()
    {
        using Fixture fixture = new();

        ByteArrayBufferWriter output = new(64);
        fixture.Ping.Send(new MapPing { Value = 1 });

        Assert.HasCount(1, fixture.Transport.Sent);
        PacketFraming.WriteFrame(
            output,
            default,
            fixture.Transport.Sent[0].ID,
            fixture.Transport.Sent[0].Route,
            fixture.Transport.Sent[0].Packet,
            fixture.Context
        );

        Assert.IsTrue(PacketFraming.TryReadFrame(output.WrittenSpan, out ParsedFrame parsed, out _));
        Assert.IsTrue(parsed.Batchable);
    }

    [TestMethod]
    public void UnicastAndMulticastRoutesReachTheTransport()
    {
        using Fixture fixture = new();
        fixture.Whisper.SendTo(99, new Whisper { Text = "hi" });
        fixture.Shout.SendTo([7, 8], new Shout());

        Assert.HasCount(2, fixture.Transport.Sent);

        (PacketID id, PacketRoute route, IContextualPacket packet) = fixture.Transport.Sent[0];
        Assert.AreEqual(PacketScope.Player, route.Scope);
        Assert.AreEqual(99, route.Targets[0]);
        Assert.AreEqual(fixture.Whisper.LocalID, id.Value);
        Assert.AreEqual("hi", ((Whisper)packet).Text);

        (id, route, _) = fixture.Transport.Sent[1];
        Assert.AreEqual(PacketScope.Players, route.Scope);
        CollectionAssert.AreEqual(new[] { 7, 8 }, route.Targets.ToArray());
        Assert.AreEqual(fixture.Shout.LocalID, id.Value);
    }

    [TestMethod]
    public void ScopeMismatchFailsLoud()
    {
        using Fixture fixture = new();

        // A broadcast-scope packet cannot be addressed to anyone.
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Ping.SendTo(1, new MapPing()));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Ping.SendTo([1], new MapPing()));

        // A unicast packet cannot be broadcast, and a multicast one needs several targets.
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Whisper.Send(new Whisper()));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Whisper.SendTo([1, 2], new Whisper()));

        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Shout.Send(new Shout()));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Shout.SendTo(1, new Shout()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => fixture.Shout.SendTo([], new Shout()));

        Assert.IsEmpty(fixture.Transport.Sent);
    }

    [TestMethod]
    public void RegistrationIsDeclaredNotDiscovered()
    {
        using Fixture fixture = new();

        Assert.IsTrue(MiaoNetNetMods.TryGetIndex(ModName, out byte index));
        Assert.AreEqual(fixture.Index, index);

        // Both directions of the id mapping are the registry's business, not the mod's.
        Assert.IsTrue(MiaoNetNetMods.TryGetPacket(
            typeof(MapPing), out byte netModIndex, out byte localID, out PacketScope scope
        ));
        Assert.AreEqual(index, netModIndex);
        Assert.AreEqual(fixture.Ping.LocalID, localID);
        Assert.AreEqual(PacketScope.Map, scope);

        Assert.IsTrue(MiaoNetNetMods.TryGetPacketInfo(index, fixture.Ping.LocalID, out var registered, out _));
        Assert.AreEqual(PacketScope.Map, registered);

        // Cross-mod ownership is refused.
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            MiaoNetNetMods.Register(new NetMod(ModName, new Version(1, 0))));
    }

    [TestMethod]
    public void PacketTypesCannotBeClaimedTwice()
    {
        using Fixture fixture = new();

        Assert.ThrowsExactly<InvalidOperationException>(() => MiaoNetNetMods.Register(new NetMod(
            "miao.test.netmod.other",
            new Version(1, 0),
            static b => b.Packet<MapPing>(static (_, _) => { })
        )));

        Assert.ThrowsExactly<InvalidOperationException>(() => MiaoNetNetMods.Register(new NetMod(
            "miao.test.netmod.thrice",
            new Version(1, 0),
            static b =>
            {
                b.Packet<MapPing>(static (_, _) => { });
                b.Packet<MapPing>(static (_, _) => { });
            }
        )));
    }

    [TestMethod]
    public void HandlersAreCollectedForDispatch()
    {
        using Fixture fixture = new();

        List<NetModHandlerBinding> bindings = [];
        MiaoNetNetMods.CollectHandlers(bindings);

        // Every declared type is collected, in local id order: a declaration without a
        // handler cannot exist.
        Assert.HasCount(3, bindings);
        Assert.AreEqual(fixture.Ping.LocalID, bindings[0].LocalID);
        Assert.AreEqual(fixture.Whisper.LocalID, bindings[1].LocalID);
        Assert.AreEqual(fixture.Shout.LocalID, bindings[2].LocalID);
        foreach (NetModHandlerBinding binding in bindings)
            Assert.AreEqual(fixture.Index, binding.NetModIndex);

        NetModHandlerBinding whisper = bindings.Single(b => b.LocalID == fixture.Whisper.LocalID);
        whisper.Invoker.Invoke(PacketEnvelope.FromSender(42), new Whisper());
        Assert.AreEqual(1, fixture.HandlerCalls);
        Assert.AreEqual(0, fixture.PingCalls);

        // The sender travels with the packet, so a handler never has to look anywhere else.
        Assert.AreEqual(42, fixture.LastSenderPlayerID);
    }

    [TestMethod]
    public void EveryDeclaredTypeMustBeHandled()
    {
        // A mod's declaration is the same code on every peer, so declaring a type without
        // a handler would only put bytes on the wire that nobody anywhere acts on. The
        // registry refuses it, and the failed build leaves nothing behind.
        Assert.ThrowsExactly<ArgumentNullException>(() => MiaoNetNetMods.Register(new NetMod(
            "miao.test.netmod.unhandled",
            new Version(1, 0),
            static b => b.Packet<MapPing>(null!)
        )));

        Assert.IsFalse(MiaoNetNetMods.TryGetIndex("miao.test.netmod.unhandled", out _));
    }

    [TestMethod]
    public void UnregisterInvalidatesHandlesAndCompactsIndices()
    {
        Fixture fixture = new();
        PacketHandle<MapPing> ping = fixture.Ping;
        PacketHandle<Shout> shout = fixture.Shout;

        try
        {
            fixture.Dispose();

            // The handle belongs to an unregistered mod, so it must not silently work.
            Assert.ThrowsExactly<ObjectDisposedException>(() => ping.Send(new MapPing()));

            // Disposing twice is a no-op, as IDisposable requires.
            fixture.Dispose();

            Assert.IsFalse(MiaoNetNetMods.TryGetIndex(ModName, out _));

            // Re-registering allocates the local ids again, and the old handles stay dead.
            using Fixture again = new();
            Assert.IsTrue(MiaoNetNetMods.TryGetIndex(ModName, out _));
            Assert.AreEqual(shout.LocalID, again.Shout.LocalID);
            Assert.IsEmpty(again.Transport.Sent);

            // A handle follows its mod's index instead of remembering a stale one.
            again.Shout.SendTo([1], new Shout());
            PacketID sent = again.Transport.Sent[0].ID;
            Assert.AreEqual(again.Index, sent.NetModIndex);
            Assert.IsTrue(MiaoNetNetMods.TryGetIndex(ModName, out byte currentIndex));
            Assert.AreEqual(currentIndex, sent.NetModIndex);
        }
        finally
        {
            if (!fixture.Disposed)
                fixture.Dispose();
        }
    }

    [TestMethod]
    public void DecodeFailsLoudForUnknownPacketID()
    {
        using Fixture fixture = new();

        // Hand-built on purpose: the writer refuses to produce an unregistered id.
        byte[] frame = BuildExtendedFrame(
            localID: 200, fixture.Index, PacketScope.Map, [], sender: 42, batchable: false, payload: []
        );

        Assert.IsTrue(PacketFraming.TryReadFrame(frame, out ParsedFrame parsed, out _));
        Assert.ThrowsExactly<InvalidDataException>(() => PacketFraming.DecodeFrame(frame, parsed, fixture.Context, out _));
    }

    [TestMethod]
    public void DecodeReadsTheIndexAsItsOwn()
    {
        using Fixture fixture = new();

        // The index on the wire belongs to the receiver: no list of the sender's is consulted,
        // which is why a frame can be decoded on the receive thread before the main thread has
        // applied the join notification that arrived next to it.
        byte[] frame = BuildExtendedFrame(
            fixture.Ping.LocalID, fixture.Index, PacketScope.Map, [], sender: 42, batchable: false, [0x07]
        );

        Assert.IsTrue(PacketFraming.TryReadFrame(frame, out ParsedFrame parsed, out _));

        MapPing decoded = (MapPing)PacketFraming.DecodeFrame(frame, parsed, fixture.Context, out PacketID id);
        Assert.AreEqual(7, decoded.Value);
        Assert.AreEqual(fixture.Index, id.NetModIndex);
    }

    [TestMethod]
    public void DecodeFailsLoudForAnIndexThisProcessDoesNotHave()
    {
        using Fixture fixture = new();

        byte[] frame = BuildExtendedFrame(
            fixture.Ping.LocalID, (byte)(fixture.Index + 1), PacketScope.Map, [], sender: 42, batchable: false, [0x07]
        );

        Assert.IsTrue(PacketFraming.TryReadFrame(frame, out ParsedFrame parsed, out _));
        Assert.ThrowsExactly<InvalidDataException>(() => PacketFraming.DecodeFrame(frame, parsed, fixture.Context, out _));
    }

    [TestMethod]
    public void DecodingIgnoresTheFramesThatFollow()
    {
        using Fixture fixture = new();

        ByteArrayBufferWriter first = new(64);
        ByteArrayBufferWriter second = new(64);
        PacketEnvelope envelope = PacketEnvelope.FromSender(1);
        PacketFraming.WriteFrame(
            first, envelope, PacketID.Extended(fixture.Index, fixture.Ping.LocalID),
            PacketRoute.Broadcast(PacketScope.Map), new MapPing { Value = 1 }, fixture.Context
        );
        PacketFraming.WriteFrame(
            second, envelope, PacketID.Extended(fixture.Index, fixture.Ping.LocalID),
            PacketRoute.Broadcast(PacketScope.Map), new MapPing { Value = 2 }, fixture.Context
        );

        // One buffer holding two frames: the payload must end where the frame says it does.
        byte[] both = [.. first.WrittenSpan.ToArray(), .. second.WrittenSpan.ToArray()];

        Assert.IsTrue(PacketFraming.TryReadFrame(both, out ParsedFrame parsed, out _));
        Assert.IsInstanceOfType<MapPing>(PacketFraming.DecodeFrame(both, parsed, fixture.Context, out _));
        Assert.AreEqual(1, ((MapPing)PacketFraming.DecodeFrame(both, parsed, fixture.Context, out _)).Value);

        Assert.IsTrue(PacketFraming.TryReadFrame(both.AsSpan(first.WrittenCount), out ParsedFrame later, out _));
        Assert.AreEqual(2, ((MapPing)PacketFraming.DecodeFrame(both.AsSpan(first.WrittenCount), later, fixture.Context, out _)).Value);
    }

    [TestMethod]
    public void TryReadFrameWaitsForAWholeFrame()
    {
        using Fixture fixture = new();

        ByteArrayBufferWriter output = new(64);
        PacketFraming.WriteFrame(
            output,
            default,
            PacketID.Extended(fixture.Index, fixture.Ping.LocalID),
            PacketRoute.Broadcast(PacketScope.Map),
            new MapPing { Value = 7 },
            fixture.Context
        );

        byte[] frame = output.WrittenSpan.ToArray();

        Assert.IsFalse(PacketFraming.TryReadFrame(frame.AsSpan(0, Connection.PacketHeaderSize - 1), out _, out _));
        Assert.IsFalse(PacketFraming.TryReadFrame(frame.AsSpan(0, frame.Length - 1), out _, out _));
        Assert.IsTrue(PacketFraming.TryReadFrame(frame, out _, out int consumed));
        Assert.AreEqual(frame.Length, consumed);
    }

    [TestMethod]
    public void PacketIDKeyHasNoCollisionsBetweenTheTwoSpaces()
    {
        Assert.AreEqual((ushort)1, PacketID.BuiltIn(1).Key);
        Assert.AreEqual((ushort)0x00FF, PacketID.BuiltIn(255).Key);

        Assert.AreEqual((ushort)0x0101, PacketID.Extended(0, 1).Key);
        Assert.AreEqual((ushort)0xFFFF, PacketID.Extended(254, 255).Key);

        Assert.AreNotEqual(PacketID.BuiltIn(1).Key, PacketID.Extended(0, 1).Key);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PacketID.BuiltIn(0));
    }

    [TestMethod]
    [DataRow(PacketScope.Global)]
    [DataRow(PacketScope.Channel)]
    [DataRow(PacketScope.Map)]
    public void BroadcastRoutesCarryNoTargets(PacketScope scope)
    {
        ByteArrayBufferWriter output = new(16);
        RefBinaryWriter writer = new(output);
        PacketRoute.Broadcast(scope).Write(ref writer);
        writer.Flush();

        Assert.AreEqual(1, output.WrittenCount);
        Assert.AreEqual((byte)scope, output.WrittenSpan[0]);
    }

    [TestMethod]
    public void RoutesRoundTripThroughTheWire()
    {
        foreach (PacketRoute route in new[]
        {
            PacketRoute.Broadcast(PacketScope.Global),
            PacketRoute.Broadcast(PacketScope.Channel),
            PacketRoute.Broadcast(PacketScope.Map),
            PacketRoute.Unicast(1234),
            PacketRoute.Multicast([1, 2, 300])
        })
        {
            ByteArrayBufferWriter output = new(32);
            RefBinaryWriter writer = new(output);
            route.Write(ref writer);
            writer.Flush();

            RefBinaryReader reader = new(output.WrittenSpan);
            PacketRoute read = PacketRoute.Read(ref reader);

            Assert.AreEqual(route.Scope, read.Scope);
            CollectionAssert.AreEqual(route.Targets.ToArray(), read.Targets.ToArray());
            Assert.AreEqual(0, reader.BytesLeft);
        }
    }

    /// <summary>
    /// Writes the bytes the frame format promises, independently of the writer under test.
    /// </summary>
    private static byte[] BuildExtendedFrame(
        byte localID,
        byte netModIndex,
        PacketScope scope,
        int[] targets,
        int sender,
        bool batchable,
        byte[] payload
    )
    {
        List<byte> body = [netModIndex, (byte)scope];

        if (scope == PacketScope.Player)
        {
            Add7BitEncodedInt(body, targets[0]);
        }
        else if (scope == PacketScope.Players)
        {
            Add7BitEncodedInt(body, targets.Length);
            foreach (int target in targets)
                Add7BitEncodedInt(body, target);
        }

        // PacketEnvelope.FromSender always sets HasSender, which this helper assumes too.
        Add7BitEncodedInt(body, sender);
        body.AddRange(payload);

        PacketFrameFlags flags = PacketFrameFlags.Extended | PacketFrameFlags.HasSender;
        if (batchable)
            flags |= PacketFrameFlags.Batchable;

        byte[] frame = new byte[Connection.PacketHeaderSize + body.Count];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)body.Count);
        frame[sizeof(ushort)] = localID;
        frame[sizeof(ushort) + sizeof(byte)] = (byte)flags;
        body.CopyTo(frame, Connection.PacketHeaderSize);
        return frame;
    }

    private static void Add7BitEncodedInt(List<byte> body, int value)
    {
        uint remaining = (uint)value;
        while (remaining > 0x7Fu)
        {
            body.Add((byte)(remaining | ~0x7Fu));
            remaining >>= 7;
        }

        body.Add((byte)remaining);
    }

    private sealed class Fixture : IDisposable
    {
        public CaptureTransport Transport { get; } = new();

        public TestSerializationContext Context { get; } = new();

        public PacketHandle<MapPing> Ping { get; }

        public PacketHandle<Whisper> Whisper { get; }

        public PacketHandle<Shout> Shout { get; }

        public byte Index { get; }

        public int HandlerCalls { get; private set; }

        public int PingCalls { get; private set; }

        /// <summary>The sender the last <see cref="Whisper"/> handler saw.</summary>
        public int LastSenderPlayerID { get; private set; } = -1;

        public bool Disposed { get; private set; }

        private readonly IDisposable token;

        public Fixture()
        {
            PacketHandle<MapPing> ping = null!;
            PacketHandle<Whisper> whisper = null!;
            PacketHandle<Shout> shout = null!;

            token = MiaoNetNetMods.Register(new NetMod(ModName, new Version(1, 0), b =>
            {
                ping = b.Packet<MapPing>((_, _) => PingCalls++);
                whisper = b.Packet<Whisper>((envelope, _) =>
                {
                    HandlerCalls++;
                    LastSenderPlayerID = envelope.SenderPlayerID;
                });
                shout = b.Packet<Shout>(static (_, _) => { });
            }));

            Ping = ping;
            Whisper = whisper;
            Shout = shout;

            Assert.IsTrue(MiaoNetNetMods.TryGetIndex(ModName, out byte index));
            Index = index;

            MiaoNetNetMods.SetTransport(Transport);
        }

        public void Dispose()
        {
            if (Disposed)
                return;

            Disposed = true;
            MiaoNetNetMods.SetTransport(null);
            token.Dispose();
        }
    }

    private sealed class CaptureTransport : INetModTransport
    {
        public List<(PacketID ID, PacketRoute Route, IContextualPacket Packet)> Sent { get; } = [];

        public void Send(in PacketID id, in PacketRoute route, IContextualPacket packet)
            => Sent.Add((id, route, packet));
    }

    private sealed class TestSerializationContext : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }

    private sealed class MapPing : IContextualPacket<MapPing>, INetModPacket<MapPing>
    {
        public static PacketScope Scope => PacketScope.Map;

        public bool CanBatch => true;

        public int Value { get; set; }

        public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
            => writer.Write7BitEncodedInt(Value);

        public static MapPing Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
            => new() { Value = reader.Read7BitEncodedInt() };
    }

    private sealed class Whisper : IContextualPacket<Whisper>, INetModPacket<Whisper>
    {
        public static PacketScope Scope => PacketScope.Player;

        public string Text { get; set; } = string.Empty;

        public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
            => writer.Write(Text);

        public static Whisper Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
            => new() { Text = reader.ReadString() };
    }

    private sealed class Shout : IContextualPacket<Shout>, INetModPacket<Shout>
    {
        public static PacketScope Scope => PacketScope.Players;

        public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context) { }

        public static Shout Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context) => new();
    }
}
