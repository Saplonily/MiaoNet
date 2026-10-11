using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

partial class OuiMiaoNetOobe
{
    private OobePage[] CreatePages()
    {
        MiaoNetModuleSettings settings = MiaoNetModule.Settings;

        return [
            new OobePage("miaonet_oobe_title",
                CreateItems: (menu, step) => {
                    menu.Add(new TextMenu.SubHeader(Dialog.Clean("miaonet_oobe_welcome"), false));
                    menu.Add(new TextMenu.SubHeader(string.Empty));
                    menu.Add(new TextMenu.Button(Dialog.Get("miaonet_oobe_start")).Pressed(
                        () => StartGotoTransition(step + 1)
                    ));
                    menu.Add(new TextMenu.Button(Dialog.Get("miaonet_oobe_button_skip")).Pressed(Exit));
                },
                AddNavigation: false
            ),

            new OobePage("miaonet_oobe_login_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_login_desc"),
                    new PanelImageRow(OobeImages.LoginInGame)
                ],
                CreateItems: (menu, _) => AddLoginItems(menu)
            ),

            new OobePage("miaonet_oobe_connect_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_connect_desc"),
                    new PanelImageRow(OobeImages.ConnectedToServer)
                ],
                CreateItems: (menu, _) => AddConnectionItems(menu, settings)
            ),

            new OobePage("miaonet_oobe_playerlist_header",
                CreatePanel: () => [
                    new PanelText(
                        "miaonet_oobe_playerlist_desc",
                        InputButton(settings.PlayerListButton),
                        InputButton(settings.PlayerListScrollUp),
                        InputButton(settings.PlayerListScrollDown)
                    ),
                    new PanelImageRow(OobeImages.PlayerList)
                ],
                CreateItems: (menu, _) => {
                    menu.Add(CreateDisplayOnlyBindingSetting(
                        Dialog.Get("miaonet_options_button_player_list"), settings.PlayerListButton
                    ));
                    menu.Add(CreateDisplayOnlyBindingSetting(
                        Dialog.Get("miaonet_options_button_player_list_scroll_up"), settings.PlayerListScrollUp
                    ));
                    menu.Add(CreateDisplayOnlyBindingSetting(
                        Dialog.Get("miaonet_options_button_player_list_scroll_down"), settings.PlayerListScrollDown
                    ));
                }
            ),

            new OobePage("miaonet_oobe_chat_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_chat_desc",
                        InputButton(settings.ChatButton),
                        InputKey(Keys.PageUp),
                        InputKey(Keys.PageDown),
                        InputKey(Keys.Enter),
                        InputKey(Keys.Escape)
                    ),
                    new PanelImageRow(OobeImages.ChatBox)
                ],
                CreateItems: (menu, _) => menu.Add(
                    CreateDisplayOnlyBindingSetting(Dialog.Get("miaonet_options_button_chat"), settings.ChatButton)
                )
            ),

            new OobePage("miaonet_oobe_command_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_command_desc"),
                    new PanelImageRow(OobeImages.CommandAndTabCompletion)
                ],
                CreateItems: (menu, _) => menu.Add(
                    CreateDisplayOnlyBindingSetting(Dialog.Get("miaonet_options_button_chat_command"), settings.ChatCommandButton)
                )
            ),

            new OobePage("miaonet_oobe_players_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_players_desc"),
                    new PanelImageRow(OobeImages.PlayersInMap)
                ]
            ),

            new OobePage("miaonet_oobe_emote_shortcut_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_emote_shortcut_desc"),
                    new PanelImageRow(OobeImages.EmoteByShortcut)
                ]
            ),

            new OobePage("miaonet_oobe_emote_command_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_emote_command_desc"),
                    new PanelImageRow(OobeImages.EmoteByCommand)
                ]
            ),

            new OobePage("miaonet_oobe_emote_file_header",
                CreateItems: (menu, _) => {
                    menu.Add(new TextMenu.SubHeader(Dialog.Clean("miaonet_oobe_emote_file_desc"), false));
                    menu.Add(new TextMenu.SubHeader(""));
                    menu.Add(new TextMenu.Button(Dialog.Get("miaonet_options_open_emotes_file"))
                        .Pressed(MiaoNetModule.Instance.OpenEmotesFile));
                    menu.Add(new TextMenu.Button(Dialog.Get("miaonet_options_reload_emote_settings"))
                        .Pressed(MiaoNetModule.Instance.LoadEmotes));
                }
            ),

            new OobePage("miaonet_oobe_teleport_header",
                CreateItems: (menu, _) => menu.Add(
                    new TextMenu.SubHeader(Dialog.Clean("miaonet_oobe_teleport_desc"), false)
                )
            ),

            new OobePage("miaonet_oobe_watch_header",
                CreateItems: (menu, _) => menu.Add(
                    new TextMenu.SubHeader(Dialog.Clean("miaonet_oobe_watch_desc"), false)
                )
            ),

            new OobePage("miaonet_oobe_mention_header",
                CreateItems: (menu, _) => menu.Add(new SubHeaderWithInputs(
                    "miaonet_oobe_mention_desc", false,
                    InputButton(Input.MenuUp), InputButton(Input.MenuDown), InputKey(Keys.Tab)
                ))
            ),

            new OobePage("miaonet_oobe_interactions_header",
                CreateItems: (menu, _) => {
                    menu.Add(new TextMenu.SubHeader(Dialog.Clean("miaonet_oobe_interactions_desc"), false));
                    menu.Add(new TextMenu.SubHeader(""));
                    menu.Add(new TextMenu.OnOff(
                        Dialog.Get("miaonet_options_player_interactions"),
                        settings.PlayerInteractions
                    ).Change(v => settings.PlayerInteractions = v));
                }
            ),

            new OobePage("miaonet_oobe_channel_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_channel_desc"),
                    new PanelImageRow(OobeImages.SelfInPublicChannel, OobeImages.PlayersInPublicChannel)
                ]
            ),

            new OobePage("miaonet_oobe_channel_private_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_channel_tip"),
                    new PanelImageRow(OobeImages.SelfInPrivateChannel, OobeImages.PlayersInPrivateChannel)
                ]
            ),

            new OobePage("miaonet_oobe_chat_scopes_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_chat_scopes_desc",
                        InputKey(Keys.LeftShift), InputKey(Keys.Left), InputKey(Keys.Right)
                    ),
                    new PanelImageRow(OobeImages.ChatBox)
                ]
            ),

            new OobePage("miaonet_oobe_photo_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_photo_desc",
                        InputMouse(MInput.MouseData.MouseButtons.Left),
                        InputMouse(MInput.MouseData.MouseButtons.Middle),
                        InputMouse(MInput.MouseData.MouseButtons.Right)
                    ),
                    new PanelImageRow(OobeImages.GroupPhoto)
                ]
            ),

            new OobePage("miaonet_oobe_fireworks_header",
                CreatePanel: () => [
                    new PanelText("miaonet_oobe_photo_tip", InputButton(settings.CreateFireworksButton)),
                    new PanelImageRow(OobeImages.Fireworks)
                ],
                CreateItems: (menu, _) => {
                    menu.Add(CreateDisplayOnlyBindingSetting(
                        Dialog.Get("miaonet_options_button_create_fireworks"), settings.CreateFireworksButton
                    ));

                    var fireworks = new TextMenu.OnOff(
                        Dialog.Get("miaonet_options_fireworks"), settings.Fireworks
                    ).Change(v => settings.Fireworks = v);
                    menu.Add(fireworks);
                    fireworks.AddDescription(menu, Dialog.Clean("miaonet_options_fireworks_tip"));
                }
            ),

            new OobePage("miaonet_oobe_goldening_protection_header",
                CreateItems: (menu, _) => menu.Add(
                    new TextMenu.SubHeader(Dialog.Clean("miaonet_oobe_goldening_protection_desc"), false)
                )
            ),

            new OobePage("miaonet_oobe_livemode_header",
                CreateItems: (menu, _) => {
                    menu.Add(new TextMenu.SubHeader(Dialog.Clean("miaonet_oobe_livemode_desc"), false));
                    menu.Add(new TextMenu.SubHeader(""));

#if USE_CELEMIAO_AUTH || DEBUG
                    var liveMode = new TextMenu.OnOff(
                        Dialog.Get("miaonet_options_live_mode"), settings.LiveMode
                    ).Change(v => settings.LiveMode = v);
                    menu.Add(liveMode);
                    liveMode.AddDescription(menu, Dialog.Clean("miaonet_options_live_mode_tip"));
#endif
                }
            ),

            new OobePage("miaonet_oobe_interface_header",
                CreateItems: (menu, _) => {
                    menu.Add(new TextMenu.SubHeader(Dialog.Clean("miaonet_oobe_interface_desc"), false));
                    menu.Add(new TextMenu.SubHeader(""));

                    menu.Add(new TextMenuExt.IntSlider(
                        Dialog.Get("miaonet_options_player_list_ui_scale"), 1, 20, settings.PlayerListUIScale
                    ).Change(v => settings.PlayerListUIScale = v));
                    menu.Add(new TextMenuExt.IntSlider(
                        Dialog.Get("miaonet_options_chat_ui_scale"), 1, 20, settings.ChatUIScale
                    ).Change(v => settings.ChatUIScale = v));
                    menu.Add(new TextMenuExt.IntSlider(
                        Dialog.Get("miaonet_options_chat_message_padding"), 0, 8, settings.ChatMessagePadding
                    ).Change(v => settings.ChatMessagePadding = v));
                }
            ),

            new OobePage("miaonet_oobe_finish_header",
                CreateItems: (menu, _) => menu.Add(
                    new TextMenu.SubHeader(Dialog.Clean("miaonet_oobe_finish_desc"), false)
                )
            )
        ];
    }
}
