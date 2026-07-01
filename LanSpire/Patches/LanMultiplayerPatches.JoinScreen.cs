namespace LanSpire.Patches;

public static partial class LanMultiplayerPatches
{

    /// <summary>
    /// Removes all LAN-injected UI from the join screen, restoring it to
    /// the original Steam friend-list layout. Called when the user opens
    /// the original "Join" button after previously using "Join LAN".
    /// </summary>
    private static void CleanupLanJoinScreen(NJoinFriendScreen screen)
    {
        try
        {
            var title = screen.GetNodeOrNull<Label>("TitleLabel");
            if (title != null && screen.HasMeta("lan_original_title"))
            {
                title.Text = (string)screen.GetMeta("lan_original_title");
                screen.RemoveMeta("lan_original_title");
            }

            var buttonContainer = screen.GetNodeOrNull<Control>("%ButtonContainer");
            if (buttonContainer != null) buttonContainer.Visible = true;
            var loading = screen.GetNodeOrNull<Control>("%LoadingIndicator");
            if (loading != null) loading.Visible = true;
            var noFriends = screen.GetNodeOrNull<Control>("%NoFriendsText");
            if (noFriends != null) noFriends.Visible = true;

            var panel = screen.GetNodeOrNull<Control>("Panel");
            if (panel != null)
            {
                var lanContainer = panel.GetNodeOrNull<Control>("LanJoinContainer");
                if (lanContainer != null)
                {
                    lanContainer.Visible = false;
                    lanContainer.QueueFree();
                }
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Cleanup LAN join screen failed: {ex}");
        }
    }

    private static void ConfigureLanJoinScreen(NJoinFriendScreen screen)
    {
        var title = screen.GetNodeOrNull<Label>("TitleLabel");
        if (title != null)
        {
            if (!screen.HasMeta("lan_original_title"))
                screen.SetMeta("lan_original_title", title.Text);
            title.Text = Strings.T("加入局域网游戏", "Join LAN Game");
        }

        var buttonContainer = screen.GetNodeOrNull<Control>("%ButtonContainer");
        if (buttonContainer != null) buttonContainer.Visible = false;
        var loading = screen.GetNodeOrNull<Control>("%LoadingIndicator");
        if (loading != null) loading.Visible = false;
        var noFriends = screen.GetNodeOrNull<Control>("%NoFriendsText");
        if (noFriends != null) noFriends.Visible = false;

        var panel = screen.GetNode<Control>("Panel");
        var container = panel.GetNodeOrNull<VBoxContainer>("LanJoinContainer") ?? CreateLanJoinContainer(panel);
        container.Visible = true;

        var hostInput = container.GetNode<LineEdit>("LanHostRow/LanHostInput");
        var portInput = container.GetNode<LineEdit>("LanPortRow/LanPortInput");
        hostInput.PlaceholderText = "192.168.0.123";
        portInput.PlaceholderText = DefaultPort.ToString();

        // Only set saved values if the inputs are empty (first open).
        // On reconfigure (after delete/pin/unpin), preserve what the user
        // has already typed so we don't wipe their in-progress input.
        if (string.IsNullOrWhiteSpace(hostInput.Text))
            hostInput.Text = GetSavedJoinHost();
        if (string.IsNullOrWhiteSpace(portInput.Text))
            portInput.Text = GetSavedJoinPort().ToString();

        // Rebuild history row: RemoveChild before QueueFree to avoid
        // duplicate-name-in-tree errors during the frame gap.
        var historyContainer = container.GetNodeOrNull<HBoxContainer>("LanHistoryRow");
        if (historyContainer != null)
        {
            container.RemoveChild(historyContainer);
            historyContainer.QueueFree();
        }
        historyContainer = CreateHistoryButtons(screen, hostInput, portInput);
        container.AddChild(historyContainer);

        // Rebuild pinned row: same RemoveChild-then-QueueFree pattern.
        var pinnedContainer = container.GetNodeOrNull<HBoxContainer>("LanPinnedRow");
        if (pinnedContainer != null)
        {
            container.RemoveChild(pinnedContainer);
            pinnedContainer.QueueFree();
        }
        pinnedContainer = CreatePinnedHostRow(screen, hostInput, portInput);
        container.AddChild(pinnedContainer);

        var scanScroll = container.GetNodeOrNull<ScrollContainer>("LanScanResults") ?? CreateScanResultsContainer();
        if (scanScroll.GetParent() == null) container.AddChild(scanScroll);
        scanScroll.Visible = false;

        var loadingOverlay = screen.GetNodeOrNull<Control>("LanLoadingOverlay");
        if (loadingOverlay == null)
        {
            loadingOverlay = CreateLoadingOverlay();
            screen.AddChild(loadingOverlay);
        }
        loadingOverlay.Visible = false;

        var scanBtn = container.GetNodeOrNull<Button>("LanScanRow/LanScanBtn");
        if (scanBtn != null && !scanBtn.HasMeta("sts2_lan_scan_wired"))
        {
            scanBtn.SetMeta("sts2_lan_scan_wired", true);
            scanBtn.Connect(Button.SignalName.Pressed, Callable.From(() =>
                TaskHelper.RunSafely(ScanLanAsync(screen, scanBtn, scanScroll, hostInput, portInput))));
        }

        // Auto-scan removed: user must click "Scan LAN" manually.
        // Scanning on every screen open is unnecessary and distracting.

        var refreshButton = screen.GetNodeOrNull<Control>("RefreshButton");
        if (refreshButton != null)
        {
            var label = refreshButton.GetNodeOrNull<Label>("Label");
            if (label != null) label.Text = Strings.T("加入", "Join");
            var icon = refreshButton.GetNodeOrNull<Control>("ControllerIcon");
            if (icon != null) icon.Visible = false;
        }

        ReplaceLineSubmitted(hostInput, _ => TaskHelper.RunSafely(JoinLanGameAsync(screen, hostInput, portInput)));
        ReplaceLineSubmitted(portInput, _ => TaskHelper.RunSafely(JoinLanGameAsync(screen, hostInput, portInput)));

        if (!portInput.HasMeta("sts2_lan_numeric_filter"))
        {
            portInput.SetMeta("sts2_lan_numeric_filter", true);
            portInput.Connect(LineEdit.SignalName.TextChanged, Callable.From<string>(text =>
            {
                var filtered = new string(text.Where(char.IsDigit).ToArray());
                if (filtered != text)
                {
                    var caret = portInput.CaretColumn;
                    var removedBeforeCaret = 0;
                    for (var i = 0; i < caret && i < text.Length; i++)
                        if (!char.IsDigit(text[i]))
                            removedBeforeCaret++;
                    portInput.Text = filtered;
                    portInput.CaretColumn = Math.Max(0, caret - removedBeforeCaret);
                }
            }));
        }

        if (!portInput.HasMeta("sts2_lan_port_clamp"))
        {
            portInput.SetMeta("sts2_lan_port_clamp", true);
            portInput.Connect(LineEdit.SignalName.FocusExited, Callable.From(() =>
            {
                if (ushort.TryParse(portInput.Text, out var p) && p is > 0 and <= 65535)
                    return;
                if (string.IsNullOrWhiteSpace(portInput.Text))
                    return;
                portInput.Text = GetSavedJoinPort().ToString();
            }));
        }

        hostInput.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static HBoxContainer CreatePinnedHostRow(NJoinFriendScreen screen, LineEdit hostInput, LineEdit portInput)
    {
        var row = new HBoxContainer
        {
            Name = "LanPinnedRow",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 8);

        var pinnedHost = LanConfig.GetString("lan_pinned_host", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(pinnedHost))
        {
            // No pinned host -- show pin current button
            var pinBtn = new Button
            {
                Text = Strings.T("固定当前地址", "Pin Current"),
                CustomMinimumSize = new Vector2(0, 36),
            };
            pinBtn.AddThemeFontSizeOverride("font_size", 14);
            StyleLanButton(pinBtn);
            pinBtn.Connect(Button.SignalName.Pressed, Callable.From(() =>
            {
                var host = hostInput.Text.Trim();
                var port = portInput.Text.Trim();
                if (string.IsNullOrWhiteSpace(host))
                {
                    ShowToast(Strings.T("请先输入 IP 地址", "Enter an IP address first"));
                    return;
                }
                var entry = string.IsNullOrWhiteSpace(port) ? host : $"{host}:{port}";
                LanConfig.SetValue("lan_pinned_host", entry);
                PatchHelper.Log($"Pinned host: {entry}");
                ShowToast(Strings.T($"已固定 {entry}", $"Pinned {entry}"));
                ConfigureLanJoinScreen(screen);
            }));
            row.AddChild(pinBtn);
            return row;
        }

        // Show pinned host with unpin button
        var pinLabel = MakeLabel("LanPinnedLabel", Strings.T("固定：", "Pinned:"), 16, new Color(0.4f, 0.9f, 0.5f));
        pinLabel.CustomMinimumSize = new Vector2(80, 0);
        pinLabel.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(pinLabel);

        var pinnedBtn = new Button
        {
            Text = pinnedHost,
            CustomMinimumSize = new Vector2(0, 40),
        };
        pinnedBtn.AddThemeFontSizeOverride("font_size", 16);
        StyleLanButton(pinnedBtn);
        pinnedBtn.Modulate = new Color(0.6f, 1f, 0.6f);
        var capturedPinned = pinnedHost;
        pinnedBtn.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            var colon = capturedPinned.LastIndexOf(':');
            if (colon > 0)
            {
                hostInput.Text = capturedPinned[..colon];
                portInput.Text = capturedPinned[(colon + 1)..];
            }
            else
            {
                hostInput.Text = capturedPinned;
            }
            TaskHelper.RunSafely(JoinLanGameAsync(screen, hostInput, portInput));
        }));
        row.AddChild(pinnedBtn);

        var unpinBtn = new Button
        {
            Text = Strings.T("取消固定", "Unpin"),
            CustomMinimumSize = new Vector2(0, 36),
        };
        unpinBtn.AddThemeFontSizeOverride("font_size", 14);
        StyleLanButton(unpinBtn);
        unpinBtn.AddThemeColorOverride("font_color", new Color(0.92f, 0.55f, 0.5f));
        unpinBtn.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            LanConfig.SetValue("lan_pinned_host", "");
            PatchHelper.Log("Unpinned host.");
            ConfigureLanJoinScreen(screen);
        }));
        row.AddChild(unpinBtn);

        return row;
    }

    private static HBoxContainer CreateHistoryButtons(NJoinFriendScreen screen, LineEdit hostInput, LineEdit portInput)
    {
        var row = new HBoxContainer
        {
            Name = "LanHistoryRow",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 8);

        var history = GetJoinHistory();
        if (history.Count == 0)
        {
            var emptyLabel = MakeLabel("LanHistoryEmpty",
                Strings.T("暂无连接记录", "No recent connections"),
                14, new Color(0.5f, 0.5f, 0.5f));
            emptyLabel.CustomMinimumSize = new Vector2(0, 36);
            emptyLabel.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(emptyLabel);
            return row;
        }

        var label = MakeLabel("LanHistoryLabel", Strings.T("最近连接：", "Recent:"), 16, new Color(0.6f, 0.6f, 0.6f));
        label.CustomMinimumSize = new Vector2(80, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);

        foreach (var entry in history)
        {
            var entryRow = new HBoxContainer();
            entryRow.AddThemeConstantOverride("separation", 4);

            var btn = new Button
            {
                Text = entry,
                CustomMinimumSize = new Vector2(0, 40),
            };
            btn.AddThemeFontSizeOverride("font_size", 16);
            StyleLanButton(btn);
            var capturedEntry = entry;
            // Click fills the input fields but does NOT auto-join.
            // User can review/edit before pressing Join.
            btn.Connect(Button.SignalName.Pressed, Callable.From(() =>
            {
                var colon = capturedEntry.LastIndexOf(':');
                if (colon > 0)
                {
                    hostInput.Text = capturedEntry[..colon];
                    portInput.Text = capturedEntry[(colon + 1)..];
                }
                else
                {
                    hostInput.Text = capturedEntry;
                }
                hostInput.GrabFocus();
            }));
            entryRow.AddChild(btn);

            // Per-entry delete button (x)
            var delBtn = new Button
            {
                Text = "x",
                CustomMinimumSize = new Vector2(36, 40),
            };
            delBtn.AddThemeFontSizeOverride("font_size", 14);
            StyleLanButton(delBtn);
            delBtn.AddThemeColorOverride("font_color", new Color(0.92f, 0.55f, 0.5f));
            delBtn.Connect(Button.SignalName.Pressed, Callable.From(() =>
            {
                RemoveFromJoinHistory(capturedEntry);
                ConfigureLanJoinScreen(screen);
            }));
            entryRow.AddChild(delBtn);

            row.AddChild(entryRow);
        }

        var clearBtn = new Button
        {
            Text = Strings.T("清除全部", "Clear All"),
            CustomMinimumSize = new Vector2(0, 36),
        };
        clearBtn.AddThemeFontSizeOverride("font_size", 14);
        StyleLanButton(clearBtn);
        clearBtn.AddThemeColorOverride("font_color", new Color(0.92f, 0.55f, 0.5f));
        clearBtn.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            ClearJoinHistory();
            ConfigureLanJoinScreen(screen);
        }));
        row.AddChild(clearBtn);

        return row;
    }

    private static VBoxContainer CreateLanJoinContainer(Control panel)
    {
        var container = new VBoxContainer
        {
            Name = "LanJoinContainer",
            UniqueNameInOwner = true,
            AnchorLeft = 0.5f, AnchorTop = 0.5f, AnchorRight = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -332, OffsetTop = -200, OffsetRight = 332, OffsetBottom = 200,
            GrowHorizontal = Control.GrowDirection.Both, GrowVertical = Control.GrowDirection.Both,
        };
        container.AddThemeConstantOverride("separation", 14);

        var help = MakeLabel("LanHelpLabel", Strings.T("输入房主局域网 IP 和端口加入。", "Join the host by entering their LAN IP and port."), 22, Colors.White);
        help.CustomMinimumSize = new Vector2(0, 60);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(help);
        container.AddChild(MakeInputRow("LanHostRow", "LanHostLabel", Strings.T("房主 IP", "Host IP"), "LanHostInput", false));
        container.AddChild(MakeInputRow("LanPortRow", "LanPortLabel", Strings.T("端口", "Port"), "LanPortInput", true));
        container.AddChild(CreateScanRow());
        panel.AddChild(container);
        return container;
    }

    private static HBoxContainer MakeInputRow(string rowName, string labelName, string labelText, string inputName, bool numeric)
    {
        var row = new HBoxContainer { Name = rowName };
        row.AddThemeConstantOverride("separation", 18);
        var label = MakeLabel(labelName, labelText, 28, new Color(0.937255f, 0.784314f, 0.317647f));
        label.CustomMinimumSize = new Vector2(120, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);
        var input = new LineEdit
        {
            Name = inputName,
            UniqueNameInOwner = true,
            CustomMinimumSize = new Vector2(0, 48),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MaxLength = numeric ? 5 : 128,
        };
        input.AddThemeFontSizeOverride("font_size", 24);
        StyleLanInput(input);
        row.AddChild(input);
        return row;
    }

    private static HBoxContainer CreateScanRow()
    {
        var row = new HBoxContainer
        {
            Name = "LanScanRow",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 12);

        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(spacer);

        var scanBtn = new Button
        {
            Name = "LanScanBtn",
            Text = Strings.T("扫描局域网", "Scan LAN"),
            CustomMinimumSize = new Vector2(160, 48),
        };
        scanBtn.AddThemeFontSizeOverride("font_size", 18);
        StyleLanButton(scanBtn, accent: true);
        row.AddChild(scanBtn);

        return row;
    }

    private static ScrollContainer CreateScanResultsContainer()
    {
        var scroll = new ScrollContainer
        {
            Name = "LanScanResults",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 160),
            Visible = false,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var inner = new VBoxContainer
        {
            Name = "LanScanResultsInner",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        inner.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(inner);
        return scroll;
    }

    private static Control CreateLoadingOverlay()
    {
        var overlay = new Control
        {
            Name = "LanLoadingOverlay",
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
            // TopLevel makes the overlay use screen coordinates directly,
            // so it centers on the viewport regardless of parent transform.
            TopLevel = true,
            // High ZIndex so the overlay renders above modals and popups.
            ZIndex = 9001,
        };
        overlay.AnchorLeft = 0; overlay.AnchorTop = 0;
        overlay.AnchorRight = 1; overlay.AnchorBottom = 1;

        var bg = new ColorRect
        {
            Name = "OverlayBg",
            Color = new Color(0.04f, 0.04f, 0.04f, 0.78f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        bg.AnchorLeft = 0; bg.AnchorTop = 0;
        bg.AnchorRight = 1; bg.AnchorBottom = 1;
        overlay.AddChild(bg);

        // Center spinner + status vertically and horizontally.
        // Use anchors + offsets so the spinner is always at screen center.
        var spinnerLabel = new Label
        {
            Name = "SpinnerLabel",
            Text = "◌",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0.5f, AnchorTop = 0.5f,
            AnchorRight = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -30, OffsetTop = -80,
            OffsetRight = 30, OffsetBottom = -20,
        };
        spinnerLabel.AddThemeFontSizeOverride("font_size", 48);
        spinnerLabel.AddThemeColorOverride("font_color", new Color(0.937f, 0.784f, 0.318f));
        spinnerLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        spinnerLabel.AddThemeConstantOverride("outline_size", 4);
        // Set pivot immediately so rotation is centered (not delayed to Ready).
        spinnerLabel.PivotOffset = new Vector2(30, 30);
        overlay.AddChild(spinnerLabel);

        var statusLabel = new Label
        {
            Name = "StatusLabel",
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.5f, AnchorTop = 0.5f,
            AnchorRight = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -200, OffsetTop = 10,
            OffsetRight = 200, OffsetBottom = 50,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        statusLabel.AddThemeFontSizeOverride("font_size", 18);
        statusLabel.AddThemeColorOverride("font_color", Colors.White);
        statusLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        statusLabel.AddThemeConstantOverride("outline_size", 4);
        overlay.AddChild(statusLabel);

        var tween = spinnerLabel.CreateTween();
        tween.SetLoops();
        tween.TweenProperty(spinnerLabel, "rotation", Mathf.Pi, 0.4f);
        tween.TweenProperty(spinnerLabel, "rotation", Mathf.Tau, 0.4f);

        return overlay;
    }

    private static bool TryGetLanInputs(NJoinFriendScreen screen, out LineEdit? hostInput, out LineEdit? portInput)
    {
        hostInput = null;
        portInput = null;
        var container = screen.GetNodeOrNull<VBoxContainer>("Panel/LanJoinContainer");
        if (container?.Visible != true) return false;
        hostInput = container.GetNodeOrNull<LineEdit>("LanHostRow/LanHostInput");
        portInput = container.GetNodeOrNull<LineEdit>("LanPortRow/LanPortInput");
        return hostInput != null && portInput != null;
    }

    private static void ReplaceLineSubmitted(LineEdit input, Action<string> action)
    {
        try
        {
            if (input.HasMeta("sts2_lan_pc_submit_connected")) return;
            input.SetMeta("sts2_lan_pc_submit_connected", true);
            input.Connect(LineEdit.SignalName.TextSubmitted, Callable.From<string>(value => action(value)));
        }
        catch { }
    }

    private static bool IsTransientJoinError(Exception ex)
    {
        if (ex is System.Net.Sockets.SocketException) return true;
        if (ex is TimeoutException) return true;
        var msg = ex.Message ?? "";
        return msg.Contains("refused", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("unreachable", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detects NetId collision from the base game handshake. The base game's
    /// ENetHost sends IdCollision status and disconnects the peer; ENetClient
    /// returns NetError.Kicked. ClientConnectionFailedException carries the
    /// NetErrorInfo with that reason.
    /// </summary>
    private static bool IsNetIdCollision(Exception ex)
    {
        // Walk inner exceptions to find ClientConnectionFailedException
        var current = ex;
        while (current != null)
        {
            var type = current.GetType();
            if (type.Name == "ClientConnectionFailedException" || type.FullName == "MegaCrit.Sts2.Core.Multiplayer.Connection.ClientConnectionFailedException")
            {
                var infoField = AccessTools.Field(type, "info");
                if (infoField?.GetValue(current) is NetErrorInfo info)
                    return info.GetReason() == NetError.Kicked;
            }
            current = current.InnerException;
        }
        return false;
    }

    private static string TranslateJoinError(Exception exception)
    {
        var msg = exception.Message ?? "";
        if (IsNetIdCollision(exception))
            return Strings.T(
                "玩家 ID 冲突，已自动生成新 ID 并重试。如果仍然失败，请在设置中更改自定义玩家 ID。",
                "Player ID collision. A new ID has been generated automatically. If it still fails, set a custom player ID in Settings.");
        if (exception is System.Net.Sockets.SocketException)
            return Strings.T(
                "无法连接到主机。请确认房主已开始游戏，且 IP 地址和端口正确。\n如果使用 ZeroTier/Tailscale 等 VPN，请确认 VPN 网络已连接并使用房主的 VPN IP。",
                "Could not connect to host. Make sure the host has started a game and the IP/port are correct.\nIf using ZeroTier/Tailscale VPN, make sure the VPN network is connected and you're using the host's VPN IP.");
        if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return Strings.T(
                "连接超时。请确认房主在线且网络可达。\n如果是 VPN 连接，VPN 路由可能需要几秒钟建立，已自动重试一次。",
                "Connection timed out. Make sure the host is online and reachable.\nFor VPN connections, the route may take a few seconds to establish (auto-retried once).");
        if (msg.Contains("refused", StringComparison.OrdinalIgnoreCase))
            return Strings.T(
                "连接被拒绝。房主可能尚未开房，或端口被防火墙拦截。\n请确认房主已点击 \"多人游戏\" 并开始游戏。",
                "Connection refused. The host may not have started a game yet, or the port is blocked by a firewall.\nMake sure the host has clicked \"Multiplayer\" and started a game.");
        if (msg.Contains("unreachable", StringComparison.OrdinalIgnoreCase) || msg.Contains("nreachable", StringComparison.OrdinalIgnoreCase))
            return Strings.T(
                "网络不可达。请确认你和房主在同一局域网，或 IP 地址正确。\n如果是 VPN，请确认 VPN 已连接并使用正确的 VPN IP 地址。",
                "Network unreachable. Make sure you and the host are on the same LAN, or the IP is correct.\nFor VPN, make sure the VPN is connected and you're using the correct VPN IP address.");
        return msg;
    }

}