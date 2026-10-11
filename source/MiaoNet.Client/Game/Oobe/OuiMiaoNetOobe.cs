using System.Collections;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

// messy but works
public sealed partial class OuiMiaoNetOobe : Oui
{
    private sealed record OobePage(
        string HeaderKey,
        Func<List<PanelBlock>>? CreatePanel = null,
        Action<TextMenu, int>? CreateItems = null,
        bool AddNavigation = true
    );

    private const float TransitionDuration = 1f / 4f;

    private readonly OobePage[] pages;

    private float fade;
    private TextMenu? menu;
    private int step;
    private bool fromModOptions;
    private bool previousShowInputUI;

    private readonly PanelLayer panelLayer = new();
    private readonly PanelLayer outGoingPanelLayer = new();

    private readonly Dictionary<string, MTexture> images = [];
    private readonly List<VirtualTexture> loadedImages = [];

    private int LastStep => pages.Length - 1;

    public OuiMiaoNetOobe()
        => pages = CreatePages();

    public override IEnumerator Enter(Oui from)
    {
        Overworld.Maddy.Hide();
        previousShowInputUI = Overworld.ShowInputUI;
        Overworld.ShowInputUI = true;
        fromModOptions = from is OuiModOptions;

        LoadImages();

        Visible = true;
        fade = 0f;
        step = 0;
        panelLayer.Panel = BuildPanel(step);
        panelLayer.Fade(0f, 0f);
        outGoingPanelLayer.Reset();

        float homeX = MenuHomeX(step);

        menu = CreateMenu(step);
        menu.Focused = false;
        menu.Alpha = 0f;
        menu.X = OffScreenRightX;
        Scene.Add(menu);

        yield return Slide(menu, OffScreenRightX, homeX, 0f, 1f, Ease.CubeOut, t =>
        {
            fade = t;
            panelLayer.Fade(t, menu.X - homeX);
        });
        menu.Focused = true;
    }

    public override IEnumerator Leave(Oui next)
    {
        if (menu is not null)
        {
            float homeX = MenuHomeX(step);
            menu.Focused = false;
            yield return Slide(menu, menu.X, OffScreenLeftX, menu.Alpha, 0f, Ease.CubeIn, t =>
            {
                fade = 1f - t;
                panelLayer.Fade(1f - t, menu.X - homeX);
            });
            Scene.Remove(menu);
            menu = null;
        }

        fade = 0f;
        Visible = false;
        panelLayer.Reset();
        outGoingPanelLayer.Reset();
        Overworld.ShowInputUI = previousShowInputUI;
        UnloadImages();
        MiaoNetModule.Instance.SaveSettings();
    }

    public override void Update()
    {
        if (menu is not null && menu.Focused && Selected && Input.MenuCancel.Pressed)
        {
            if (step > 0)
                StartGotoTransition(step - 1, back: true);
            else
                Exit();
        }

        base.Update();
    }

    public override void Render()
    {
        const float p = 10f;
        Draw.Rect(-p, -p, Celeste.TargetWidth + p * 2f, Celeste.TargetHeight + p * 2f, Color.Black * fade);

        outGoingPanelLayer.Draw();
        panelLayer.Draw();

        base.Render();
    }

    #region Navigation

    private void StartGotoTransition(int target, bool back = false)
    {
        Add(new Coroutine(GotoTransitionRoutine(target, back)));

        IEnumerator GotoTransitionRoutine(int target, bool back)
        {
            var old = menu!;
            var next = CreateMenu(target);

            var oldFromX = old.X;
            var oldToX = back ? OffScreenRightX : OffScreenLeftX;
            var nextFromX = back ? OffScreenLeftX : OffScreenRightX;

            float oldHomeX = MenuHomeX(step);
            float nextHomeX = MenuHomeX(target);

            old.Focused = false;
            next.Focused = false;
            next.Alpha = 0f;
            next.X = nextFromX;
            Scene.Add(next);

            outGoingPanelLayer.Panel = panelLayer.Panel;
            outGoingPanelLayer.Fade(1f, 0f);
            panelLayer.Panel = BuildPanel(target);
            panelLayer.Fade(0f, nextFromX - nextHomeX);

            Audio.Play(back ? SFX.ui_main_whoosh_list_out : SFX.ui_main_whoosh_list_in);

            for (float p = 0f; p < 1f; p += Engine.DeltaTime / TransitionDuration)
            {
                var t = Ease.CubeInOut(p);
                old.X = MathHelper.Lerp(oldFromX, oldToX, t);
                old.Alpha = 1f - t;
                next.X = MathHelper.Lerp(nextFromX, nextHomeX, t);
                next.Alpha = t;
                outGoingPanelLayer.Fade(1f - t, old.X - oldHomeX);
                panelLayer.Fade(t, next.X - nextHomeX);
                yield return null;
            }

            old.RemoveSelf();
            next.X = nextHomeX;
            next.Alpha = 1f;
            menu = next;
            step = target;
            next.Focused = true;
            outGoingPanelLayer.Reset();
            panelLayer.Fade(1f, 0f);
        }

    }

    private void Exit()
    {
        if (fromModOptions)
            Overworld.Goto<OuiModOptions>();
        else
            Overworld.Goto<OuiMainMenu>();
    }

    private static IEnumerator Slide(
        TextMenu target,
        float fromX, float toX,
        float fromAlpha, float toAlpha,
        Ease.Easer easer,
        Action<float> update
    )
    {
        for (float p = 0f; p < 1f; p += Engine.DeltaTime / TransitionDuration)
        {
            var t = easer(p);
            target.X = MathHelper.Lerp(fromX, toX, t);
            target.Alpha = MathHelper.Lerp(fromAlpha, toAlpha, t);
            update(t);
            yield return null;
        }

        target.X = toX;
        target.Alpha = toAlpha;
        update(1f);
    }

    #endregion

    #region Menus

    private float MenuHomeX(int step)
        => pages[step].CreatePanel is null ? ScreenCenterX : PanelTextMenuX;

    private TextMenu CreateMenu(int step)
    {
        OobePage page = pages[step];

        bool hasPanel = page.CreatePanel is not null;

        var menu = new TextMenu();
        menu.BatchMode = true;

        if (hasPanel)
        {
            menu.Justify = new Vector2(0f, 0.5f);
            menu.MinWidth = PanelPageMenuMinWidth;
        }

        menu.Add(new TextMenu.Header(Dialog.Get(page.HeaderKey)));
        page.CreateItems?.Invoke(menu, step);

        if (page.AddNavigation)
        {
            menu.Add(new TextMenu.SubHeader(""));

            if (step == LastStep)
            {
                menu.Add(new TextMenu.Button(Dialog.Get("miaonet_oobe_prev")).Pressed(
                    () => StartGotoTransition(step - 1, back: true)
                ));
                menu.Add(new TextMenu.Button(Dialog.Get("miaonet_oobe_finish")).Pressed(Exit));
            }
            else
            {
                if (step > 0)
                {
                    menu.Add(new TextMenu.Button(Dialog.Get("miaonet_oobe_prev")).Pressed(
                        () => StartGotoTransition(step - 1, back: true)
                    ));
                }

                menu.Add(new TextMenu.Button(Dialog.Get("miaonet_oobe_next")).Pressed(
                    () => StartGotoTransition(step + 1)
                ));
            }
        }

        menu.BatchMode = false;
        menu.Position = new Vector2(MenuHomeX(step), 540f);

        menu.FirstSelection();

        return menu;
    }

#if USE_CELEMIAO_AUTH
    private static void AddLoginItems(TextMenu menu)
    {
        var login = new TextMenu.Button(Dialog.Get("miaonet_options_login"))
            .Pressed(MenuMiaoNetOptions.StartLogin);
        menu.Add(login);
        login.AddDescription(menu, Dialog.Clean("miaonet_options_login_tip"));

        string? name = MiaoNetModule.Settings.LastName;
        if (name is not null)
        {
            string text = Dialog.Get("miaonet_options_last_logged_in_name") + name;
            TextMenu.Button loggedInAs = new TextMenu.Button(text);
            loggedInAs.Selectable = false;
            menu.Add(loggedInAs);
        }
    }
#else
    private static void AddLoginItems(TextMenu menu)
        => menu.Add(new TextMenu.SubHeader(Dialog.Clean("miaonet_oobe_login_local"), false));
#endif

    private static void AddConnectionItems(TextMenu menu, MiaoNetModuleSettings settings)
    {
        var context = MiaoNetModule.Instance.MiaoNetContext;

        menu.Add(new TextMenu.OnOff(
            Dialog.Get("miaonet_options_connected"), context.HasConnection
        ).Change(v =>
        {
            if (v)
                context.Connect();
            else
                context.Disconnect();
        }));

        menu.Add(new TextMenu.OnOff(
            Dialog.Get("miaonet_options_connect_on_game_start"), settings.ConnectOnGameStart
        ).Change(v => settings.ConnectOnGameStart = v));
    }

    private static TextMenu.Setting CreateDisplayOnlyBindingSetting(string label, ButtonBinding binding)
        => new(label, binding.Binding, false) { Selectable = false };

    private void LoadImages()
    {
        foreach (string path in OobeImages.All)
        {
            ModAsset? asset = Everest.Content.Get(path)
                ?? throw new FileNotFoundException($"Oobe image {path} not found.");
            var texture = VirtualContent.CreateTexture(asset);

            loadedImages.Add(texture);
            images.Add(path, new MTexture(texture));
        }
    }

    private void UnloadImages()
    {
        foreach (var texture in loadedImages)
            texture.Dispose();
        loadedImages.Clear();
        images.Clear();
    }

    #endregion
}
