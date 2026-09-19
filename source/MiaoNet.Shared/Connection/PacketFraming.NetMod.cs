using System.Buffers.Binary;
using System.Globalization;

namespace MiaoNet.Shared;

/// <summary>
/// A frame's own fields, separated from its payload.
/// <para>
/// The payload is deliberately not part of this value. It stays a slice of the buffer the
/// frame arrived in, so a client decodes it where it lies, and the server copies it exactly
/// once — when it turns the frame into a <see cref="RelayedFrame"/> that outlives the read.
/// </para>
/// </summary>
public readonly struct ParsedFrame
{
    public PacketEnvelope Envelope { get; }

    public PacketFrameFlags Flags { get; }

    /// <summary>Whether the id belongs to a net-mod instead of the packet registry.</summary>
    public bool IsExtended { get; }

    /// <summary>Registry id, or a net-mod's own packet id when <see cref="IsExtended"/>.</summary>
    public byte ID { get; }

    /// <summary>
    /// The net-mod index this frame carries. A client writes its own index; the server rewrites
    /// it per target, so on arrival it indexes <em>this</em> process's registry and a receiver
    /// never has to know how the sender ordered its mods.
    /// </summary>
    public byte NetModIndex { get; }

    /// <summary>Present exactly when <see cref="IsExtended"/>.</summary>
    public PacketRoute? Route { get; }

    /// <summary>Where the payload starts, relative to the frame body (the bytes after the header).</summary>
    public int PayloadOffset { get; }

    /// <summary>Size of the whole frame, header included.</summary>
    public int TotalSize { get; }

    /// <summary>
    /// The sender's batching hint: it describes the packet, not the hop, so a relay preserves it.
    /// </summary>
    public bool Batchable => (Flags & PacketFrameFlags.Batchable) != 0;

    internal ParsedFrame(
        PacketEnvelope envelope,
        PacketFrameFlags flags,
        bool isExtended,
        byte id,
        byte netModIndex,
        PacketRoute? route,
        int payloadOffset,
        int totalSize
    )
    {
        Envelope = envelope;
        Flags = flags;
        IsExtended = isExtended;
        ID = id;
        NetModIndex = netModIndex;
        Route = route;
        PayloadOffset = payloadOffset;
        TotalSize = totalSize;
    }
}

public static partial class PacketFraming
{
    /// <summary>Writes a built-in packet frame.</summary>
    public static void WriteFrame(
        ByteArrayBufferWriter output,
        PacketEnvelope envelope,
        IContextualPacket packet,
        IPacketSerializationContext context
    )
    {
        ArgumentNullException.ThrowIfNull(packet);

        byte id = PacketRegistry.GetPacketID(packet);
        RefBinaryWriter writer = new(output);
        int frameStart = BeginFrame(output, ref writer, envelope, id, false, 0, null, packet.CanBatch);
        packet.Serialize(ref writer, context);
        writer.Flush();
        EndFrame(output, frameStart, packet.GetType());
    }

    /// <summary>
    /// Writes a net-mod packet frame. The route is mandatory and has to agree with the scope the
    /// type declared at registration, which is re-checked here so that a caller bypassing
    /// <see cref="PacketHandle{TPacket}"/> still fails loud instead of sending a misrouted frame.
    /// </summary>
    public static void WriteFrame(
        ByteArrayBufferWriter output,
        PacketEnvelope envelope,
        in PacketID id,
        in PacketRoute route,
        IContextualPacket packet,
        IPacketSerializationContext context
    )
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (!id.IsExtended)
            throw new InvalidOperationException(SR.FrameIsNotExtended);

        if (!MiaoNetNetMods.TryGetPacket(packet.GetType(), out byte netModIndex, out byte localID, out PacketScope scope)
            || netModIndex != id.NetModIndex
            || localID != id.Value)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.UnknownNetModPacket, packet.GetType().FullName, id.Value
            ));
        }

        route.CheckAgainst(scope);

        PacketRoute? routeBox = route;
        RefBinaryWriter writer = new(output);
        int frameStart = BeginFrame(output, ref writer, envelope, id.Value, true, id.NetModIndex, routeBox, packet.CanBatch);
        packet.Serialize(ref writer, context);
        writer.Flush();
        EndFrame(output, frameStart, packet.GetType());
    }

#if MIAO_SERVER
    /// <summary>
    /// Re-emits a net-mod frame the server routed without decoding, addressed by the index the
    /// receiving connection gave to the sending mod. That index is the only byte that differs
    /// from what arrived; the payload is the very same memory for every target.
    /// </summary>
    public static void WriteFrame(
        ByteArrayBufferWriter output,
        PacketEnvelope envelope,
        in RelayedFrame relay,
        byte relayNetModIndex
    )
    {
        PacketRoute? routeBox = relay.Route;
        RefBinaryWriter writer = new(output);
        int frameStart = BeginFrame(output, ref writer, envelope, relay.ID.Value, true, relayNetModIndex, routeBox, relay.Batchable);
        writer.WriteSpan(relay.Payload.Span);
        writer.Flush();
        EndFrame(output, frameStart, typeof(RelayedFrame));
    }

    /// <summary>
    /// Copies a parsed extended frame into the carrier the server forwards: one copy, owned by
    /// the managed heap so that every target can share the very same bytes. The index inside
    /// stays the sender's own, which is what the service needs to look the declaration up.
    /// </summary>
    public static RelayedFrame ToRelay(ReadOnlySpan<byte> body, in ParsedFrame frame)
    {
        if (!frame.IsExtended || frame.Route is not { } route)
            throw new InvalidDataException(SR.FrameIsNotExtended);

        return new RelayedFrame(
            PacketID.Extended(frame.NetModIndex, frame.ID),
            route,
            frame.Batchable,
            Payload(body, frame).ToArray()
        );
    }
#endif

    /// <summary>Writes whatever a queue holds, packet or relayed bytes alike.</summary>
    public static void WriteFrame(
        ByteArrayBufferWriter output,
        in OutgoingFrame frame,
        IPacketSerializationContext context
    )
    {
        if (frame.Packet is { } packet)
        {
            if (frame.Route is { } route)
                WriteFrame(output, frame.Envelope, frame.ID, route, packet, context);
            else
                WriteFrame(output, frame.Envelope, packet, context);
            return;
        }

#if MIAO_SERVER
        WriteFrame(output, frame.Envelope, frame.Relay!.Value, frame.RelayNetModIndex);
#else
        throw new InvalidOperationException(SR.FrameHasNoPacket);
#endif
    }

    /// <summary>
    /// Parses a frame's own fields out of its header and body. The body is the frame's payload
    /// region, i.e. exactly the number of bytes the header's size field claims.
    /// </summary>
    public static bool TryParseFrame(ReadOnlySpan<byte> header, ReadOnlySpan<byte> body, out ParsedFrame frame)
    {
        frame = default;

        if (header.Length < Connection.PacketHeaderSize)
            return false;

        int size = BinaryPrimitives.ReadUInt16LittleEndian(header);
        if (body.Length < size)
            return false;

        body = body[..size];
        byte id = header[sizeof(ushort)];
        PacketFrameFlags flags = (PacketFrameFlags)header[sizeof(ushort) + sizeof(byte)];

        RefBinaryReader reader = new(body);

        bool isExtended = (flags & PacketFrameFlags.Extended) != 0;
        byte netModIndex = 0;
        PacketRoute? route = null;
        if (isExtended)
        {
            netModIndex = reader.ReadByte();
            route = PacketRoute.Read(ref reader);
        }

        PacketEnvelope envelope = PacketEnvelope.ReadOptional(
            ref reader, (PacketEnvelopeFlags)(flags & PacketFrameFlags.EnvelopeMask)
        );

        frame = new ParsedFrame(
            envelope,
            flags,
            isExtended,
            id,
            netModIndex,
            route,
            body.Length - reader.BytesLeft,
            Connection.PacketHeaderSize + size
        );
        return true;
    }

    /// <summary>Parses a frame out of a whole-frame buffer, without decoding its payload.</summary>
    public static bool TryReadFrame(ReadOnlySpan<byte> source, out ParsedFrame frame, out int bytesConsumed)
    {
        frame = default;
        bytesConsumed = 0;

        if (source.Length < Connection.PacketHeaderSize)
            return false;

        int size = BinaryPrimitives.ReadUInt16LittleEndian(source);
        if (source.Length < Connection.PacketHeaderSize + size)
            return false;

        if (!TryParseFrame(source, source[Connection.PacketHeaderSize..], out frame))
            return false;

        bytesConsumed = frame.TotalSize;
        return true;
    }

    /// <summary>
    /// Resolves a parsed frame's packet object, built-in or net-mod alike.
    /// <para>
    /// A net-mod frame indexes this process's own registry: the server has already rewritten the
    /// sender's index into the target's, which also means nothing here touches connection state
    /// on the client. Decoding is therefore safe on the receive thread, even while the main
    /// thread has not yet applied the join notification that arrived next to the frame.
    /// </para>
    /// <para>
    /// Every failure here throws: at this stage a frame is either a bug or a protocol violation,
    /// never something to skip quietly.
    /// </para>
    /// </summary>
    /// <param name="body">The frame body, i.e. the bytes after the frame header.</param>
    public static IContextualPacket Decode(
        ReadOnlySpan<byte> body,
        in ParsedFrame frame,
        IPacketSerializationContext context,
        out PacketID id
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!frame.IsExtended)
        {
            id = PacketID.BuiltIn(frame.ID);
            return Read(frame.ID, PacketRegistry.GetPacketReader(frame.ID), body, frame, context);
        }

        if (!frame.Envelope.HasSender)
        {
            // Only the server writes the sender, and it always does so for a relayed frame: a
            // net-mod frame without one did not come from the server.
            throw new InvalidDataException(SR.ExtendedFrameHasNoSender);
        }

        if (!MiaoNetNetMods.TryGetPacketInfo(frame.NetModIndex, frame.ID, out PacketScope scope, out RefBinaryPacketReadHandler reader))
        {
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture, SR.UnknownNetModPacket, frame.NetModIndex, frame.ID
            ));
        }

        // The scope travels on the wire for the server's sake. Here it also catches two peers
        // that declared the same mod's packet types in different orders.
        if (frame.Route is not { } route || route.Scope != scope)
        {
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture, SR.NetModScopeMismatchOnWire, frame.NetModIndex, frame.Route?.Scope, scope
            ));
        }

        id = PacketID.Extended(frame.NetModIndex, frame.ID);
        return Read(frame.ID, reader, body, frame, context);
    }

    /// <summary>Decodes a frame that is held in one contiguous buffer.</summary>
    public static IContextualPacket DecodeFrame(
        ReadOnlySpan<byte> frame,
        in ParsedFrame parsed,
        IPacketSerializationContext context,
        out PacketID id
    ) => Decode(frame[Connection.PacketHeaderSize..], parsed, context, out id);

    public static async ValueTask<EnvelopedPacket?> ReadFrameAsync(
        Stream stream,
        IPacketSerializationContext context,
        CancellationToken cancellationToken
    )
    {
        byte[] headerBuffer = pool.Rent(Connection.PacketHeaderSize);
        try
        {
            return await ReadFrameAsync(
                stream,
                headerBuffer.AsMemory(0, Connection.PacketHeaderSize),
                context,
                cancellationToken
            );
        }
        finally
        {
            pool.Return(headerBuffer);
        }
    }

    internal static async ValueTask<EnvelopedPacket?> ReadFrameAsync(
        Stream stream,
        Memory<byte> headerMemory,
        IPacketSerializationContext context,
        CancellationToken cancellationToken
    )
    {
        if (headerMemory.Length < Connection.PacketHeaderSize)
            throw new ArgumentException(SR.PacketHeaderBufferTooSmall, nameof(headerMemory));

        headerMemory = headerMemory[..Connection.PacketHeaderSize];
        int headerBytesRead = await stream.ReadAtLeastAsync(
            headerMemory,
            Connection.PacketHeaderSize,
            throwOnEndOfStream: false,
            cancellationToken
        );
        if (headerBytesRead < Connection.PacketHeaderSize)
        {
            if (headerBytesRead > 0)
                throw new PacketTruncatedException(isPayload: false, headerBytesRead, Connection.PacketHeaderSize);
            return null;
        }

        ushort payloadSize = BinaryPrimitives.ReadUInt16LittleEndian(headerMemory.Span);

        byte[] payloadBuffer = pool.Rent(payloadSize);
        try
        {
            Memory<byte> body = payloadBuffer.AsMemory(0, payloadSize);
            if (payloadSize > 0)
            {
                int payloadBytesRead = await stream.ReadAtLeastAsync(
                    body,
                    payloadSize,
                    throwOnEndOfStream: false,
                    cancellationToken
                );
                if (payloadBytesRead < payloadSize)
                    throw new PacketTruncatedException(isPayload: true, payloadBytesRead, payloadSize);
            }

            try
            {
                if (!TryParseFrame(headerMemory.Span, body.Span, out ParsedFrame parsed))
                    throw new InvalidDataException(SR.FrameIsNotExtended);

                IContextualPacket packet = Decode(body.Span, parsed, context, out PacketID id);
                return new EnvelopedPacket(parsed.Envelope, id, packet);
            }
            catch (Exception exception)
            {
                throw new InvalidPacketDataException(body.ToArray(), exception);
            }
        }
        finally
        {
            pool.Return(payloadBuffer);
        }
    }

    /// <summary>
    /// The payload of a parsed frame, sliced by the length the frame declares rather than to the
    /// end of the buffer: a buffer may well hold the frames that follow it.
    /// </summary>
    private static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> body, in ParsedFrame frame)
        => body.Slice(
            frame.PayloadOffset,
            frame.TotalSize - Connection.PacketHeaderSize - frame.PayloadOffset
        );

    private static IContextualPacket Read(
        byte id,
        RefBinaryPacketReadHandler reader,
        ReadOnlySpan<byte> body,
        in ParsedFrame frame,
        IPacketSerializationContext context
    )
    {
        RefBinaryReader inner = new(Payload(body, frame));
        IContextualPacket packet = reader(ref inner, context);
        if (inner.BytesLeft != 0)
        {
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture, SR.PacketHasDataLeft, id, inner.BytesLeft
            ));
        }

        return packet;
    }

    /// <summary>
    /// Writes everything ahead of the payload and returns where the frame started, which
    /// <see cref="EndFrame"/> needs in order to backfill the size.
    /// </summary>
    private static int BeginFrame(
        ByteArrayBufferWriter output,
        ref RefBinaryWriter writer,
        PacketEnvelope envelope,
        byte id,
        bool isExtended,
        byte netModIndex,
        PacketRoute? route,
        bool batchable
    )
    {
        int frameStart = output.WrittenCount;

        PacketFrameFlags flags = (PacketFrameFlags)envelope.Flags;
        if (isExtended)
            flags |= PacketFrameFlags.Extended;
        if (batchable)
            flags |= PacketFrameFlags.Batchable;

        writer.Write((ushort)0);
        writer.Write(id);
        writer.Write((byte)flags);

        if (isExtended)
        {
            // Every extended frame carries a route: it is all the server has to go on.
            // The index is the sender's own here; the server replaces it for each target.
            writer.Write(netModIndex);
            (route ?? throw new InvalidOperationException(SR.FrameHasNoRoute)).Write(ref writer);
        }

        envelope.WriteOptional(ref writer);
        return frameStart;
    }

    /// <summary>Backfills the payload size. <c>writer.Flush()</c> must have run first.</summary>
    private static void EndFrame(ByteArrayBufferWriter output, int frameStart, Type packetType)
    {
        int payloadSize = output.WrittenCount - frameStart - Connection.PacketHeaderSize;
        if (payloadSize > Connection.MaxPayloadSize)
        {
            throw new PacketTooLargeException(
                packetType,
                payloadSize,
                Connection.MaxPayloadSize
            );
        }

        Span<byte> frame = output.WrittenSpan[frameStart..];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)payloadSize);
    }
}
