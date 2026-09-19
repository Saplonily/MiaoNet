using System.Buffers;
using System.Buffers.Binary;
using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

/// <summary>
/// Covers the server's half of the net-mod path: a frame it cannot decode still has to become
/// something it can route, and re-emitting it must not disturb a single byte of the payload.
/// </summary>
[TestClass]
public sealed class NetModRelayTests
{
    private readonly TestContext context = new();

    [TestMethod]
    public void ExtendedFrameBecomesARelayedFrameWithoutBeingDecoded()
    {
        byte[] payload = [0x11, 0x22, 0x33];
        byte[] frame = BuildClientExtendedFrame(
            localID: 4, netModIndex: 2, PacketScope.Players, [7, 8], batchable: true, payload
        );

        ReadOnlySequence<byte> sequence = new(frame);
        Assert.IsTrue(MiaoClientConnection.TryParseFrame(ref sequence, out IncomingFrame parsed, context));

        Assert.IsTrue(parsed.IsRelay);
        Assert.IsNull(parsed.Packet);
        Assert.AreEqual(0L, sequence.Length);

        RelayedFrame relay = parsed.Relay!.Value;
        Assert.AreEqual(2, relay.ID.NetModIndex);
        Assert.AreEqual(4, relay.ID.Value);
        Assert.IsTrue(relay.ID.IsExtended);
        Assert.AreEqual(PacketScope.Players, relay.Route.Scope);
        CollectionAssert.AreEqual(new[] { 7, 8 }, relay.Route.Targets.ToArray());
        Assert.IsTrue(relay.Batchable);
        CollectionAssert.AreEqual(payload, relay.Payload.ToArray());
    }

    [TestMethod]
    public void BroadcastExtendedFrameCarriesNoTargets()
    {
        byte[] frame = BuildClientExtendedFrame(1, 0, PacketScope.Map, [], batchable: false, [0xAB]);
        ReadOnlySequence<byte> sequence = new(frame);

        Assert.IsTrue(MiaoClientConnection.TryParseFrame(ref sequence, out IncomingFrame parsed, context));

        RelayedFrame relay = parsed.Relay!.Value;
        Assert.AreEqual(PacketScope.Map, relay.Route.Scope);
        Assert.IsEmpty(relay.Route.Targets);
        Assert.IsFalse(relay.Batchable);
    }

    [TestMethod]
    public void ClientMayNotClaimASenderInAnExtendedFrame()
    {
        // Same bytes, but with the envelope's HasSender bit set, which only the server sets.
        byte[] frame = BuildClientExtendedFrame(
            localID: 1, netModIndex: 0, PacketScope.Map, [], batchable: false, [0x01], hasSender: true
        );
        ReadOnlySequence<byte> sequence = new(frame);

        Assert.ThrowsExactly<InvalidDataException>(
            () => MiaoClientConnection.TryParseFrame(ref sequence, out _, context)
        );
    }

    [TestMethod]
    public void RelayingReEmitsTheSameFrameWithAFreshEnvelope()
    {
        byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];
        byte[] inbound = BuildClientExtendedFrame(
            localID: 9, netModIndex: 3, PacketScope.Player, [1234], batchable: true, payload
        );

        ReadOnlySequence<byte> sequence = new(inbound);
        Assert.IsTrue(MiaoClientConnection.TryParseFrame(ref sequence, out IncomingFrame parsed, context));

        // The server knows the sender, so it writes one; the client's own frame had none.
        OutgoingFrame outgoing = OutgoingFrame.FromRelay(
            PacketEnvelope.FromSender(4321), parsed.Relay!.Value, relayNetModIndex: 0
        );

        ByteArrayBufferWriter output = new(64);
        PacketFraming.WriteFrame(output, outgoing, context);

        Assert.IsTrue(PacketFraming.TryReadFrame(output.WrittenSpan, out ParsedFrame relayed, out int consumed));
        Assert.AreEqual(output.WrittenCount, consumed);

        Assert.IsTrue(relayed.IsExtended);
        Assert.AreEqual(0, relayed.NetModIndex);
        Assert.AreEqual(9, relayed.ID);
        Assert.AreEqual(PacketScope.Player, relayed.Route!.Value.Scope);
        Assert.AreEqual(1234, relayed.Route.Value.Targets[0]);
        Assert.IsTrue(relayed.Batchable);
        Assert.AreEqual(4321, relayed.Envelope.SenderPlayerID);

        // The payload is the one thing the server must leave alone.
        CollectionAssert.AreEqual(payload, output.WrittenSpan[(Connection.PacketHeaderSize + relayed.PayloadOffset)..].ToArray());
    }

    [TestMethod]
    public void RelayingRewritesTheIndexForTheReceiver()
    {
        byte[] payload = [0x5A, 0x5B];
        byte[] frame = BuildClientExtendedFrame(2, 7, PacketScope.Global, [], batchable: false, payload);
        ReadOnlySequence<byte> sequence = new(frame);

        Assert.IsTrue(MiaoClientConnection.TryParseFrame(ref sequence, out IncomingFrame parsed, context));

        // As it arrives, the index is the sender's own: that is what lets the service find the
        // declaration the frame belongs to.
        Assert.AreEqual(7, parsed.Relay!.Value.ID.NetModIndex);

        // What a target reads is its own index for the same mod, and the payload is untouched.
        OutgoingFrame outgoing = OutgoingFrame.FromRelay(default, parsed.Relay!.Value, relayNetModIndex: 3);
        ByteArrayBufferWriter output = new(64);
        PacketFraming.WriteFrame(output, outgoing, context);

        Assert.IsTrue(PacketFraming.TryReadFrame(output.WrittenSpan, out ParsedFrame relayed, out _));
        Assert.AreEqual(3, relayed.NetModIndex);
        CollectionAssert.AreEqual(
            payload,
            output.WrittenSpan[(Connection.PacketHeaderSize + relayed.PayloadOffset)..].ToArray()
        );
    }

    private static byte[] BuildClientExtendedFrame(
        byte localID,
        byte netModIndex,
        PacketScope scope,
        int[] targets,
        bool batchable,
        byte[] payload,
        bool hasSender = false
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

        if (hasSender)
            Add7BitEncodedInt(body, 1);

        body.AddRange(payload);

        PacketFrameFlags flags = PacketFrameFlags.Extended;
        if (batchable)
            flags |= PacketFrameFlags.Batchable;
        if (hasSender)
            flags |= PacketFrameFlags.HasSender;

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

    private sealed class TestContext : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }
}
