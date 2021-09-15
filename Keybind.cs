using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Dalamud.Interface;
using ImGuiNET;

namespace QoLBar
{
    public static class Keybind
    {
        private const int modifierMask = -1 << (int)Keys.ShiftKey;
        private const int shiftModifier = 1 << (int)Keys.ShiftKey;
        private const int controlModifier = 1 << (int)Keys.ControlKey;
        private const int altModifier = 1 << (int)Keys.Menu;

        public static readonly List<(BarUI, ShortcutUI)> hotkeys = new();

        public struct QoLKeyState
        {
            [Flags]
            public enum State
            {
                None = 0,
                Held = 1,
                KeyDown = 2,
                KeyUp = 4,
                ShortHold = 8
            }

            public State CurrentState { get; private set; }
            public float HoldDuration { get; private set; }
            public bool useKeyUp;
            public bool useShortHold;
            public bool wasShortHeld;

            public void Update(bool down)
            {
                if (down)
                {
                    var lastState = CurrentState;
                    CurrentState = State.Held;
                    if ((lastState & State.Held) == 0)
                        CurrentState |= State.KeyDown;
                    else if (HoldDuration >= 0.2f)
                        CurrentState |= State.ShortHold;

                    HoldDuration += (float)DalamudApi.Framework.UpdateDelta.TotalSeconds;
                }
                else if (CurrentState != State.None)
                {
                    wasShortHeld = (CurrentState & State.ShortHold) != 0;
                    CurrentState = CurrentState != State.KeyUp ? State.KeyUp : State.None;
                    HoldDuration = 0;
                }
            }
        }

        private static readonly byte[] keyboardState = new byte[256];
        private static readonly QoLKeyState[] keyState = new QoLKeyState[keyboardState.Length];
        private static bool Disabled => Game.IsGameTextInputActive || !Game.IsGameFocused || ImGui.GetIO().WantCaptureKeyboard;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        public static void Run()
        {
            GetKeyState();
            DoPieHotkeys();
            DoHotkeys();
        }

        public static void SetupHotkeys(List<BarUI> bars)
        {
            foreach (var bar in bars.Where(bar => bar.IsVisible))
                bar.SetupHotkeys();
        }

        private static void GetKeyState()
        {
            GetKeyboardState(keyboardState);
            for (int i = 0; i < keyState.Length; i++)
                keyState[i].Update((keyboardState[i] & 0x80) != 0);
        }

        public static bool CheckKeyState(int i, QoLKeyState.State state) => i is >= 0 and < 256 && (keyState[i].CurrentState & state) != 0;

        public static bool IsHotkeyActivated(int i) => i is >= 0 and < 256 && (!keyState[i].useKeyUp
            ? CheckKeyState(i, QoLKeyState.State.KeyDown)
            : CheckKeyState(i, QoLKeyState.State.KeyUp));

        public static int GetModifiers()
        {
            var key = 0;
            var io = ImGui.GetIO();
            if (io.KeyShift)
                key |= shiftModifier;
            if (io.KeyCtrl)
                key |= controlModifier;
            if (io.KeyAlt)
                key |= altModifier;
            return key;
        }

        public static bool IsHotkeyHeld(int hotkey, bool blockGame)
        {
            if (Disabled) return false;

            var key = hotkey & ~modifierMask;
            var isDown = CheckKeyState(key, QoLKeyState.State.Held) && hotkey == (key | GetModifiers());

            if (isDown)
            {
                if (keyState[key].useShortHold)
                    isDown = CheckKeyState(key, QoLKeyState.State.ShortHold);

                if (blockGame)
                    BlockGameKey(key);
            }

            keyState[key].useKeyUp = true;
            keyState[key].useShortHold = false;

            return isDown;
        }

        public static void BlockGameKey(int key)
        {
            try { DalamudApi.KeyState[key] = false; }
            catch { }
        }

        private static void DoPieHotkeys()
        {
            if (!PieUI.enabled) return;

            foreach (var bar in QoLBar.Plugin.ui.bars.Where(bar => bar.Config.Hotkey > 0 && bar.CheckConditionSet()))
            {
                if (IsHotkeyHeld(bar.Config.Hotkey, true))
                {
                    if (bar.tempDisableHotkey <= 0)
                    {
                        bar.openPie = true;
                        return;
                    }
                }
                else if (bar.tempDisableHotkey > 0)
                {
                    --bar.tempDisableHotkey;
                }
                bar.openPie = false;
            }

            PieUI.enabled = false; // Used to disable all pies if the UI is hidden
        }

        // TODO: Loop through hotkeys instead of all keys
        // TODO: Fix bug where keys activate after being pressed if they use key up
        private static void DoHotkeys()
        {
            if (Disabled) { hotkeys.Clear(); return; }
            if (hotkeys.Count == 0) return;

            var key = GetModifiers();
            for (var k = 0; k < 240; k++)
            {
                var state = keyState[k];
                var activated = k is < 16 or > 18 && IsHotkeyActivated(k) && (!state.useKeyUp || !state.wasShortHeld);
                var hotkey = key | k;
                foreach (var (bar, sh) in hotkeys)
                {
                    var cfg = sh.Config;
                    if (cfg.Hotkey != hotkey) continue;

                    if (state.useKeyUp)
                    {
                        keyState[k].useKeyUp = false;
                        keyState[k].useShortHold = true;
                    }

                    if (!activated) break;

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
                    {
                        sh.OnClick(false, false, false, true);
                    }

                    if (!cfg.KeyPassthrough)
                        BlockGameKey(k);
                }
            }
            hotkeys.Clear();
        }

        public static void AddHotkey(ShortcutUI sh) => hotkeys.Add((sh.parentBar, sh));

        private static bool InputHotkey(string id, ref int hotkey)
        {
            var dispKey = GetKeyName(hotkey);
            ImGui.InputText($"{id}##{hotkey}", ref dispKey, 200, ImGuiInputTextFlags.ReadOnly | ImGuiInputTextFlags.AllowTabInput); // delete the box to delete focus 4head
            if (ImGui.IsItemActive())
            {
                var keysDown = ImGui.GetIO().KeysDown;
                var key = 0;
                if (ImGui.GetIO().KeyShift)
                    key |= shiftModifier;
                if (ImGui.GetIO().KeyCtrl)
                    key |= controlModifier;
                if (ImGui.GetIO().KeyAlt)
                    key |= altModifier;
                for (var k = 0; k < keysDown.Count; k++)
                {
                    if (k is >= 16 and <= 18 || !keysDown[k] || ImGui.GetIO().KeysDownDuration[k] > 0) continue;

                    key |= k;
                    hotkey = key;
                    return true;
                }
            }

            if (!ImGui.IsItemDeactivated() || !ImGui.GetIO().KeysDown[(int)Keys.Escape]) return false;

            hotkey = 0;
            return true;
        }

        public static bool KeybindInput(ShCfg sh)
        {
            var ret = false;
            if (InputHotkey("Hotkey", ref sh.Hotkey))
            {
                QoLBar.Config.Save();
                ret = true;
            }
            ImGuiEx.SetItemTooltip("Press escape to clear the hotkey.");

            if (sh.Hotkey <= 0) return ret;

            if (ImGui.Checkbox("Pass Input to Game", ref sh.KeyPassthrough))
                QoLBar.Config.Save();
            ImGuiEx.SetItemTooltip("Disables the hotkey from blocking the game input.");
            return ret;
        }

        public static bool KeybindInput(BarCfg bar)
        {
            var ret = false;
            if (InputHotkey("Pie Hotkey", ref bar.Hotkey))
            {
                QoLBar.Config.Save();
                ret = true;
            }
            ImGuiEx.SetItemTooltip("Use this to specify a held hotkey to bring the bar up as a pie menu.\n" +
                "Press escape to clear the hotkey.");
            return ret;
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

        private static readonly Dictionary<Keys, string> _keynames = new()
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
                return mod + key;
        }
    }
}
