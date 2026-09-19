using System.Buffers;
using System.Buffers.Binary;

namespace MiaoNet.Shared;

public static partial class PacketFraming
{
    private static readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Writes a size-prefixed value. The handshake happens before any packet frame exists,
    /// so it does not use the frame format.
    /// </summary>
    public static void WriteSizePrefixed<T>(ByteArrayBufferWriter output, T value)
        where T : IRefBinarySerializable<T>
    {
        int frameStart = output.WrittenCount;

        RefBinaryWriter writer = new(output);
        writer.Write((ushort)0);
        value.Serialize(ref writer);
        writer.Flush();

        int size = output.WrittenCount - frameStart - sizeof(ushort);
        if (size > Connection.MaxPayloadSize)
            throw new ArgumentOutOfRangeException(nameof(value));

        Span<byte> frame = output.WrittenSpan[frameStart..];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)size);
    }
}
