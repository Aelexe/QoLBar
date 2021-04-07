﻿using System.Collections.Generic;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Dalamud.Interface;
using ImGuiNET;

namespace QoLBar
{
    public static class Keybind
    {
        public static readonly List<(BarUI, ShortcutUI)> hotkeys = new List<(BarUI, ShortcutUI)>();
        private static readonly byte[] keyState = new byte[256];
        private static readonly bool[] prevKeyState = new bool[keyState.Length];
        private static readonly bool[] keyPressed = new bool[keyState.Length];

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        public static void Run(bool gameInputActive)
        {
            GetKeyState();
            DoHotkeys(gameInputActive);
        }

        public static void SetupHotkeys(List<BarUI> bars)
        {
            foreach (var bar in bars)
                if (bar.IsVisible)
                    bar.SetupHotkeys();
        }

        private static void GetKeyState()
        {
            GetKeyboardState(keyState);
            for (int i = 0; i < keyState.Length; i++)
            {
                var down = (keyState[i] & 0x80) != 0;
                keyPressed[i] = down && !prevKeyState[i];
                prevKeyState[i] = down;
            }
        }

        private static void DoHotkeys(bool gameInputActive)
        {
            if (gameInputActive || !QoLBar.IsGameFocused || ImGui.GetIO().WantCaptureKeyboard) { hotkeys.Clear(); return; }

            if (hotkeys.Count > 0)
            {
                var key = 0;
                if (ImGui.GetIO().KeyShift)
                    key |= (int)Keys.Shift;
                if (ImGui.GetIO().KeyCtrl)
                    key |= (int)Keys.Control;
                if (ImGui.GetIO().KeyAlt)
                    key |= (int)Keys.Alt;
                for (var k = 0; k < keyState.Length; k++)
                {
                    if (16 <= k && k <= 18) continue;

                    if (keyPressed[k])
                    {
                        foreach ((var bar, var sh) in hotkeys)
                        {
                            var cfg = sh.Config;
                            if (cfg.Hotkey == (key | k))
                            {
                                if (cfg.Type == ShCfg.ShortcutType.Category && cfg.Mode == ShCfg.ShortcutMode.Default)
                                {
                                    // TODO: Make less hacky
                                    bar.ForceReveal();
                                    var parent = sh.parent;
                                    while (parent != null)
                                    {
                                        parent._activated = true;
                                        parent = parent.parent;
                                    }
                                    sh._activated = true;
                                }
                                else
                                    sh.OnClick(false, false);

                                if (!cfg.KeyPassthrough && k <= 160)
                                    QoLBar.Interface.ClientState.KeyState[k] = false;
                            }
                        }
                    }
                }
                hotkeys.Clear();
            }
        }

        public static void AddHotkey(ShortcutUI sh) => hotkeys.Add((sh.parentBar, sh));

        public static void KeybindInput(ShCfg sh)
        {
            var dispKey = GetKeyName(sh.Hotkey);
            ImGui.InputText($"Hotkey##{sh.Hotkey}", ref dispKey, 200, ImGuiInputTextFlags.ReadOnly | ImGuiInputTextFlags.AllowTabInput); // delete the box to delete focus 4head
            if (ImGui.IsItemActive())
            {
                var keysDown = ImGui.GetIO().KeysDown;
                var key = 0;
                if (ImGui.GetIO().KeyShift)
                    key |= (int)Keys.Shift;
                if (ImGui.GetIO().KeyCtrl)
                    key |= (int)Keys.Control;
                if (ImGui.GetIO().KeyAlt)
                    key |= (int)Keys.Alt;
                for (var k = 0; k < keyState.Length; k++)
                {
                    if (16 <= k && k <= 18) continue;

                    if (keysDown[k] && ImGui.GetIO().KeysDownDuration[k] == 0)
                    {
                        key |= k;
                        sh.Hotkey = key;
                        QoLBar.Config.Save();
                        break;
                    }
                }
            }
            if (ImGui.IsItemDeactivated() && ImGui.GetIO().KeysDown[(int)Keys.Escape])
            {
                sh.Hotkey = 0;
                sh.KeyPassthrough = false;
                QoLBar.Config.Save();
            }
            ImGuiEx.SetItemTooltip("Press escape to clear the hotkey.");

            if (sh.Hotkey > 0)
            {
                if (ImGui.Checkbox("Pass Input to Game", ref sh.KeyPassthrough))
                    QoLBar.Config.Save();
                ImGuiEx.SetItemTooltip("Disables the hotkey from blocking the game input.\n" +
                    "Some keys are unable to be blocked.");
            }
        }

        public static void DrawDebug()
        {
            ImGui.TextUnformatted($"Active Hotkeys - {hotkeys.Count}");
            ImGui.Spacing();
            if (hotkeys.Count < 1)
                ImGui.Separator();
            else
            {
                ImGui.Columns(2);
                ImGui.Separator();
                for (int i = 0; i < hotkeys.Count; i++)
                {
                    ImGui.PushID(i);

                    (_, var ui) = hotkeys[i];
                    var sh = ui.Config;
                    if (ImGui.SmallButton("Delete"))
                    {
                        sh.Hotkey = 0;
                        sh.KeyPassthrough = false;
                        QoLBar.Config.Save();
                    }
                    ImGui.SameLine();
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextUnformatted(sh.KeyPassthrough ? FontAwesomeIcon.CheckCircle.ToIconString() : FontAwesomeIcon.TimesCircle.ToIconString());
                    ImGui.PopFont();
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Hotkey {(sh.KeyPassthrough ? "doesn't block" : "blocks")} game input.");

                        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        {
                            sh.KeyPassthrough = !sh.KeyPassthrough;
                            QoLBar.Config.Save();
                        }
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted(GetKeyName(sh.Hotkey));
                    ImGui.NextColumn();
                    if (sh.Type == ShCfg.ShortcutType.Category)
                        ImGui.TextUnformatted($"{sh.Mode} {(ui.parent == null ? "Category" : "Subcategory")} \"{sh.Name}\" {(string.IsNullOrEmpty(sh.Command) ? "" : "\n" + sh.Command)}");
                    else
                        ImGui.TextUnformatted(sh.Command);
                    ImGui.NextColumn();
                    if (i != hotkeys.Count - 1) // Shift last separator outside of columns so it doesn't clip with column borders
                        ImGui.Separator();

                    ImGui.PopID();
                }
                ImGui.Columns(1);
                ImGui.Separator();
            }
        }

        private static readonly Dictionary<Keys, string> _keynames = new Dictionary<Keys, string>
        {
            [Keys.ShiftKey] = "Shift",
            [Keys.ControlKey] = "Ctrl",
            [Keys.Menu] = "Alt",
            [Keys.PageUp] = "PageUp",
            [Keys.PageDown] = "PageDown",
            [Keys.PrintScreen] = "PrintScreen",
            [Keys.D0] = "0",
            [Keys.D1] = "1",
            [Keys.D2] = "2",
            [Keys.D3] = "3",
            [Keys.D4] = "4",
            [Keys.D5] = "5",
            [Keys.D6] = "6",
            [Keys.D7] = "7",
            [Keys.D8] = "8",
            [Keys.D9] = "9",
            [Keys.Scroll] = "ScrollLock",
            [Keys.OemSemicolon] = ";",
            [Keys.Oemplus] = "=",
            [Keys.OemMinus] = "-",
            [Keys.Oemcomma] = ",",
            [Keys.OemPeriod] = ".",
            [Keys.OemQuestion] = "/",
            [Keys.Oemtilde] = "`",
            [Keys.OemOpenBrackets] = "[",
            [Keys.OemPipe] = "\\",
            [Keys.OemCloseBrackets] = "]",
            [Keys.OemQuotes] = "'"
        };
        public static string GetKeyName(int k)
        {
            var key = (Keys)k;
            string mod = string.Empty;
            if ((key & Keys.Shift) != 0)
            {
                mod += "Shift + ";
                key -= Keys.Shift;
            }
            if ((key & Keys.Control) != 0)
            {
                mod += "Ctrl + ";
                key -= Keys.Control;
            }
            if ((key & Keys.Alt) != 0)
            {
                mod += "Alt + ";
                key -= Keys.Alt;
            }
            if (_keynames.TryGetValue(key, out var name))
                return mod + name;
            else
                return mod + key.ToString();
        }
    }
}
