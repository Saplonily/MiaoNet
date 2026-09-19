using System.Globalization;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed partial class MiaoServerService
{
    /// <summary>
    /// Routes a net-mod frame that the server deliberately did not decode.
    /// <para>
    /// The scope and the targets come from the frame itself; the owning mod comes from the
    /// sender's own declaration, and the same lookup on the receiving side gives the index the
    /// frame has to carry — that translation is the whole reason a receiver never needs to learn
    /// anybody else's net-mod list. Nothing here needs the mod's code, which is exactly why the
    /// server can host mods it has never heard of.
    /// </para>
    /// </summary>
    public async ValueTask HandleRelayedFrameAsync(MiaoClientConnection connection, RelayedFrame relay)
    {
        byte netModIndex = relay.ID.NetModIndex;
        if (netModIndex >= connection.NetMods.Count)
        {
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture, SR.NetModIndexOutOfRange, connection.NetMods.Count, netModIndex
            ));
        }

        // Same name and same major version, otherwise the bytes mean nothing to a receiver.
        HandshakeData.NetMod senderMod = connection.NetMods[netModIndex];

        // The sender is the connection itself: a client must never claim one, and here it is
        // written into the envelope the receivers see.
        PacketEnvelope envelope = PacketEnvelope.FromSender(connection.ID);

        switch (relay.Route.Scope)
        {
            case PacketScope.Global:
                await RelayToAsync(connection, relay, envelope, PlayersOf(serverState), senderMod, excludeSender: true);
                break;

            case PacketScope.Channel:
                await RelayToAsync(connection, relay, envelope, PlayersOf(connection.Player.Channel), senderMod, excludeSender: true);
                break;

            case PacketScope.Map:
                {
                    ServerChannel channel = connection.Player.Channel;
                    if (!channel.Maps.TryGetValue(connection.Player.Location.Map, out ServerMap? map))
                        return;

                    await RelayToAsync(connection, relay, envelope, PlayersOf(map), senderMod, excludeSender: true);
                    break;
                }

            case PacketScope.Player:
            case PacketScope.Players:
                {
                    // Explicit targets are taken verbatim and may include the sender itself.
                    List<MiaoClientConnection> targets = [];
                    foreach (int targetPlayerID in relay.Route.Targets)
                    {
                        // A missing id is a race (the player just left), not a protocol error.
                        if (ServerState.Players.TryGetValue(targetPlayerID, out MiaoClientConnection? target))
                            targets.Add(target);
                    }

                    await RelayToAsync(connection, relay, envelope, targets, senderMod, excludeSender: false);
                    break;
                }

            default:
                throw new InvalidDataException(string.Format(
                    CultureInfo.InvariantCulture, SR.UnknownPacketScope, (byte)relay.Route.Scope
                ));
        }
    }

    /// <summary>
    /// The players of a scope. Going through the interface on purpose: a state object also
    /// exposes a dictionary of the same name, and the scope's view is the one to route by.
    /// </summary>
    private static IEnumerable<MiaoClientConnection> PlayersOf(IPlayerScope scope) => scope.Players;

    private static async ValueTask RelayToAsync(
        MiaoClientConnection sender,
        RelayedFrame relay,
        PacketEnvelope envelope,
        IEnumerable<MiaoClientConnection> candidates,
        HandshakeData.NetMod senderMod,
        bool excludeSender
    )
    {
        List<Task>? bounded = null;
        foreach (MiaoClientConnection target in candidates)
        {
            // The broadcast scopes describe the peers around the sender, so the sender is not
            // among them; the explicit scopes were chosen by the sender and are kept as they are.
            if (excludeSender && target.ID == sender.ID)
                continue;

            // The index the target gave to the same mod is what the frame must carry.
            if (!target.TryGetNetModIndex(senderMod, out byte relayNetModIndex))
                continue;

            if (!target.TryQueueRelayedFrame(envelope, in relay, relayNetModIndex))
                (bounded ??= []).Add(target.QueueRelayedFrameAsync(envelope, in relay, relayNetModIndex).AsTask());
        }

        if (bounded is not null)
            await Task.WhenAll(bounded);
    }
}
