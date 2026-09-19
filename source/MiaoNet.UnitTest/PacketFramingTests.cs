using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class PacketFramingTests
{
    private readonly TestPacketSerializationContext context = new();

    [TestMethod]
    public void WriteFrameRejectsPayloadLargerThanProtocolLimit()
    {
        string largeName = new('a', 33_000);
        string largePrefix = new('b', 33_000);
        var packet = new PacketClientInitial(
            channelID: 1,
            playerID: 2,
            new PlayerInfo(3, largeName, largePrefix, string.Empty, Color.White),
            Array.Empty<PacketClientInitial.Channel>(),
            Array.Empty<PacketClientInitial.Player>(),
            new PlayerPresenceMessage(string.Empty, string.Empty),
            string.Empty
        );
        ByteArrayBufferWriter output = new();

        PacketTooLargeException exception = Assert.ThrowsExactly<PacketTooLargeException>(
            () => PacketFraming.WriteFrame(output, default, packet, context)
        );

        Assert.AreEqual(packet.GetType(), exception.PacketType);
        Assert.IsGreaterThan(Connection.MaxPayloadSize, exception.PayloadSize);
        Assert.AreEqual(Connection.MaxPayloadSize, exception.MaxPayloadSize);
    }

    [TestMethod]
    public void WriteFrameAcceptsPayloadAtProtocolLimit()
    {
        // ChatChannel(byte) + string length(ushort)
        const int packetFieldsSize = sizeof(byte) + sizeof(ushort);
        var packet = new PacketSendChatMessage(
            default,
            new string('a', Connection.MaxPayloadSize - packetFieldsSize)
        );
        ByteArrayBufferWriter output = new();

        PacketFraming.WriteFrame(output, default, packet, context);

        Assert.AreEqual(
            Connection.MaxPayloadSize,
            BinaryPrimitives.ReadUInt16LittleEndian(output.WrittenSpan)
        );
        Assert.AreEqual(
            Connection.PacketHeaderSize + Connection.MaxPayloadSize,
            output.WrittenCount
        );
    }

    [TestMethod]
    public async Task ReadFrameThrowsOnTruncatedHeader()
    {
        byte[] frame = BuildFrame(new PacketPing());
        using var stream = new MemoryStream(frame, 0, Connection.PacketHeaderSize - 1);

        PacketTruncatedException exception = await Assert.ThrowsExactlyAsync<PacketTruncatedException>(
            () => PacketFraming.ReadFrameAsync(stream, context, CancellationToken.None).AsTask()
        );

        Assert.IsFalse(exception.IsPayload);
        Assert.AreEqual(Connection.PacketHeaderSize - 1, exception.BytesRead);
        Assert.AreEqual(Connection.PacketHeaderSize, exception.ExpectedBytes);
    }

    [TestMethod]
    public async Task ReadFrameReturnsNullAtCleanEof()
    {
        using var stream = new MemoryStream();

        EnvelopedPacket? packet = await PacketFraming.ReadFrameAsync(
            stream,
            context,
            CancellationToken.None
        );

        Assert.IsNull(packet);
    }

    [TestMethod]
    public async Task ReadFrameThrowsOnTruncatedPayload()
    {
        byte[] frame = BuildFrame(new PacketSendChatMessage(default, "hello"));
        using var stream = new MemoryStream(frame, 0, frame.Length - 1);

        PacketTruncatedException exception = await Assert.ThrowsExactlyAsync<PacketTruncatedException>(
            () => PacketFraming.ReadFrameAsync(stream, context, CancellationToken.None).AsTask()
        );

        Assert.IsTrue(exception.IsPayload);
        Assert.AreEqual(frame.Length - Connection.PacketHeaderSize - 1, exception.BytesRead);
        Assert.AreEqual(exception.ExpectedBytes, frame.Length - Connection.PacketHeaderSize);
    }

    [TestMethod]
    public async Task ReadFrameHandlesZeroLengthPayload()
    {
        byte[] frame = BuildFrame(new PacketPing());
        Assert.AreEqual(0, BinaryPrimitives.ReadUInt16LittleEndian(frame));
        Assert.AreEqual(
            PacketRegistry.GetPacketID(new PacketPing()),
            frame[sizeof(ushort)]
        );
        Assert.AreEqual((byte)0, frame[Connection.PacketHeaderSize - 1]);
        using var stream = new MemoryStream(frame);

        EnvelopedPacket? packet = await PacketFraming.ReadFrameAsync(
            stream,
            context,
            CancellationToken.None
        );

        Assert.IsInstanceOfType<PacketPing>(packet!.Value.Packet);
        Assert.AreEqual(PacketID.BuiltIn(PacketRegistry.GetPacketID(new PacketPing())), packet.Value.ID);
    }

    [TestMethod]
    public async Task ReadFrameParsesMultipleFramesSequentially()
    {
        byte[] frameA = BuildFrame(new PacketPing());
        byte[] frameB = BuildFrame(new PacketPong());
        byte[] frameC = BuildFrame(new PacketSendChatMessage(default, "hello"));
        byte[] all = new byte[frameA.Length + frameB.Length + frameC.Length];
        frameA.CopyTo(all, 0);
        frameB.CopyTo(all, frameA.Length);
        frameC.CopyTo(all, frameA.Length + frameB.Length);
        using var stream = new MemoryStream(all);

        EnvelopedPacket? packet1 = await PacketFraming.ReadFrameAsync(stream, context, CancellationToken.None);
        EnvelopedPacket? packet2 = await PacketFraming.ReadFrameAsync(stream, context, CancellationToken.None);
        EnvelopedPacket? packet3 = await PacketFraming.ReadFrameAsync(stream, context, CancellationToken.None);
        EnvelopedPacket? packet4 = await PacketFraming.ReadFrameAsync(stream, context, CancellationToken.None);

        Assert.IsInstanceOfType<PacketPing>(packet1!.Value.Packet);
        Assert.IsInstanceOfType<PacketPong>(packet2!.Value.Packet);
        Assert.IsInstanceOfType<PacketSendChatMessage>(packet3!.Value.Packet);
        Assert.IsNull(packet4);
    }

    [TestMethod]
    public async Task ReadFrameCarriesEnvelopeAndPayload()
    {
        byte[] frame = BuildFrame(new PacketSendChatMessage(default, "hello"), PacketEnvelope.FromSender(4321));

        // the last header byte is the flags byte
        Assert.AreEqual((byte)PacketEnvelopeFlags.HasSender, frame[Connection.PacketHeaderSize - 1]);

        using var stream = new MemoryStream(frame);
        EnvelopedPacket? packet = await PacketFraming.ReadFrameAsync(stream, context, CancellationToken.None);

        Assert.IsNotNull(packet);
        Assert.IsTrue(packet.Value.Envelope.HasSender);
        Assert.IsFalse(packet.Value.Envelope.HasRequestID);
        Assert.AreEqual(4321, packet.Value.Envelope.SenderPlayerID);
        Assert.IsInstanceOfType<PacketSendChatMessage>(packet.Value.Packet);
    }

    [TestMethod]
    public async Task ReadFrameWrapsDeserializationFailureInInvalidPacketDataException()
    {
        byte[] payload = { 0 };
        byte[] frame = new byte[Connection.PacketHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)payload.Length);
        frame[sizeof(ushort)] = PacketRegistry.GetPacketID(new PacketSendChatMessage(default, string.Empty));
        payload.CopyTo(frame.AsSpan(Connection.PacketHeaderSize));
        using var stream = new MemoryStream(frame);

        InvalidPacketDataException exception = await Assert.ThrowsExactlyAsync<InvalidPacketDataException>(
            () => PacketFraming.ReadFrameAsync(stream, context, CancellationToken.None).AsTask()
        );

        Assert.HasCount(1, exception.Payload);
        Assert.AreEqual((byte)0, exception.Payload[0]);
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public async Task CompletedPipeStillProcessesItsFinalBufferedPacket()
    {
        byte[] frame = BuildFrame(new PacketPing());
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(frame);
        await pipe.Writer.CompleteAsync();
        var receivedPackets = new List<IContextualPacket>();

        long leftover = await MiaoClientConnection.ProcessPacketsAsync(
            pipe.Reader,
            context,
            (frame, _) =>
            {
                receivedPackets.Add(frame.Packet!);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None
        );

        Assert.HasCount(1, receivedPackets);
        Assert.IsInstanceOfType<PacketPing>(receivedPackets[0]);
        Assert.AreEqual(0L, leftover);
    }

    [TestMethod]
    public async Task CompletedPipeProcessesMaximumPayloadWithoutLargeStackAllocation()
    {
        // ChatChannel(byte) + string length(ushort)
        const int packetFieldsSize = sizeof(byte) + sizeof(ushort);
        byte[] frame = BuildFrame(new PacketSendChatMessage(
            default,
            new string('a', Connection.MaxPayloadSize - packetFieldsSize)
        ));
        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: long.MaxValue,
            resumeWriterThreshold: long.MaxValue - 1
        ));
        await pipe.Writer.WriteAsync(frame);
        await pipe.Writer.CompleteAsync();
        int received = 0;

        long leftover = await MiaoClientConnection.ProcessPacketsAsync(
            pipe.Reader,
            context,
            (receivedPacket, bytesConsumed) =>
            {
                Assert.IsInstanceOfType<PacketSendChatMessage>(receivedPacket.Packet!);
                Assert.AreEqual(frame.Length, bytesConsumed);
                received++;
                return ValueTask.CompletedTask;
            },
            CancellationToken.None
        );

        Assert.AreEqual(1, received);
        Assert.AreEqual(0L, leftover);
    }

    [TestMethod]
    public async Task CompletedPipeReturnsLeftoverBytesForPartialFrame()
    {
        byte[] frame = BuildFrame(new PacketPing());
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(frame.AsMemory(0, frame.Length - 2));
        await pipe.Writer.CompleteAsync();
        var receivedPackets = new List<IContextualPacket>();

        long leftover = await MiaoClientConnection.ProcessPacketsAsync(
            pipe.Reader,
            context,
            (frame, _) =>
            {
                receivedPackets.Add(frame.Packet!);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None
        );

        Assert.HasCount(0, receivedPackets);
        Assert.AreEqual(frame.Length - 2, leftover);
    }

    [TestMethod]
    public void TryParseFrameParsesSingleFrame()
    {
        byte[] frame = BuildFrame(new PacketPing());
        ReadOnlySequence<byte> sequence = new(frame);

        bool parsed = MiaoClientConnection.TryParseFrame(ref sequence, out var parsedFrame, context);

        Assert.IsTrue(parsed);
        Assert.IsFalse(parsedFrame.IsRelay);
        Assert.IsInstanceOfType<PacketPing>(parsedFrame.Packet!);
        Assert.IsFalse(parsedFrame.Envelope.HasSender);
        Assert.AreEqual(0L, sequence.Length);
    }

    [TestMethod]
    public void TryParseFrameParsesEnvelope()
    {
        byte[] frame = BuildFrame(new PacketPing(), PacketEnvelope.ReplyTo(16384));
        ReadOnlySequence<byte> sequence = new(frame);

        bool parsed = MiaoClientConnection.TryParseFrame(ref sequence, out var parsedFrame, context);

        Assert.IsTrue(parsed);
        Assert.IsTrue(parsedFrame.Envelope.IsResponse);
        Assert.AreEqual(16384, parsedFrame.Envelope.RequestID);
    }

    [TestMethod]
    public void TryParseFrameReturnsFalseForIncompleteHeader()
    {
        byte[] frame = BuildFrame(new PacketPing());
        ReadOnlySequence<byte> sequence = new(frame.AsMemory(0, Connection.PacketHeaderSize - 1));

        bool parsed = MiaoClientConnection.TryParseFrame(ref sequence, out var parsedFrame, context);

        Assert.IsFalse(parsed);
        Assert.IsNull(parsedFrame.Packet);
    }

    [TestMethod]
    public void TryParseFrameReturnsFalseForIncompletePayload()
    {
        byte[] frame = BuildFrame(new PacketPing());
        ReadOnlySequence<byte> sequence = new(frame.AsMemory(0, frame.Length - 1));

        bool parsed = MiaoClientConnection.TryParseFrame(ref sequence, out var parsedFrame, context);

        Assert.IsFalse(parsed);
        Assert.IsNull(parsedFrame.Packet);
    }

    [TestMethod]
    public void TryParseFrameParsesMultipleFrames()
    {
        byte[] frameA = BuildFrame(new PacketPing());
        byte[] frameB = BuildFrame(new PacketPong());
        byte[] all = new byte[frameA.Length + frameB.Length];
        frameA.CopyTo(all, 0);
        frameB.CopyTo(all, frameA.Length);
        ReadOnlySequence<byte> sequence = new(all);

        bool parsed1 = MiaoClientConnection.TryParseFrame(ref sequence, out var frame1, context);
        Assert.IsTrue(parsed1);
        Assert.IsInstanceOfType<PacketPing>(frame1.Packet!);

        bool parsed2 = MiaoClientConnection.TryParseFrame(ref sequence, out var frame2, context);
        Assert.IsTrue(parsed2);
        Assert.IsInstanceOfType<PacketPong>(frame2.Packet!);

        Assert.AreEqual(0L, sequence.Length);
    }

    [TestMethod]
    public void TryParseFrameHandlesLargePayloadWithoutStackAllocation()
    {
        byte[] frame = BuildFrame(new PacketSendChatMessage(default, new string('a', 5000)));
        ReadOnlySequence<byte> sequence = new(frame);

        bool parsed = MiaoClientConnection.TryParseFrame(ref sequence, out var parsedFrame, context);

        Assert.IsTrue(parsed);
        Assert.IsInstanceOfType<PacketSendChatMessage>(parsedFrame.Packet!);
        Assert.AreEqual(0L, sequence.Length);
    }

    private byte[] BuildFrame(IContextualPacket packet, PacketEnvelope envelope = default)
    {
        ByteArrayBufferWriter output = new();
        PacketFraming.WriteFrame(output, envelope, packet, context);
        return output.WrittenSpan.ToArray();
    }

    private sealed class TestPacketSerializationContext : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }
}
