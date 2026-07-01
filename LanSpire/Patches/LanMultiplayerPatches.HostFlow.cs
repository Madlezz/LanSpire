namespace LanSpire.Patches;

public static partial class LanMultiplayerPatches
{
    private static Task StartHostFromSubmenuAsync(GameMode gameMode, Control loadingOverlay, NSubmenuStack stack)
    {
        var platformType = PlatformType.None;
        var hostCapacity = GetHostCapacityForPlatform(platformType, GetConfiguredHostMaxPlayers());
        loadingOverlay.Visible = true;
        NetHostGameService? netService = null;
        var hostHandedOff = false;
        try
        {
            if (IsPortInUse(DefaultPort))
            {
                PatchHelper.Log($"Port {DefaultPort} already in use - cannot host.");
                AddModal(NErrorPopup.Create(Strings.T("端口被占用", "Port In Use"),
                    Strings.T($"端口 {DefaultPort} 已被其他程序占用。请关闭其他 STS2 实例或正在运行的局域网房间，然后重试。", $"Port {DefaultPort} is already in use. Close other STS2 instances or running LAN games, then try again."),
                    showReportBugButton: false));
                return Task.CompletedTask;
    }
            netService = new NetHostGameService();
            var error = netService.StartENetHost(DefaultPort, hostCapacity);
            if (!error.HasValue)
            {
                switch (gameMode)
                                {
                                    case GameMode.Standard:
                                        var character = stack.GetSubmenuType<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen>();
                                        character.InitializeMultiplayerAsHost(netService, hostCapacity);
                                        stack.Push(character);
                                        break;
                                    case GameMode.Daily:
                                        var daily = stack.GetSubmenuType<MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunScreen>();
                                        daily.InitializeMultiplayerAsHost(netService);
                                        stack.Push(daily);
                                        break;
                                    case GameMode.Custom:
                                        var custom = stack.GetSubmenuType<MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen>();
                                        custom.InitializeMultiplayerAsHost(netService, hostCapacity);
                                        stack.Push(custom);
                                        break;
                                    default:
                                        PatchHelper.Log($"LAN host: unknown GameMode {gameMode}, falling back to Custom run screen.");
                                        var fallback = stack.GetSubmenuType<MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen>();
                                        fallback.InitializeMultiplayerAsHost(netService, hostCapacity);
                                        stack.Push(fallback);
                                        break;
                                }
                                PatchHelper.Log($"LAN host: game mode {gameMode}, dispatching host info popup...");
                                ShowLanHostInfo(netService);
                hostHandedOff = true;
    }
            else
            {
                netService = null;
                AddModal(NErrorPopup.Create(error.Value));
    }
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"LAN host failed: {exception}");
            if (netService != null && !hostHandedOff)
            {
                try { netService.Disconnect(NetError.InternalError, true); }
                catch (Exception ex) { PatchHelper.Log($"Host cleanup after failure failed: {ex}"); }
    }
            AddModal(NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false)));
        }
        finally
        {
            loadingOverlay.Visible = false;
        }
        return Task.CompletedTask;
    }

    private static Task StartLoadedHostAsync(object multiplayerSubmenu, SerializableRun run)
    {
        var loadingOverlay = (Control?)AccessTools.Field(multiplayerSubmenu.GetType(), "_loadingOverlay")?.GetValue(multiplayerSubmenu);
        var stack = (NSubmenuStack?)AccessTools.Field(typeof(NSubmenu), "_stack")?.GetValue(multiplayerSubmenu);
        if (loadingOverlay == null || stack == null)
            throw new InvalidOperationException("Could not reflect multiplayer submenu loading overlay/stack.");

        var hostCapacity = GetHostCapacityForPlatform(PlatformType.None, Math.Max(GetConfiguredHostMaxPlayers(), run.Players.Count));
        loadingOverlay.Visible = true;
        NetHostGameService? netService = null;
        var hostHandedOff = false;
        try
        {
            if (IsPortInUse(DefaultPort))
            {
                PatchHelper.Log($"Port {DefaultPort} already in use - cannot host loaded run.");
                AddModal(NErrorPopup.Create(Strings.T("端口被占用", "Port In Use"),
                    Strings.T($"端口 {DefaultPort} 已被其他程序占用。", $"Port {DefaultPort} is already in use."),
                    showReportBugButton: false));
                return Task.CompletedTask;
    }
            netService = new NetHostGameService();
            var error = netService.StartENetHost(DefaultPort, hostCapacity);
            if (!error.HasValue)
            {
                switch (GetRunGameMode(run))
                {
                    case GameMode.Daily:
                        var daily = stack.GetSubmenuType<MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunLoadScreen>();
                        daily.InitializeAsHost(netService, run);
                        stack.Push(daily);
                        break;
                    case GameMode.Custom:
                        var custom = stack.GetSubmenuType<MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunLoadScreen>();
                        custom.InitializeAsHost(netService, run);
                        stack.Push(custom);
                        break;
                    default:
                        var standard = stack.GetSubmenuType<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NMultiplayerLoadGameScreen>();
                        standard.InitializeAsHost(netService, run);
                        stack.Push(standard);
                        break;
                }
                ShowLanHostInfo(netService);
                hostHandedOff = true;
    }
            else
            {
                netService = null;
                AddModal(NErrorPopup.Create(error.Value));
    }
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"LAN loaded host failed: {exception}");
            if (netService != null && !hostHandedOff)
            {
                try { netService.Disconnect(NetError.InternalError, true); }
                catch (Exception ex) { PatchHelper.Log($"Host cleanup after failure failed: {ex}"); }
    }
            AddModal(NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false)));
        }
        finally
        {
            loadingOverlay.Visible = false;
        }
        return Task.CompletedTask;
    }

    private static GameMode GetRunGameMode(SerializableRun run)
    {
        var property = run.GetType().GetProperty("GameMode", BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(run) is GameMode gameMode)
            return gameMode;
        return GameMode.Standard;
    }


    private static Godot.Control? _hostIpButton;
        private static Node? _currentHostPopup;

    /// <summary>
        /// Creates a circular clickable element in the bottom-right corner
        /// with three horizontal lines (hamburger icon). Built from raw
        /// Controls (not a Button) because Godot's flat Button ignores
        /// custom StyleBoxFlat backgrounds. The circle background is a
        /// ColorRect with corner radius, always visible. Brightens on
        /// hover. Clicking reopens the host IP popup. Persists while host
        /// is active.
        /// </summary>
        private static void StartHostIpButton()
        {
            StopHostIpButton();
            try
            {
                var tree = Engine.GetMainLoop() as SceneTree;
                if (tree?.Root == null) return;

                const float btnSize = 56f;
                const float lineW = 24f;
                const float lineH = 3.5f;
                const float lineGap = 6f;
                var corner = (int)(btnSize / 2f);

                // Root Control: click detector + position anchor.
                // TopLevel = screen-space coords, ignores parent transforms.
                var root = new Control
                {
                    Name = "LanHostIpButton",
                    TopLevel = true,
                    MouseFilter = Control.MouseFilterEnum.Stop,
                    ZIndex = 9000,
                };
                // Bottom-right corner, 20px margin from edges.
                var vpSize = tree.Root.Size;
                root.Position = new Vector2(vpSize.X - btnSize - 20, vpSize.Y - btnSize - 20);
                root.Size = new Vector2(btnSize, btnSize);

                // Circle background - Panel with StyleBoxFlat (which has
                // corner radius). ColorRect does not support rounded corners.
                // Alpha 0.35 so it is clearly visible against any backdrop.
                var bgStyle = new StyleBoxFlat
                {
                    BgColor = new Color(0.25f, 0.25f, 0.25f, 0.35f),
                    CornerRadiusTopLeft = corner,
                    CornerRadiusTopRight = corner,
                    CornerRadiusBottomLeft = corner,
                    CornerRadiusBottomRight = corner,
                };
                var bg = new Panel
                {
                    Name = "CircleBg",
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                bg.AddThemeStyleboxOverride("panel", bgStyle);
                root.AddChild(bg);

                // Three horizontal lines (hamburger icon) as ColorRects.
                var totalH = 3 * lineH + 2 * lineGap;
                var startY = (btnSize - totalH) / 2f;
                var startX = (btnSize - lineW) / 2f;
                for (var i = 0; i < 3; i++)
                {
                    var line = new ColorRect
                    {
                        Color = new Color(0.937f, 0.784f, 0.318f, 0.9f),
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                    };
                    line.AnchorLeft = 0f; line.AnchorTop = 0f;
                    line.AnchorRight = 0f; line.AnchorBottom = 0f;
                    line.OffsetLeft = startX;
                    line.OffsetTop = startY + i * (lineH + lineGap);
                    line.OffsetRight = startX + lineW;
                    line.OffsetBottom = startY + i * (lineH + lineGap) + lineH;
                    root.AddChild(line);
                }

                // Hover: brighten background on mouse enter/exit.
                root.MouseEntered += () =>
                {
                    bgStyle.BgColor = new Color(0.4f, 0.4f, 0.4f, 0.55f);
                };
                root.MouseExited += () =>
                {
                    bgStyle.BgColor = new Color(0.25f, 0.25f, 0.25f, 0.35f);
                };

                // Click: reopen host IP popup.
                root.GuiInput += (evt) =>
                {
                    if (evt is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    {
                        if (_currentHostPopup != null && Godot.GodotObject.IsInstanceValid(_currentHostPopup))
                        {
                            _currentHostPopup.QueueFree();
                            _currentHostPopup = null;
                        }
                        var netAddresses = GetNetworkAddresses();
                        var popup = CreateHostIpPopup(DefaultPort, netAddresses);
                        var canvas = new CanvasLayer
                        {
                            Name = "LanHostIpPopupCanvas",
                            Layer = 128,
                        };
                        canvas.AddChild(popup);
                        var t = Engine.GetMainLoop() as SceneTree;
                        t?.Root?.AddChild(canvas);
                        _currentHostPopup = canvas;
                        root.AcceptEvent();
                    }
                };

                // Wrap in CanvasLayer for extra z-order insurance.
                var btnCanvas = new CanvasLayer
                {
                    Name = "LanHostIpButtonCanvas",
                    Layer = 128,
                };
                btnCanvas.AddChild(root);
                tree.Root.AddChild(btnCanvas);
                // Store the root Control so StopHostIpButton can find it.
                _hostIpButton = root;
    }
            catch (Exception ex)
            {
                PatchHelper.Log($"Host IP button start failed: {ex}");
    }
        }

    private static void StopHostIpButton()
        {
            var btn = _hostIpButton;
            _hostIpButton = null;
            if (btn != null && Godot.GodotObject.IsInstanceValid(btn))
            {
                // Free the button's CanvasLayer wrapper (parent) as well.
                try { btn.GetParent()?.QueueFree(); } catch { }
    }
            var popup = _currentHostPopup;
            _currentHostPopup = null;
            if (popup != null && Godot.GodotObject.IsInstanceValid(popup))
            {
                try { popup.QueueFree(); } catch { }
    }
        }

    private static void ShowLanHostInfo(NetHostGameService netService)
    {
        ClearPlayerNameOverrides(PlatformType.None);
        RegisterNameSyncHandler(netService);
        StartDiscoveryResponder(DefaultPort);
        _hostService = netService;
        StartPlayerCountPoller();
        StartPingPoller();
        // Use a scene-tree timer with a generous delay so the popup
        // and button appear AFTER the game screen has fully loaded.
        // Character select / daily / custom screens take time to
        // build their CanvasLayers; a shorter delay results in our
        // popup being covered by the game's own UI layers.
        var tree = Engine.GetMainLoop() as SceneTree;
        tree?.CreateTimer(1.5).Timeout += () =>
        {
            PatchHelper.Log("LAN host info timer fired - showing popup and button.");
            var netAddresses = GetNetworkAddresses();
            StartHostIpButton();
            ShowHostIpPopup(DefaultPort, netAddresses);
        };
    }

    private static void ShowHostIpPopup(ushort port, IReadOnlyList<NetworkHelpers.NetAddress> addresses)
        {
            try
            {
                // Remove any existing popup canvas first
                if (_currentHostPopup != null && Godot.GodotObject.IsInstanceValid(_currentHostPopup))
                {
                    _currentHostPopup.QueueFree();
                    _currentHostPopup = null;
                }
                var tree = Engine.GetMainLoop() as SceneTree;
                if (tree?.Root != null)
                {
                    var popup = CreateHostIpPopup(port, addresses);
                    // Wrap in a CanvasLayer so the popup renders above all
                    // game UI regardless of the scene tree's z-order. The
                    // game uses CanvasLayers for its own UI; a plain
                    // tree.Root.AddChild would render behind them.
                    var canvas = new CanvasLayer
                    {
                        Name = "LanHostIpPopupCanvas",
                        Layer = 128,
                    };
                    canvas.AddChild(popup);
                    tree.Root.AddChild(canvas);
                    _currentHostPopup = canvas;
                }
                else
                    PatchHelper.Log("Cannot show host IP popup - scene tree root not available.");
    }
            catch (Exception ex)
            {
                PatchHelper.Log($"Failed to show host IP popup: {ex}");
    }
        }

    /// <summary>
    /// Builds a self-contained modal popup with the host info text and
    /// a clickable copy button for each IP address. The popup is a
    /// ColorRect overlay (dark bg) with a centered VBoxContainer inside.
    /// No dependency on NErrorPopup -- fully self-contained.
    /// </summary>
    private static Control CreateHostIpPopup(ushort port, IReadOnlyList<NetworkHelpers.NetAddress> addresses)
    {
        var overlay = new Control
        {
            Name = "LanHostIpPopup",
            MouseFilter = Control.MouseFilterEnum.Stop,
            AnchorLeft = 0, AnchorTop = 0,
            AnchorRight = 1, AnchorBottom = 1,
            ZIndex = 9000,
        };

        var bg = new ColorRect
        {
            Name = "PopupBg",
            Color = new Color(0.04f, 0.04f, 0.04f, 0.85f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        bg.AnchorLeft = 0; bg.AnchorTop = 0;
        bg.AnchorRight = 1; bg.AnchorBottom = 1;
        overlay.AddChild(bg);

        var panel = new PanelContainer
        {
            Name = "PopupPanel",
            AnchorLeft = 0.5f, AnchorTop = 0.5f,
            AnchorRight = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -320, OffsetTop = -260,
            OffsetRight = 320, OffsetBottom = 260,
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.11f, 0.085f, 0.06f, 0.97f),
            BorderColor = new Color(0.45f, 0.37f, 0.23f),
            BorderWidthLeft = 2, BorderWidthRight = 2,
            BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        };
        panelStyle.SetContentMarginAll(20);
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        overlay.AddChild(panel);

        var vbox = new VBoxContainer
        {
            Name = "PopupContent",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        var title = new Label
        {
            Text = Strings.T("局域网房间已开启", "LAN Host Ready"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color(0.937f, 0.784f, 0.318f));
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        title.AddThemeConstantOverride("outline_size", 4);
        vbox.AddChild(title);

        if (addresses.Count == 0)
        {
            var noIp = new Label
            {
                Text = Strings.T(
                    $"未能自动识别本机 IP，请在系统网络设置查看后用 <IP>:{port} 加入。",
                    $"Couldn't detect this PC's LAN IP. Check your network settings, then join with <IP>:{port}."),
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            noIp.AddThemeFontSizeOverride("font_size", 16);
            noIp.AddThemeColorOverride("font_color", Colors.White);
            vbox.AddChild(noIp);
        }
        else
        {
            var hint = new Label
            {
                Text = Strings.T("点击下方任一 IP 复制到剪贴板：", "Click any IP below to copy to clipboard:"),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            hint.AddThemeFontSizeOverride("font_size", 14);
            hint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            vbox.AddChild(hint);

            foreach (var addr in addresses)
            {
                var label = addr.IsVpn ? $" ({addr.Label})" : "";
                var ipText = $"{addr.Ip}:{port}{label}";
                var ipOnly = addr.Ip;
                var copyBtn = new Button
                {
                    Text = ipText,
                    CustomMinimumSize = new Vector2(0, 40),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                };
                copyBtn.AddThemeFontSizeOverride("font_size", 16);
                StyleLanButton(copyBtn);
                copyBtn.Connect(Button.SignalName.Pressed, Callable.From(() =>
                {
                    try { DisplayServer.ClipboardSet(ipOnly); } catch { }
                    ShowToast(Strings.T($"已复制 {ipOnly}", $"Copied {ipOnly}"));
                }));
                vbox.AddChild(copyBtn);
    }

            var discoveryHint = new Label
            {
                Text = _discoveryBindOk
                    ? Strings.T("好友也可点 \"扫描局域网\" 自动找到你。",
                        "Friends can also click \"Scan LAN\" to find you automatically.")
                    : Strings.T("自动发现不可用，好友需手动输入此 IP。",
                        "Auto-discovery is off. Friends must enter this IP manually."),
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            discoveryHint.AddThemeFontSizeOverride("font_size", 13);
            discoveryHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            vbox.AddChild(discoveryHint);
        }

        var closeBtn = new Button
        {
            Text = Strings.T("关闭", "Close"),
            CustomMinimumSize = new Vector2(120, 44),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        closeBtn.AddThemeFontSizeOverride("font_size", 16);
        StyleLanButton(closeBtn, accent: true);
        closeBtn.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            try { overlay.QueueFree(); } catch { }
        }));
        vbox.AddChild(closeBtn);

        return overlay;
    }

    private static void ShowToast(string text, float duration = 2.5f, Color? fontColor = null)
            {
        ShowToastInternal(text, duration, fontColor);
    }

    /// <summary>Public toast for use by UpdateChecker and other services.</summary>
    internal static void ShowUpdateToast(string text)
    {
        ShowToastInternal(text, 6.0f, new Color(0.4f, 0.9f, 0.5f));
    }

    private static void ShowToastInternal(string text, float duration, Color? fontColor)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null) return;

            foreach (var child in tree.Root.GetChildren())
            {
                if (child is PanelContainer p && p.Name == "LanToast")
                {
                    p.QueueFree();
                }
    }

            var panel = new PanelContainer { Name = "LanToast" };
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.08f, 0.08f, 0.08f, 0.92f);
            style.SetContentMarginAll(14);
            style.SetCornerRadiusAll(8);
            panel.AddThemeStyleboxOverride("panel", style);

            var label = new Label { Text = text };
            label.AddThemeFontSizeOverride("font_size", 18);
            label.AddThemeColorOverride("font_color", fontColor ?? new Color(0.937f, 0.784f, 0.318f));
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 4);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            panel.AddChild(label);

            panel.AnchorLeft = 0.5f; panel.AnchorRight = 0.5f;
            panel.AnchorTop = 0f; panel.AnchorBottom = 0f;
            panel.OffsetLeft = -300; panel.OffsetRight = 300;
            panel.OffsetTop = 60; panel.OffsetBottom = 110;
            panel.ZIndex = 1000;

            tree.Root.AddChild(panel);

            var tween = panel.CreateTween();
            tween.TweenInterval(duration);
            tween.TweenProperty(panel, "modulate:a", 0f, 0.5f);
            tween.TweenCallback(Callable.From(() => { try { panel.QueueFree(); } catch { } }));
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Toast failed: {ex}");
        }
    }

    /// <summary>
    /// Central error reporter. Logs the full exception (stack trace, so the PDB
    /// resolves file and line numbers) and surfaces a red in-game toast telling
    /// the player to open the game log and share it. Logging always runs even if
    /// the toast cannot be shown.
    /// </summary>
    internal static void ReportError(string context, Exception exception)
    {
        try
        {
            PatchHelper.Log($"ERROR in {context}: {exception}");
        }
        catch { }

        try
        {
            ShowToast(
                Strings.T(
                    $"局域网联机出错: {context}. 打开设置里的查看日志并分享给作者。",
                    $"LanSpire error: {context}. Open Settings then Open Game Log to share."),
                6.0f,
                new Color(1f, 0.45f, 0.4f));
        }
        catch { }
    }


    private static void ShowInfoPopup(string title, string body)
    {
        AddModal(NErrorPopup.Create(title, body, showReportBugButton: false));
    }

    private static void AddModal(Node? popup)
    {
        if (popup == null) return;
        try
        {
            var container = NModalContainer.Instance;
            if (container != null && Godot.GodotObject.IsInstanceValid(container))
                container.Add(popup);
            else
                PatchHelper.Log("Cannot show modal - NModalContainer instance not available.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"AddModal failed: {ex}");
        }
    }
}