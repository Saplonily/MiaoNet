using System.Globalization;

namespace MiaoNet.Shared;

/// <summary>
/// The wire representation of "who receives this net-mod packet".
/// <para>
/// It is read and acted on by the server alone: the sender knows the scope from its
/// packet type, and a receiver knows it from its own registration, so neither end has
/// to look at this block.
/// </para>
/// </summary>
public readonly struct PacketRoute
{
    /// <summary>Scale of the target list, checked before it is written to the wire.</summary>
    public const int MaxTargets = 255;

    public PacketScope Scope { get; }

    /// <summary>
    /// The explicit targets, in the sender's order: exactly one for
    /// <see cref="PacketScope.Player"/>, at least one for <see cref="PacketScope.Players"/>,
    /// and empty for the broadcast scopes.
    /// </summary>
    public IReadOnlyList<int> Targets { get; }

    private PacketRoute(PacketScope scope, IReadOnlyList<int> targets)
    {
        Scope = scope;
        Targets = targets;
    }

    /// <summary>The route of a packet whose receivers come from the sender's own position.</summary>
    public static PacketRoute Broadcast(PacketScope scope)
    {
        if (!scope.IsBroadcast())
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.BroadcastScopeRequired, scope
            ));
        }

        return new(scope, Array.Empty<int>());
    }

    /// <summary>The route of a packet addressed to exactly one player.</summary>
    public static PacketRoute Unicast(int targetPlayerID)
        => new(PacketScope.Player, [targetPlayerID]);

    /// <summary>The route of a packet addressed to several players at once.</summary>
    public static PacketRoute Multicast(IReadOnlyList<int> targetPlayerIDs)
    {
        ArgumentNullException.ThrowIfNull(targetPlayerIDs);

        if (targetPlayerIDs.Count is 0 or > MaxTargets)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetPlayerIDs),
                targetPlayerIDs.Count,
                string.Format(CultureInfo.InvariantCulture, SR.InvalidMulticastTargetCount, MaxTargets)
            );
        }

        return new(PacketScope.Players, [.. targetPlayerIDs]);
    }

    /// <summary>
    /// Fails loud when a route does not match the scope its packet type declared.
    /// Both ends know the scope from the registration, so a mismatch is a bug, not
    /// something to recover from.
    /// </summary>
    public void CheckAgainst(PacketScope scope)
    {
        if (Scope != scope)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.RouteScopeMismatch, Scope, scope
            ));
        }

        if (scope == PacketScope.Player && Targets.Count != 1)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.InvalidUnicastTargetCount, Targets.Count
            ));
        }

        if (scope == PacketScope.Players && Targets.Count is 0 or > MaxTargets)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.InvalidMulticastTargetCount, MaxTargets
            ));
        }
    }

    public void Write(ref RefBinaryWriter writer)
    {
        writer.Write((byte)Scope);
        switch (Scope)
        {
            case PacketScope.Player:
                // Unicast: the count is implied by the scope.
                writer.Write7BitEncodedInt(Targets[0]);
                break;

            case PacketScope.Players:
                writer.Write7BitEncodedInt(Targets.Count);
                foreach (int targetPlayerID in Targets)
                    writer.Write7BitEncodedInt(targetPlayerID);
                break;
        }
    }

    public static PacketRoute Read(ref RefBinaryReader reader)
    {
        PacketScope scope = (PacketScope)reader.ReadByte();

        switch (scope)
        {
            case PacketScope.Player:
                return Unicast(reader.Read7BitEncodedInt());

            case PacketScope.Players:
                {
                    int count = reader.Read7BitEncodedInt();
                    // Every id costs at least one byte, so this bounds the allocation
                    // before the count is trusted: a hostile peer must not be able to
                    // make us allocate wildly.
                    if ((uint)count > (uint)reader.BytesLeft)
                    {
                        throw new InvalidDataException(string.Format(
                            CultureInfo.InvariantCulture, SR.InvalidScopeTargetCount, count, reader.BytesLeft
                        ));
                    }

                    int[] targets = new int[count];
                    for (int i = 0; i < count; i++)
                        targets[i] = reader.Read7BitEncodedInt();
                    return new(PacketScope.Players, targets);
                }

            case PacketScope.Global:
            case PacketScope.Channel:
            case PacketScope.Map:
                return Broadcast(scope);

            default:
                throw new InvalidDataException(string.Format(
                    CultureInfo.InvariantCulture, SR.UnknownPacketScope, (byte)scope
                ));
        }
    }

    public override string ToString()
        => Targets.Count == 0 ? Scope.ToString() : $"{Scope}[{string.Join(',', Targets)}]";
}
