namespace MiaoNet.Shared;

internal static class SR
{
    public const string MissingPooledString
        = "A pooled string(id {0}) is missing but doesn't contain value.";
    public const string PooledStringValueNotMatch
        = "A pooled string is found locally(\"{0}\") but remote provided a different value(\"{1}\").";
    public const string InvalidPooledStringID
        = "A pooled string id must be positive, but remote provided {0}.";
    public const string UnexpectedPooledStringID
        = "A new pooled string has id {0}, but the next expected id is {1}.";
    public const string PooledStringEntryLimitExceeded
        = "The remote pooled string entry limit has been exceeded.";
    public const string PooledStringValueTooLarge
        = "A remote pooled string uses {0} UTF-8 bytes, exceeding the limit of {1}.";
    public const string PooledStringTotalBytesExceeded
        = "The remote pooled string UTF-8 byte limit has been exceeded.";
    public const string TypeMustAtLeaseImplContextualPacket
        = "A packet type being registered must at lease implement IContextualPacket.";
    public const string TooManyPacketsRegistered
        = "At most 255 packets can be registered.";
    public const string TypeIsNotRegisteredAsAPacket
        = "Type \"{0}\" is not registered as a packet.";
    public const string PacketNotFoundByID
        = "Packet type with id {0} is not found.";
    public const string PacketTooLarge
        = "Packet \"{0}\" is too large with payload size {1}; the maximum is {2}.";
    public const string PacketTruncated
        = "Packet {0} is truncated: read {1} of {2} bytes.";
    public const string PacketHeaderBufferTooSmall
        = "The packet header buffer is too small.";
    public const string PacketPartHeader
        = "header";
    public const string PacketPartPayload
        = "payload";
    public const string PacketHasDataLeft
        = "Packet id {0} read finished but left {1} bytes not read.";
    public const string HasDataLeft
        = "Object {0} read finished but left {1} bytes not read.";
    public const string DeltasLengthMismatch
        = "Length of deltas {0} mismatched length {1}.";
    public const string EnvelopeHasNoSenderID
        = "The packet envelope has no sender id, but the sender id was requested.";
    public const string EnvelopeHasNoRequestID
        = "The packet envelope has no request id, but the request id was requested.";
    public const string InvalidScopeTargetCount
        = "A scope packet claims {0} targets, but only {1} bytes are left to read them from.";
    public const string InvalidUnicastTargetCount
        = "A unicast packet must address exactly one player, but {0} were given.";
    public const string InvalidMulticastTargetCount
        = "A multicast packet must address between 1 and {0} players.";
    public const string RouteScopeMismatch
        = "A packet was routed as {0}, but its type declares the {1} scope.";
    public const string UnknownPacketScope
        = "Frame carries unknown packet scope {0}.";
    public const string FrameIsNotExtended
        = "A route block was read from a frame that does not carry an extended packet id.";
    public const string FrameHasNoRoute
        = "An extended frame from a client carries no route block.";
    public const string NetModIndexOutOfRange
        = "The sender declared {0} net-mod(s), but referenced index {1}.";
    public const string UnknownNetModPacket
        = "No net-mod is registered at index {0}, so packet id {1} cannot be decoded.";
    public const string NetModAlreadyRegistered
        = "A net-mod named \"{0}\" is already registered; unregister it before registering it again.";
    public const string NetModNotRegistered
        = "No net-mod named \"{0}\" is registered.";
    public const string NetModPacketTypeAlreadyRegistered
        = "Net-mod \"{0}\" already registered the packet type \"{1}\".";
    public const string NetModIndexSpaceExhausted
        = "At most {0} net-mods can be registered.";
    public const string NetModPacketIDSpaceExhausted
        = "Net-mod \"{0}\" registered more than {1} packet types.";
    public const string HandleDisposed
        = "The net-mod \"{0}\" was unregistered, so this packet handle is no longer usable.";
    public const string NetModNotConnected
        = "A net-mod packet was sent while no connection is installed.";
    public const string NetModSendNotAllowed
        = "Net-mod \"{0}\" packet \"{1}\" declares the {2} scope, so {3} is not applicable; use {4}.";
    public const string BroadcastScopeRequired
        = "A broadcast route requires a broadcast scope, but {0} was given.";
    public const string BuiltInPacketIDZero
        = "A built-in packet id must not be zero.";
    public const string NetModScopeMismatchOnWire
        = "The net-mod at index {0} arrived with scope {1}, but its registered type declares {2}.";
    public const string ExtendedFrameMustNotCarryEnvelope
        = "A client sent an extended frame carrying an envelope field, which only the server may set.";
    public const string ExtendedFrameHasNoSender
        = "A net-mod frame arrived without a sender, which only a frame relayed by the server has.";
    public const string FrameHasNoPacket
        = "An outgoing frame carries neither a packet nor relayed bytes.";
}
