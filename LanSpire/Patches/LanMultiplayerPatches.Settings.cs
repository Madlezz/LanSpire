namespace LanSpire.Patches;

public static partial class LanMultiplayerPatches
{
    public static void SettingsScreenReadyPostfix(Node __instance)
    {
        try
        {
            DebugLog($"LAN settings: OnSubmenuShown on {__instance.GetType().Name}, {__instance.GetChildCount()} children.");

            Node? content = null;

            var generalSettings = __instance.GetNodeOrNull<Node>("%GeneralSettings");
            if (generalSettings != null)
            {
                DebugLog($"LAN settings: found %GeneralSettings ({generalSettings.GetType().Name}).");
                content = generalSettings.GetType().GetProperty("Content", BindingFlags.Public | BindingFlags.Instance)?.GetValue(generalSettings) as Node
                    ?? generalSettings.GetNodeOrNull<Node>("Content");
            }

            if (content == null)
            {
                DebugLog("LAN settings: %GeneralSettings/Content not found - searching tree for container...");
                content = FindSettingsContainer(__instance);
            }

            if (content == null)
            {
                PatchHelper.Log("LAN settings: no suitable container found - settings UI will NOT be added.");
                return;
            }

            DebugLog($"LAN settings: using container '{content.Name}' ({content.GetType().Name}).");

            var enabled = LanConfig.GetBool("max_multiplayer_enabled", true);
            var maxPlayersNode = content.GetNodeOrNull<Control>("MaxMultiplayerPlayers");
            var maxPlayersDivider = content.GetNodeOrNull<Control>("MaxMultiplayerPlayersDivider");
            if (maxPlayersNode != null) maxPlayersNode.Visible = enabled;
            if (maxPlayersDivider != null) maxPlayersDivider.Visible = enabled;

            if (content.GetNodeOrNull<Control>("LanSettingsSection") != null)
                return;

            var section = CreateLanSettingsSection();
            content.AddChild(section);
            PatchHelper.Log("LAN settings: section added successfully.");
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"LAN settings UI patch failed: {exception}");
        }
    }

    /// <summary>
    /// Recursively searches for a container suitable for adding settings rows.
    /// Looks for VBoxContainer or ScrollContainer with multiple children
    /// (indicating it holds settings items, not just layout scaffolding).
    /// </summary>
    private static Node? FindSettingsContainer(Node root, int depth = 0, int maxDepth = 4)
    {
        if (depth > maxDepth) return null;
        for (int i = 0; i < root.GetChildCount(); i++)
        {
            var child = root.GetChild(i);
            if (child is VBoxContainer vbox && vbox.GetChildCount() >= 3)
                return vbox;
            if (child is ScrollContainer scroll)
            {
                var inner = FindSettingsContainer(scroll, depth + 1, maxDepth);
                if (inner != null) return inner;
            }
            if (child is Container or Control or TabContainer)
            {
                var found = FindSettingsContainer(child, depth + 1, maxDepth);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static VBoxContainer CreateLanSettingsSection()
    {
        var section = new VBoxContainer
        {
            Name = "LanSettingsSection",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        section.AddThemeConstantOverride("separation", 8);

        var title = new Label { Text = Strings.T("局域网联机", "LAN Multiplayer") };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.937255f, 0.784314f, 0.317647f));
        section.AddChild(title);

        var maxRow = new HBoxContainer();
        maxRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        maxRow.AddThemeConstantOverride("separation", 12);
        var maxLabel = new Label
        {
            Text = Strings.T("最大玩家数", "Max LAN Players"),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        maxLabel.AddThemeFontSizeOverride("font_size", 16);
        var maxSpin = new SpinBox
        {
            MinValue = 2,
            MaxValue = 8,
            Step = 1,
            CustomMinimumSize = new Vector2(80, 0),
        };
        maxSpin.SetValueNoSignal(LanConfig.GetInt("max_multiplayer_players", 4));
        maxSpin.Connect(SpinBox.SignalName.ValueChanged, Callable.From<double>(v =>
        {
            LanConfig.SetValue("max_multiplayer_players", (int)v);
            PatchHelper.Log($"Max LAN players set to {(int)v}");
        }));
        maxRow.AddChild(maxLabel);
        maxRow.AddChild(maxSpin);
        section.AddChild(maxRow);

        var maxHint = new Label { Text = Strings.T("局域网房间的最大玩家数 (2-8)。", "Maximum number of players in a LAN lobby (2-8).") };
        maxHint.AddThemeFontSizeOverride("font_size", 13);
        maxHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        section.AddChild(maxHint);

        var nameRow = new HBoxContainer();
        nameRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameRow.AddThemeConstantOverride("separation", 12);
        var nameLabel = new Label
        {
            Text = Strings.T("玩家名称 (可选)", "Player Name (optional)"),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        var nameInput = new LineEdit
        {
            CustomMinimumSize = new Vector2(200, 0),
            MaxLength = 32,
            PlaceholderText = Strings.T("自动生成", "Auto-generated"),
            Text = LanConfig.GetBool("lan_use_custom_player_id", false)
                ? LanConfig.GetString("lan_custom_player_id", "")
                : "",
        };
        nameInput.Connect(LineEdit.SignalName.TextSubmitted, Callable.From<string>(text =>
        {
            // Save on Enter key press
            var useCustom = !string.IsNullOrWhiteSpace(text);
            LanConfig.SetValue("lan_use_custom_player_id", useCustom);
            LanConfig.SetValue("lan_custom_player_id", text ?? "");
        }));
        nameInput.Connect(LineEdit.SignalName.FocusExited, Callable.From(() =>
        {
            // Save when focus is lost (user clicks away)
            var text = nameInput.Text;
            var useCustom = !string.IsNullOrWhiteSpace(text);
            LanConfig.SetValue("lan_use_custom_player_id", useCustom);
            LanConfig.SetValue("lan_custom_player_id", text ?? "");
        }));
        nameRow.AddChild(nameLabel);
        nameRow.AddChild(nameInput);
        section.AddChild(nameRow);

        var nameHint = new Label { Text = Strings.T("在局域网游戏中显示给其他玩家。下次创建或加入游戏时生效。", "Shown to other players in LAN games. Applies to the next hosted or joined game.") };
        nameHint.AddThemeFontSizeOverride("font_size", 13);
        nameHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        section.AddChild(nameHint);

        var hint = new Label { Text = Strings.T("更改在下次创建或加入游戏时生效。\n配置: %APPDATA%/SlayTheSpire2/LanSpire/lan_config.json", "Changes apply to the next hosted or joined game.\nConfig: %APPDATA%/SlayTheSpire2/LanSpire/lan_config.json") };
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        section.AddChild(hint);

        var logBtn = new Button
        {
            Text = Strings.T("打开游戏日志", "Open Game Log"),
            CustomMinimumSize = new Vector2(0, 40),
        };
        logBtn.AddThemeFontSizeOverride("font_size", 14);
        StyleLanButton(logBtn);
        logBtn.Connect(Button.SignalName.Pressed, Callable.From(() =>
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "SlayTheSpire2", "logs", "godot.log");
                if (System.IO.File.Exists(logPath))
                    System.Diagnostics.Process.Start("notepad.exe", $"\"{logPath}\"");
                else
                    PatchHelper.Log($"Log file not found at {logPath}");
            }
            catch (Exception ex) { PatchHelper.Log($"Failed to open log: {ex}"); }
        }));
        section.AddChild(logBtn);

        return section;
    }
}