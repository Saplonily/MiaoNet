using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using MiaoNet.Shared;

namespace PacketDumpInspector;

public sealed class Program
{
    public static void Main()
    {
        var ctx = new InspectorContext();
        JsonSerializerOptions options = new()
        {
            IncludeFields = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            Converters = { new JsonStringEnumConverter() },
            WriteIndented = true
        };

        Console.Write("File name:\n> ");
        string fileName = Console.ReadLine()!.Trim('\"');
        var bytes = File.ReadAllBytes(fileName);
        ReadOnlySpan<byte> span = bytes;
        while (true)
        {
            if (!PacketFraming.TryReadFrame(span, out ParsedFrame parsed, out int consumed))
                break;

            if (parsed.IsExtended)
            {
                // A net-mod index only means something to a process that declares the mod, so a
                // dump can describe such a frame but never decode it.
                Console.WriteLine($"===net-mod frame: mod index {parsed.NetModIndex}, packet {parsed.ID}===");
                Console.WriteLine($"route: {parsed.Route}");
                Console.WriteLine($"payload: {parsed.TotalSize - Connection.PacketHeaderSize - parsed.PayloadOffset} bytes");
                span = span[consumed..];
                continue;
            }

            IContextualPacket packet = PacketFraming.DecodeFrame(span, parsed, ctx, out PacketID id);
            var text = JsonSerializer.Serialize<object>(packet, options);
            Console.WriteLine($"==={packet.GetType().FullName} ({id})===");
            string sender = parsed.Envelope.HasSender ? parsed.Envelope.SenderPlayerID.ToString() : "-";
            string request = parsed.Envelope.HasRequestID ? parsed.Envelope.RequestID.ToString() : "-";
            Console.WriteLine($"envelope: sender={sender}, request={request}");
            Console.WriteLine(text);
            span = span[consumed..];
        }
    }

    private sealed class InspectorContext : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }
}
