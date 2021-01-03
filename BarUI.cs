using ImGuiNET;
using System;
using System.Numerics;
using System.Collections.Generic;
using static QoLBar.BarConfig;
using Dalamud.Plugin;

namespace QoLBar
{
    public class BarUI : IDisposable
    {
        private int barNumber;
        private BarConfig barConfig => config.BarConfigs[barNumber];
        public void SetBarNumber(int i)
        {
            barNumber = i;
            SetupPosition();
        }

#if DEBUG
        public bool IsVisible => !barConfig.Hidden;
#else
        public bool IsVisible => !barConfig.Hidden && (plugin.pluginInterface.ClientState.LocalPlayer != null || _lastLocalPlayer < 3);
#endif
        public void ToggleVisible()
        {
            barConfig.Hidden = !barConfig.Hidden;
            config.Save();
        }
        private float _lastLocalPlayer = 9999;

        private Shortcut _sh;
        private static Vector2 window = ImGui.GetIO().DisplaySize;
        private static Vector2 mousePos = ImGui.GetIO().MousePos;
        private static float globalSize;
        private Vector2 barSize = new Vector2(200, 38);
        private Vector2 barPos;
        private ImGuiWindowFlags flags;
        private readonly int maxCommandLength = 180; // 180 is the max per line for macros, 500 is the max you can actually type into the chat, however it is still possible to inject more
        private Vector2 piv = new Vector2();
        private Vector2 hidePos = new Vector2();
        private Vector2 revealPos = new Vector2();
        private bool vertical = false;
        private bool docked = true;

        private bool _reveal = false;
        private void Reveal() => _reveal = true;
        private void Hide() => _reveal = false;

        private bool _firstframe = true;
        private bool _setPos = true;
        private bool _lastReveal = true;
        private Vector2 _tweenStart;
        private float _tweenProgress = 1;
        private Vector2 _catpiv = new Vector2();
        private Vector2 _catpos = new Vector2();

        private readonly QoLBar plugin;
        private readonly Configuration config;

        public BarUI(QoLBar p, Configuration config, int nbar)
        {
            plugin = p;
            this.config = config;
            barNumber = nbar;
            plugin.LoadIcon(46); // Magnifying glass / Search
            SetupPosition();
        }

        private void SetupPosition()
        {
            var pivX = 0.0f;
            var pivY = 0.0f;
            var defPos = 0.0f;
            var offset = 0.0f;
            switch (barConfig.DockSide)
            {
                case BarDock.Top: //    0.0 1.0, 0.5 1.0, 1.0 1.0 // 0 0(+H),    winX/2 0(+H),    winX 0(+H)
                    pivY = 1.0f;
                    defPos = 0.0f;
                    vertical = false;
                    docked = true;
                    break;
                case BarDock.Left: //   1.0 0.0, 1.0 0.5, 1.0 1.0 // 0(+W) 0,    0(+W) winY/2,    0(+W) winY
                    pivY = 1.0f;
                    defPos = 0.0f;
                    vertical = true;
                    docked = true;
                    break;
                case BarDock.Bottom: // 0.0 0.0, 0.5 0.0, 1.0 0.0 // 0 winY(-H), winX/2 winY(-H), winX winY(-H)
                    pivY = 0.0f;
                    defPos = window.Y;
                    vertical = false;
                    docked = true;
                    break;
                case BarDock.Right: //  0.0 0.0, 0.0 0.5, 0.0 1.0 // winX(-W) 0, winX(-W) winY/2, winX(-W) winY
                    pivY = 0.0f;
                    defPos = window.X;
                    vertical = true;
                    docked = true;
                    break;
                case BarDock.UndockedH:
                    piv = new Vector2();
                    vertical = false;
                    docked = false;
                    _setPos = true;
                    return;
                case BarDock.UndockedV:
                    piv = new Vector2();
                    vertical = true;
                    docked = false;
                    _setPos = true;
                    return;
                default:
                    break;
            }

            switch (barConfig.Alignment)
            {
                case BarAlign.LeftOrTop:
                    pivX = 0.0f;
                    offset = 22 + ImGui.GetFontSize();
                    break;
                case BarAlign.Center:
                    pivX = 0.5f;
                    break;
                case BarAlign.RightOrBottom:
                    pivX = 1.0f;
                    offset = -22 - ImGui.GetFontSize();
                    break;
                default:
                    break;
            }

            if (!vertical)
            {
                piv.X = pivX;
                piv.Y = pivY;

                hidePos.X = window.X * pivX + offset;
                hidePos.Y = defPos;
                revealPos.X = hidePos.X;
            }
            else
            {
                piv.X = pivY;
                piv.Y = pivX;

                hidePos.X = defPos;
                hidePos.Y = window.Y * pivX + offset;
                revealPos.Y = hidePos.Y;
            }

            //PluginLog.Log($"piv {piv.X} {piv.Y} hide {hidePos.X} {hidePos.Y} reveal {revealPos.X} {revealPos.Y}");

            SetupRevealPosition();

            barPos = hidePos;
            _tweenStart = hidePos;
        }

        private void SetupRevealPosition()
        {
            switch (barConfig.DockSide)
            {
                case BarDock.Top:
                    revealPos.Y = hidePos.Y + barSize.Y;
                    break;
                case BarDock.Left:
                    revealPos.X = hidePos.X + barSize.X;
                    break;
                case BarDock.Bottom:
                    revealPos.Y = hidePos.Y - barSize.Y;
                    break;
                case BarDock.Right:
                    revealPos.X = hidePos.X - barSize.X;
                    break;
                case BarDock.UndockedH:
                    break;
                case BarDock.UndockedV:
                    break;
                default:
                    break;
            }
        }

        private void SetupImGuiFlags()
        {
            flags = ImGuiWindowFlags.None;

            flags |= ImGuiWindowFlags.NoDecoration;
            if (docked || barConfig.LockedPosition)
                flags |= ImGuiWindowFlags.NoMove;
            flags |= ImGuiWindowFlags.NoScrollWithMouse;
            if (barConfig.NoBackground)
                flags |= ImGuiWindowFlags.NoBackground;
            flags |= ImGuiWindowFlags.NoSavedSettings;
            flags |= ImGuiWindowFlags.NoFocusOnAppearing;
        }

        public void Draw()
        {
            if (!IsVisible)
            {
                _lastLocalPlayer += ImGui.GetIO().DeltaTime;
                return;
            }
            else
                _lastLocalPlayer = 0;

            var io = ImGui.GetIO();
            window = io.DisplaySize;
            mousePos = io.MousePos;
            globalSize = io.FontGlobalScale;

            if (docked || barConfig.Visibility == VisibilityMode.Immediate)
            {
                SetupRevealPosition();

                CheckMouse();
            }
            else
                Reveal();

            if (!docked && !_firstframe && !_reveal && !_lastReveal)
                return;

            if (_firstframe || _reveal || barPos != hidePos || (!docked && _lastReveal)) // Don't bother to render when fully off screen
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.286f, 0.286f, 0.286f, 0.9f));

                if (docked)
                    ImGui.SetNextWindowPos(barPos, ImGuiCond.Always, piv);
                else if (_setPos || barConfig.LockedPosition)
                {
                    if (!_firstframe)
                    {
                        ImGui.SetNextWindowPos(barConfig.Position);
                        _setPos = false;
                    }
                    else
                        ImGui.SetNextWindowPos(new Vector2(window.X, window.Y));
                }
                ImGui.SetNextWindowSize(barSize);

                SetupImGuiFlags();
                ImGui.Begin($"QoLBar##{barNumber}", flags);

                ImGui.SetWindowFontScale(barConfig.Scale);

                if (ImGui.IsWindowHovered())
                {
                    Reveal();

                    if (ImGui.IsMouseReleased(1))
                        ImGui.OpenPopup($"BarConfig##{barNumber}");
                }

                DrawItems();

                if (!barConfig.HideAdd || barConfig.ShortcutList.Count < 1)
                    DrawAddButton();

                if (!barConfig.LockedPosition && !_firstframe && !docked && ImGui.GetWindowPos() != barConfig.Position)
                {
                    barConfig.Position = ImGui.GetWindowPos();
                    config.Save();
                }

                ImGui.SetWindowFontScale(1);
                BarConfigPopup();
                ImGui.SetWindowFontScale(barConfig.Scale);

                SetBarSize();

                ImGui.End();

                ImGui.PopStyleColor();
                ImGui.PopStyleVar(2);
            }

            if (docked)
            {
                SetBarPosition();
                Hide(); // Allows other objects to reveal the bar
            }
            else
                _lastReveal = _reveal;

            _firstframe = false;
        }

        private void CheckMouse()
        {
            Vector2 _pos;
            if (docked)
            {
                if (barConfig.DockSide == BarDock.Bottom || barConfig.DockSide == BarDock.Top)
                {
                    var _offset = Math.Min(barSize.Y * (1 - barConfig.RevealAreaScale), barSize.Y - 1);
                    if (barConfig.DockSide == BarDock.Bottom)
                        _pos = new Vector2(revealPos.X, revealPos.Y + _offset);
                    else
                        _pos = new Vector2(revealPos.X, revealPos.Y - _offset);
                }
                else
                {
                    var _offset = Math.Min(barSize.X * (1 - barConfig.RevealAreaScale), barSize.X - 1);
                    if (barConfig.DockSide == BarDock.Right)
                        _pos = new Vector2(revealPos.X + _offset, revealPos.Y);
                    else
                        _pos = new Vector2(revealPos.X - _offset, revealPos.Y);
                }
            }
            else
                _pos = barConfig.Position;

            var _min = new Vector2(_pos.X - (barSize.X * piv.X), _pos.Y - (barSize.Y * piv.Y));
            var _max = new Vector2(_pos.X + (barSize.X * (1 - piv.X)), _pos.Y + (barSize.Y * (1 - piv.Y)));
            var mX = mousePos.X;
            var mY = mousePos.Y;

            //if (ImGui.IsMouseHoveringRect(_min, _max, true)) // This only works in the context of a window... thanks ImGui
            if (barConfig.Visibility == VisibilityMode.Always || (_min.X <= mX && mX < _max.X && _min.Y <= mY && mY < _max.Y))
                Reveal();
            else
                Hide();
        }

        private bool ParseName(ref string name, out string tooltip, out int icon)
        {
            var args = name.Split(new[] { "##" }, 2, StringSplitOptions.None);
            name = args[0];

            tooltip = (args.Length > 1) ? args[1] : "";

            icon = 0;
            if (name.StartsWith("::"))
            {
                int.TryParse(name.Substring(2), out icon);
                return true;
            }
            else
                return false;
        }

        private void DrawItems()
        {
            for (int i = 0; i < barConfig.ShortcutList.Count; i++)
            {
                var sh = barConfig.ShortcutList[i];
                var name = sh.Name;
                var type = sh.Type;
                var command = sh.Command;

                ImGui.PushID(i);

                var useIcon = ParseName(ref name, out string tooltip, out int icon);

                if (type == Shortcut.ShortcutType.Spacer)
                {
                    ImGui.BeginChild("", new Vector2(barConfig.ButtonWidth * globalSize * barConfig.Scale, ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.Y * 2));
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X / 2 - ImGui.CalcTextSize(name).X / 2);
                    ImGui.Text(name);
                    ImGui.EndChild();
                }
                else if (useIcon)
                    DrawIconButton(icon, new Vector2(ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.Y * 2), sh.IconZoom);
                else
                    ImGui.Button(name, new Vector2((!vertical && barConfig.AutoButtonWidth) ? 0 : (barConfig.ButtonWidth * globalSize * barConfig.Scale), 0));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                {
                    Reveal();

                    if (ImGui.IsMouseReleased(0))
                        ItemClicked(type, command);

                    if (!string.IsNullOrEmpty(tooltip))
                        ImGui.SetTooltip(tooltip);
                }

                if (type == Shortcut.ShortcutType.Category)
                {
                    ImGui.SetWindowFontScale(barConfig.CategoryScale);
                    CategoryPopup(sh);
                    ImGui.SetWindowFontScale(barConfig.Scale);
                }

                ImGui.OpenPopupOnItemClick("editItem", 1);

                ImGui.SetWindowFontScale(1);
                ItemConfigPopup(barConfig.ShortcutList, i);
                ImGui.SetWindowFontScale(barConfig.Scale);

                if (!vertical && i != barConfig.ShortcutList.Count - 1)
                    ImGui.SameLine();

                ImGui.PopID();
            }
        }

        private void DrawAddButton()
        {
            if (!vertical && barConfig.ShortcutList.Count > 0)
                ImGui.SameLine();

            ImGui.Button("+", new Vector2((!vertical && barConfig.AutoButtonWidth) ? 0 : (barConfig.ButtonWidth * globalSize * barConfig.Scale), 0));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                Reveal();
                ImGui.SetTooltip("Add a new button.\nRight click this (or the bar background) for options.\nRight click other buttons to edit them.");
            }

            ImGui.OpenPopupOnItemClick("addItem", 0);
            ImGui.OpenPopupOnItemClick($"BarConfig##{barNumber}", 1);

            ImGui.SetWindowFontScale(1);
            ItemCreatePopup(barConfig.ShortcutList);
            ImGui.SetWindowFontScale(barConfig.Scale);
        }

        private void ItemClicked(Shortcut.ShortcutType type, string command)
        {
            Reveal();

            switch (type)
            {
                case Shortcut.ShortcutType.Single:
                    if (!string.IsNullOrEmpty(command))
                        plugin.ExecuteCommand(command.Substring(0, Math.Min(command.Length, maxCommandLength)));
                    break;
                case Shortcut.ShortcutType.Multiline:
                    foreach (string c in command.Split('\n'))
                    {
                        if (!string.IsNullOrEmpty(c))
                            plugin.ExecuteCommand(c.Substring(0, Math.Min(c.Length, maxCommandLength)));
                    }
                    break;
                case Shortcut.ShortcutType.Category:
                    var align = 0; // Align to button (possible user option later)
                    var _pos = align switch
                    {
                        0 => ImGui.GetItemRectMin() + (ImGui.GetItemRectSize() / 2),
                        1 => ImGui.GetWindowPos() + (ImGui.GetWindowSize() / 2),
                        2 => mousePos,
                        _ => new Vector2(0),
                    };
                    var _offset = align switch
                    {
                        2 => 6.0f * globalSize,
                        _ => !vertical ? (ImGui.GetWindowHeight() / 2 - ImGui.GetStyle().FramePadding.Y) : (ImGui.GetWindowWidth() / 2 - ImGui.GetStyle().FramePadding.X),
                    };
                    var _x = _pos.X;
                    var _y = _pos.Y;
                    float _pX, _pY;

                    if (!vertical)
                    {
                        _pX = 0.5f;
                        if (_y < window.Y / 2)
                        {
                            _pY = 0.0f;
                            _y += _offset;
                        }
                        else
                        {
                            _pY = 1.0f;
                            _y -= _offset;
                        }
                    }
                    else
                    {
                        _pY = 0.5f;
                        if (_x < window.X / 2)
                        {
                            _pX = 0.0f;
                            _x += _offset;
                        }
                        else
                        {
                            _pX = 1.0f;
                            _x -= _offset;
                        }
                    }
                    _catpiv = new Vector2(_pX, _pY);
                    _catpos = new Vector2(_x, _y);
                    ImGui.OpenPopup("ShortcutCategory");
                    break;
                default:
                    break;
            }
        }

        private void CategoryPopup(Shortcut sh)
        {
            ImGui.SetNextWindowPos(_catpos, ImGuiCond.Appearing, _catpiv);
            if (ImGui.BeginPopup("ShortcutCategory", (barConfig.NoCategoryBackgrounds ? ImGuiWindowFlags.NoBackground : ImGuiWindowFlags.None) | ImGuiWindowFlags.NoMove))
            {
                Reveal();

                var sublist = sh.SubList;
                var cols = Math.Max(sh.CategoryColumns, 1);

                for (int j = 0; j < sublist.Count; j++)
                {
                    var _sh = sublist[j];
                    var _name = _sh.Name;
                    var _type = _sh.Type;
                    var _command = _sh.Command;

                    ImGui.PushID(j);

                    bool useIcon = ParseName(ref _name, out string tooltip, out int icon);

                    if (useIcon || !barConfig.NoCategoryBackgrounds)
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0));
                    else
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.08f, 0.08f, 0.08f, 0.94f));
                    if (_type == Shortcut.ShortcutType.Spacer)
                    {
                        ImGui.BeginChild("", new Vector2(sh.CategoryWidth * globalSize * barConfig.CategoryScale, ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.Y * 2));
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X / 2 - ImGui.CalcTextSize(_name).X / 2);
                        ImGui.Text(_name);
                        ImGui.EndChild();
                    }
                    else if (useIcon ? DrawIconButton(icon, new Vector2(ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.Y * 2), _sh.IconZoom) : ImGui.Button(_name, new Vector2(sh.CategoryWidth * globalSize * barConfig.CategoryScale, 0)))
                    {
                        ItemClicked(_type, _command);
                        if (!sh.CategoryStaysOpen)
                            ImGui.CloseCurrentPopup();
                    }
                    ImGui.PopStyleColor();
                    if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                        ImGui.SetTooltip(tooltip);

                    if (_type == Shortcut.ShortcutType.Category)
                        CategoryPopup(_sh);

                    if (j % cols != cols - 1)
                        ImGui.SameLine();

                    ImGui.OpenPopupOnItemClick("editItem", 1);

                    ImGui.SetWindowFontScale(1);
                    ItemConfigPopup(sublist, j);
                    ImGui.SetWindowFontScale(barConfig.CategoryScale);

                    ImGui.PopID();
                }

                if (!sh.HideAdd)
                {
                    if (!barConfig.NoCategoryBackgrounds)
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0));
                    else
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.08f, 0.08f, 0.08f, 0.94f));
                    if (ImGui.Button("+", new Vector2(sh.CategoryWidth * globalSize * barConfig.CategoryScale, 0)))
                        ImGui.OpenPopup("addItem");
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Add a new button.");
                }

                ImGui.SetWindowFontScale(1);
                ItemCreatePopup(sublist);
                ImGui.SetWindowFontScale(barConfig.CategoryScale);

                ClampWindowPos();

                ImGui.EndPopup();
            }
        }

        private void ItemBaseUI(Shortcut sh, bool editing)
        {
            Reveal();

            if (ImGui.InputText("Name          ", ref sh.Name, 256) && editing) // Not a bug... just ImGui not extending the window to fit multiline's name...
                config.Save();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Start the name with ::x where x is a number to use icons, i.e. \"::2914\".\n" +
                    "Use ## anywhere in the name to make the text afterwards into a tooltip,\ni.e. \"Name##This is a Tooltip\".");

            // No nested categories
            var _t = (int)sh.Type;
            if (ImGui.Combo("Type", ref _t, "Single\0Multiline\0Category")) // TODO: Fix spacers on categories to add them back
            {
                sh.Type = (Shortcut.ShortcutType)_t;
                if (sh.Type == Shortcut.ShortcutType.Category)
                    sh.SubList ??= new List<Shortcut>();

                if (sh.Type == Shortcut.ShortcutType.Single)
                    sh.Command = sh.Command.Split('\n')[0];
                //else if (sh.Type != Shortcut.ShortcutType.Multiline)
                //    sh.Command = "";

                if (editing)
                    config.Save();
            }

            switch (sh.Type)
            {
                case Shortcut.ShortcutType.Single:
                    if (ImGui.InputText("Command", ref sh.Command, (uint)maxCommandLength) && editing)
                        config.Save();
                    break;
                case Shortcut.ShortcutType.Multiline:
                    if (ImGui.InputTextMultiline("Command##Multi", ref sh.Command, (uint)maxCommandLength * 15, new Vector2(272 * globalSize, 124 * globalSize)) && editing)
                        config.Save();
                    break;
                default:
                    break;
            }
        }

        private void ItemCreatePopup(List<Shortcut> shortcuts)
        {
            if (ImGui.BeginPopup("addItem"))
            {
                _sh ??= new Shortcut();
                ItemBaseUI(_sh, false);

                if (ImGui.Button("Create"))
                {
                    shortcuts.Add(_sh);
                    config.Save();
                    _sh = null;
                    ImGui.CloseCurrentPopup();
                }

                ClampWindowPos();

                ImGui.EndPopup();
            }
        }

        private void ItemConfigPopup(List<Shortcut> shortcuts, int i)
        {
            if (ImGui.BeginPopup("editItem"))
            {
                var sh = shortcuts[i];
                ItemBaseUI(sh, true);

                if (sh.Type == Shortcut.ShortcutType.Category)
                {
                    if (ImGui.Checkbox("Hide + Button", ref sh.HideAdd))
                        config.Save();

                    ImGui.SameLine(ImGui.GetWindowWidth() / 2);
                    if (ImGui.Checkbox("Stay Open on Selection", ref sh.CategoryStaysOpen))
                        config.Save();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Keeps the category open when selecting buttons within it.\nMay not work if the button interacts with other plugins.");

                    if (ImGui.SliderInt("Category Width", ref sh.CategoryWidth, 8, 200))
                        config.Save();

                    if (ImGui.SliderInt("Columns", ref sh.CategoryColumns, 1, 12))
                        config.Save();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Number of buttons in each row before starting another.");
                }

                if (ImGui.DragFloat("Icon Zoom", ref sh.IconZoom, 0.01f, 1.0f, 5.0f, "%.2f"))
                    config.Save();

                if (ImGui.Button((shortcuts == barConfig.ShortcutList && !vertical) ? "←" : "↑") && i > 0)
                {
                    shortcuts.RemoveAt(i);
                    shortcuts.Insert(i - 1, sh);
                    config.Save();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button((shortcuts == barConfig.ShortcutList && !vertical) ? "→" : "↓") && i < (shortcuts.Count - 1))
                {
                    shortcuts.RemoveAt(i);
                    shortcuts.Insert(i + 1, sh);
                    config.Save();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Delete"))
                    plugin.ExecuteCommand("/echo <se> Right click to delete!");
                //if (ImGui.IsItemClicked(1)) // Jesus christ I hate ImGui who made this function activate on PRESS AND NOT RELEASE??? THIS ISN'T A CLICK
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Right click this button to delete the shortcut!");

                    if (ImGui.IsMouseReleased(1))
                    {
                        shortcuts.RemoveAt(i);
                        config.Save();
                        ImGui.CloseCurrentPopup();
                    }
                }

                var iconSize = ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.Y * 2;
                ImGui.SameLine(ImGui.GetWindowContentRegionWidth() + ImGui.GetStyle().FramePadding.X * 2 - iconSize);
                if (DrawIconButton(46, new Vector2(iconSize), 1.0f))
                    plugin.ToggleIconBrowser();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Opens up a list of all icons you can use instead of text.\n" +
                        "Warning: This will load EVERY icon available so it will probably lag for a moment.\n" +
                        "Clicking on one will copy text to be pasted into the \"Name\" field of a button.");

                ClampWindowPos();

                ImGui.EndPopup();
            }
        }

        public void BarConfigPopup()
        {
            if (ImGui.BeginPopup($"BarConfig##{barNumber}"))
            {
                Reveal();

                var _dock = (int)barConfig.DockSide;
                if (ImGui.Combo("Bar Side", ref _dock, "Top\0Left\0Bottom\0Right\0Undocked\0Undocked (Vertical)"))
                {
                    barConfig.DockSide = (BarDock)_dock;
                    if (barConfig.DockSide == BarDock.UndockedH || barConfig.DockSide == BarDock.UndockedV)
                        barConfig.Visibility = VisibilityMode.Always;
                    config.Save();
                    SetupPosition();
                }

                if (docked)
                {
                    var _align = (int)barConfig.Alignment;
                    if (ImGui.Combo("Bar Alignment", ref _align, vertical ? "Top\0Center\0Bottom" : "Left\0Center\0Right"))
                    {
                        barConfig.Alignment = (BarAlign)_align;
                        config.Save();
                        SetupPosition();
                    }

                    var _visibility = (int)barConfig.Visibility;
                    if (ImGui.Combo("Bar Animation", ref _visibility, "Slide\0Immediate\0Always Visible"))
                    {
                        barConfig.Visibility = (VisibilityMode)_visibility;
                        config.Save();
                    }

                    if (barConfig.Visibility != VisibilityMode.Always)
                    {
                        if (ImGui.DragFloat("Reveal Area Scale", ref barConfig.RevealAreaScale, 0.01f, 0.0f, 1.0f, "%.2f"))
                            config.Save();
                    }
                }
                else
                {
                    var _visibility = (int)barConfig.Visibility - 1;
                    if (ImGui.Combo("Bar Animation", ref _visibility, "Immediate\0Always Visible"))
                    {
                        barConfig.Visibility = (VisibilityMode)(_visibility + 1);
                        config.Save();
                    }

                    if (ImGui.Checkbox("Lock Position", ref barConfig.LockedPosition))
                        config.Save();
                }

                if (ImGui.DragFloat("Bar Scale", ref barConfig.Scale, 0.01f, 0.7f, 2.0f, "%.2f"))
                    config.Save();

                if (ImGui.DragFloat("Category Scale", ref barConfig.CategoryScale, 0.01f, 0.7f, 1.5f, "%.2f"))
                    config.Save();

                if (!vertical)
                {
                    if (ImGui.Checkbox("Automatic Button Width", ref barConfig.AutoButtonWidth))
                        config.Save();
                }

                if (vertical || !barConfig.AutoButtonWidth)
                {
                    if (ImGui.SliderInt("Button Width", ref barConfig.ButtonWidth, 16, 200))
                        config.Save();
                }

                if (ImGui.Checkbox("No Bar Background", ref barConfig.NoBackground))
                    config.Save();
                ImGui.SameLine(ImGui.GetWindowWidth() / 2);
                if (ImGui.Checkbox("No Category Backgrounds", ref barConfig.NoCategoryBackgrounds))
                    config.Save();

                if (barConfig.ShortcutList.Count > 0)
                {
                    if (ImGui.Checkbox("Hide + Button", ref barConfig.HideAdd))
                    {
                        if (barConfig.HideAdd)
                            plugin.ExecuteCommand("/echo <se> You can right click on the bar itself (the black background) to reopen this settings menu!");
                        config.Save();
                    }
                }

                ImGui.Spacing();
                ImGui.Spacing();
                if (ImGui.Button("QoL Bar Config"))
                    plugin.ToggleConfig();

                ClampWindowPos();

                ImGui.EndPopup();
            }
        }

        private void SetBarSize()
        {
            barSize.Y = ImGui.GetCursorPosY() + 4;
            ImGui.SameLine();
            barSize.X = ImGui.GetCursorPosX();
        }

        private void ClampWindowPos()
        {
            var _lastPos = ImGui.GetWindowPos();
            var _size = ImGui.GetWindowSize();
            var _x = Math.Min(Math.Max(_lastPos.X, 0), window.X - _size.X);
            var _y = Math.Min(Math.Max(_lastPos.Y, 0), window.Y - _size.Y);
            ImGui.SetWindowPos(new Vector2(_x, _y));
        }

        private void SetBarPosition()
        {
            if (barConfig.Visibility == VisibilityMode.Slide)
                TweenBarPosition();
            else
                barPos = _reveal ? revealPos : hidePos;
        }

        private void TweenBarPosition()
        {
            if (_reveal != _lastReveal)
            {
                _lastReveal = _reveal;
                _tweenStart = barPos;
                _tweenProgress = 0;
            }

            if (_tweenProgress >= 1)
            {
                barPos = _reveal ? revealPos : hidePos;
            }
            else
            {
                var dt = ImGui.GetIO().DeltaTime * 2;
                _tweenProgress = Math.Min(_tweenProgress + dt, 1);

                var x = -1 * ((float)Math.Pow(_tweenProgress - 1, 4) - 1); // Quartic ease out
                var deltaX = ((_reveal ? revealPos.X : hidePos.X) - _tweenStart.X) * x;
                var deltaY = ((_reveal ? revealPos.Y : hidePos.Y) - _tweenStart.Y) * x;

                barPos.X = _tweenStart.X + deltaX;
                barPos.Y = _tweenStart.Y + deltaY;
            }
        }

        public bool DrawIconButton(int icon, Vector2 size, float zoom, bool retExists = false)
        {
            bool ret = false;
            var texd = plugin.textureDictionary;
            if (texd.ContainsKey(icon))
            {
                var tex = texd[icon];
                if (tex == null || tex.ImGuiHandle == IntPtr.Zero)
                {
                    if (!retExists)
                    {
                        if (icon == 66001)
                            ret = ImGui.Button("  X  ##FailedTexture");
                        else
                            ret = DrawIconButton(66001, size, zoom);
                    }
                }
                else
                {
                    var z = 0.5f / zoom;
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0));
                    ret = ImGui.ImageButton(texd[icon].ImGuiHandle, size, new Vector2(0.5f - z), new Vector2(0.5f + z), 0);
                    ImGui.PopStyleColor();
                    if (retExists)
                        ret = true;
                }
            }
            else
            {
                plugin.LoadIcon(icon);
                if (!retExists)
                    ret = ImGui.Button("  ?  ##WaitingTexture");
            }
            return ret;
        }

        public void Dispose()
        {

        }
    }
}
