namespace Celeste.Mod.MiaoNet;

internal static class OobeImages
{
    private const string BasePath = "Graphics/MiaoNet/Oobe/";

    public const string PlayersInMap = BasePath + "players-in-map";

    public const string LoginInGame = BasePath + "login-in-game";

    public const string ConnectedToServer = BasePath + "connected-to-server";

    public const string PlayerList = BasePath + "player-list";

    public const string ChatBox = BasePath + "chatbox";

    public const string CommandAndTabCompletion = BasePath + "command-and-tab-completion";

    public const string EmoteByShortcut = BasePath + "emote-by-shortcut";

    public const string EmoteByCommand = BasePath + "emote-by-command";

    public const string SelfInPublicChannel = BasePath + "self-in-public-channel";

    public const string PlayersInPublicChannel = BasePath + "players-in-public-channel";

    public const string SelfInPrivateChannel = BasePath + "self-in-private-channel";

    public const string PlayersInPrivateChannel = BasePath + "players-in-private-channel";

    public const string GroupPhoto = BasePath + "group-photo";

    public const string Fireworks = BasePath + "fireworks";

    public static readonly string[] All =
    [
        LoginInGame,
        ConnectedToServer,
        PlayerList,
        ChatBox,
        CommandAndTabCompletion,
        PlayersInMap,
        EmoteByShortcut,
        EmoteByCommand,
        SelfInPublicChannel,
        PlayersInPublicChannel,
        SelfInPrivateChannel,
        PlayersInPrivateChannel,
        GroupPhoto,
        Fireworks,
    ];
}
