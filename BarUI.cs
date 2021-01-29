using ImGuiNET;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Windows.Forms;
using Dalamud.Plugin;
using static QoLBar.BarConfig;

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

        public bool IsVisible => !barConfig.Hidden && CheckConditionSet();
        public void ToggleVisible()
        {
            barConfig.Hidden = !barConfig.Hidden;
            config.Save();
        }

        private static ImGuiStylePtr Style => ImGui.GetStyle();

        private static Shortcut _sh;
        private Vector2 window = ImGui.GetIO().DisplaySize;
        private static Vector2 mousePos = ImGui.GetIO().MousePos;
        private static float globalSize = ImGui.GetIO().FontGlobalScale;
        private Vector2 barSize = new Vector2(200, 38);
        private Vector2 barPos;
        private ImGuiWindowFlags flags;
        private static readonly int maxCommandLength = 180; // 180 is the max per line for macros, 500 is the max you can actually type into the chat, however it is still possible to inject more
        private Vector2 piv = Vector2.Zero;
        private Vector2 hidePos = Vector2.Zero;
        private Vector2 revealPos = Vector2.Zero;
        private bool vertical = false;
        private bool docked = true;

        private bool _reveal = false;
        private void Reveal() => _reveal = true;
        private void Hide() => _reveal = false;

        private bool _firstframe = true;
        private bool _setPos = true;
        private bool _lastReveal = true;
        private bool _mouseRevealed = false;
        private float _maxW = 0;
        private Vector2 _tweenStart;
        private float _tweenProgress = 1;
        private Vector2 _catpiv = Vector2.Zero;
        private Vector2 _catpos = Vector2.Zero;
        private Vector2 _maincatpos = Vector2.Zero;
        private static readonly Vector2 _defaultSpacing = new Vector2(8, 4);

        private readonly QoLBar plugin;
        private readonly Configuration config;

        public BarUI(QoLBar p, Configuration config, int nbar)
        {
            plugin = p;
            this.config = config;
            barNumber = nbar;
            SetupPosition();
        }

        private bool CheckConditionSet()
        {
            if (barConfig.ConditionSet >= 0 && barConfig.ConditionSet < config.ConditionSets.Count)
                return config.ConditionSets[barConfig.ConditionSet].CheckConditions();
            else
                return true;
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
                    piv = Vector2.Zero;
                    vertical = false;
                    docked = false;
                    _setPos = true;
                    return;
                case BarDock.UndockedV:
                    piv = Vector2.Zero;
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

                hidePos.X = window.X * pivX + offset + (barConfig.Offset.X * globalSize);
                hidePos.Y = defPos;
                revealPos.X = hidePos.X;
            }
            else
            {
                piv.X = pivY;
                piv.Y = pivX;

                hidePos.X = defPos;
                hidePos.Y = window.Y * pivX + offset + (barConfig.Offset.Y * globalSize);
                revealPos.Y = hidePos.Y;
            }

            SetupRevealPosition();

            barPos = hidePos;
            _tweenStart = hidePos;
        }

        private void SetupRevealPosition()
        {
            switch (barConfig.DockSide)
            {
                case BarDock.Top:
                    revealPos.Y = Math.Max(hidePos.Y + barSize.Y + (barConfig.Offset.Y * globalSize), GetHidePosition().Y + 1);
                    break;
                case BarDock.Left:
                    revealPos.X = Math.Max(hidePos.X + barSize.X + (barConfig.Offset.X * globalSize), GetHidePosition().X + 1);
                    break;
                case BarDock.Bottom:
                    revealPos.Y = Math.Min(hidePos.Y - barSize.Y + (barConfig.Offset.Y * globalSize), GetHidePosition().Y - 1);
                    break;
                case BarDock.Right:
                    revealPos.X = Math.Min(hidePos.X - barSize.X + (barConfig.Offset.X * globalSize), GetHidePosition().X - 1);
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

        private Vector2 GetHidePosition()
        {
            var _hidePos = hidePos;
            if (barConfig.Hint)
            {
                var _winPad = Style.WindowPadding * 2;

                switch (barConfig.DockSide)
                {
                    case BarDock.Top:
                        _hidePos.Y += _winPad.Y;
                        break;
                    case BarDock.Left:
                        _hidePos.X += _winPad.X;
                        break;
                    case BarDock.Bottom:
                        _hidePos.Y -= _winPad.Y;
                        break;
                    case BarDock.Right:
                        _hidePos.X -= _winPad.X;
                        break;
                    default:
                        break;
                }
            }
            return _hidePos;
        }

        private void SetupHotkeys(List<Shortcut> shortcuts)
        {
            foreach (var sh in shortcuts)
            {
                if (sh.Hotkey > 0)
                {
                    if (sh.Type == Shortcut.ShortcutType.Single || sh.Type == Shortcut.ShortcutType.Multiline)
                        plugin.AddHotkey(sh.Hotkey, sh.Command);
                    else if (sh.Type == Shortcut.ShortcutType.Category)
                        SetupHotkeys(sh.SubList);
                }
            }
        }

        public void Draw()
        {
            CheckGameResolution();

            if (!IsVisible) return;

            SetupHotkeys(barConfig.ShortcutList);

            var io = ImGui.GetIO();
            mousePos = io.MousePos;
            globalSize = io.FontGlobalScale;

            if (docked || barConfig.Visibility == VisibilityMode.Immediate)
            {
                SetupRevealPosition();

                CheckMousePosition();
            }
            else
                Reveal();

            if (!docked && !_firstframe && !_reveal && !_lastReveal)
                return;

            if (_firstframe || _reveal || (barPos != hidePos) || (!docked && _lastReveal)) // Don't bother to render when fully off screen
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(barConfig.Spacing));
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

                PushFontScale(barConfig.Scale);

                if (_mouseRevealed && ImGui.IsWindowHovered(ImGuiHoveredFlags.RectOnly))
                    Reveal();
                if (ImGui.IsMouseReleased(1) && ImGui.IsWindowHovered())
                    ImGui.OpenPopup($"BarConfig##{barNumber}");

                DrawItems();

                if (!barConfig.HideAdd || barConfig.ShortcutList.Count < 1)
                    DrawAddButton();

                if (!barConfig.LockedPosition && !_firstframe && !docked && ImGui.GetWindowPos() != barConfig.Position)
                {
                    barConfig.Position = ImGui.GetWindowPos();
                    config.Save();
                }

                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, _defaultSpacing);
                PushFontScale(1);
                BarConfigPopup();
                PopFontScale();
                ImGui.PopStyleVar();

                SetBarSize();

                PopFontScale();

                ImGui.End();

                ImGui.PopStyleColor();
                ImGui.PopStyleVar(3);
            }

            if (!_reveal)
                _mouseRevealed = false;

            if (docked)
            {
                SetBarPosition();
                Hide(); // Allows other objects to reveal the bar
            }
            else
                _lastReveal = _reveal;

            _firstframe = false;
        }

        private void CheckGameResolution()
        {
            var io = ImGui.GetIO();
            // Fix bar positions when the game is resized
            if (io.DisplaySize != window)
            {
                if (docked)
                {
                    window = io.DisplaySize;
                    SetupPosition();
                }
                else if (config.ResizeRepositionsBars)
                {
                    var x = io.DisplaySize / window;
                    barConfig.Position *= x;
                    config.Save();
                    _setPos = true;
                    window = io.DisplaySize;
                }
                else
                    window = io.DisplaySize;
            }
        }

        private void CheckMousePosition()
        {
            if (docked && _reveal)
                return;

            Vector2 _pos = docked ? revealPos : barConfig.Position;
            var _min = new Vector2(_pos.X - (barSize.X * piv.X), _pos.Y - (barSize.Y * piv.Y));
            var _max = new Vector2(_pos.X + (barSize.X * (1 - piv.X)), _pos.Y + (barSize.Y * (1 - piv.Y)));

            switch (barConfig.DockSide)
            {
                case BarDock.Top:
                    _max.Y = Math.Max(Math.Max(_max.Y - barSize.Y * (1 - barConfig.RevealAreaScale), _min.Y + 1), GetHidePosition().Y + 1);
                    break;
                case BarDock.Left:
                    _max.X = Math.Max(Math.Max(_max.X - barSize.X * (1 - barConfig.RevealAreaScale), _min.X + 1), GetHidePosition().X + 1);
                    break;
                case BarDock.Bottom:
                    _min.Y = Math.Min(Math.Min(_min.Y + barSize.Y * (1 - barConfig.RevealAreaScale), _max.Y - 1), GetHidePosition().Y - 1);
                    break;
                case BarDock.Right:
                    _min.X = Math.Min(Math.Min(_min.X + barSize.X * (1 - barConfig.RevealAreaScale), _max.X - 1), GetHidePosition().X - 1);
                    break;
                default:
                    break;
            }

            var mX = mousePos.X;
            var mY = mousePos.Y;

            //if (ImGui.IsMouseHoveringRect(_min, _max, true)) // This only works in the context of a window... thanks ImGui
            if (barConfig.Visibility == VisibilityMode.Always || (_min.X <= mX && mX < _max.X && _min.Y <= mY && mY < _max.Y))
            {
                _mouseRevealed = true;
                Reveal();
            }
            else
                Hide();
        }

        private bool ParseName(ref string name, out string tooltip, out int icon, out string args)
        {
            args = string.Empty;
            if (name == string.Empty)
            {
                tooltip = string.Empty;
                icon = 0;
                return false;
            }

            var split = name.Split(new[] { "##" }, 2, StringSplitOptions.None);
            name = split[0];

            tooltip = (split.Length > 1) ? split[1] : string.Empty;

            icon = 0;
            if (name.StartsWith("::"))
            {
                var substart = 2;

                // Parse icon arguments
                var done = false;
                while (!done)
                {
                    if (name.Length > substart)
                    {
                        var arg = name[substart];
                        switch (arg)
                        {
                            case '_': // Disable all args
                                args = "_";
                                substart = 3;
                                done = true;
                                break;
                            case 'f': // Use game icon frame
                                args += arg;
                                substart++;
                                break;
                            default:
                                done = true;
                                break;
                        }
                    }
                    else
                        done = true;
                }
                
                int.TryParse(name.Substring(substart), out icon);
                return true;
            }
            else
                return false;
        }

        private void DrawItems()
        {
            for (int i = 0; i < barConfig.ShortcutList.Count; i++)
            {
                ImGui.PushID(i);

                DrawShortcut(i, barConfig.ShortcutList, barConfig.ButtonWidth * globalSize * barConfig.Scale, () =>
                {
                    ItemClicked(barConfig.ShortcutList[i], vertical, false);
                });

                if (!vertical && i != barConfig.ShortcutList.Count - 1)
                    ImGui.SameLine();

                ImGui.PopID();
            }
        }

        private void DrawShortcut(int i, List<Shortcut> shortcuts, float width, Action callback)
        {
            var inCategory = (shortcuts != barConfig.ShortcutList);
            var sh = shortcuts[i];
            var name = sh.Name;
            var type = sh.Type;

            var useIcon = ParseName(ref name, out string tooltip, out int icon, out string args);

            if (inCategory)
            {
                if (useIcon || !barConfig.NoCategoryBackgrounds)
                    ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
                else
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.08f, 0.08f, 0.08f, 0.94f));
            }

            var height = ImGui.GetFontSize() + Style.FramePadding.Y * 2;
            var clicked = false;

            var c = sh.IconTint;
            if (c.W > 1)
                c = AnimateColor(c);

            PushFontScale(GetFontScale() * (!inCategory ? barConfig.FontScale : barConfig.CategoryFontScale));
            if (type == Shortcut.ShortcutType.Spacer)
            {
                if (useIcon)
                    DrawIconButton(icon, new Vector2(height), sh.IconZoom, sh.IconOffset, c, args, true, true);
                else
                {
                    var textSize = ImGui.CalcTextSize(name);
                    ImGui.BeginChild((uint)i, new Vector2((width == 0) ? (textSize.X + Style.FramePadding.X * 2) : width, height));
                    // What the fuck ImGui
                    if (inCategory)
                        ImGui.SetWindowFontScale(GetFontScale());
                    else
                        ImGui.SetWindowFontScale(1);
                    ImGui.SameLine((ImGui.GetContentRegionAvail().X - textSize.X) / 2);
                    ImGui.SetCursorPosY((ImGui.GetContentRegionAvail().Y - textSize.Y) / 2);
                    ImGui.TextColored(c, name);
                    ImGui.EndChild();
                }
            }
            else if (useIcon)
                clicked = DrawIconButton(icon, new Vector2(height), sh.IconZoom, sh.IconOffset, c, args);
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, c);
                clicked = ImGui.Button(name, new Vector2(width, height));
                ImGui.PopStyleColor();
            }
            PopFontScale();

            if (!inCategory && _maxW < ImGui.GetItemRectSize().X)
                _maxW = ImGui.GetItemRectSize().X;

            if (inCategory)
                ImGui.PopStyleColor();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                var onHover = (!inCategory ? barConfig.OpenCategoriesOnHover : barConfig.OpenSubcategoriesOnHover) && type == Shortcut.ShortcutType.Category;
                if (ImGui.IsMouseReleased(0) || (onHover && (!docked || barPos == revealPos) && !ImGui.IsPopupOpen("editItem")))
                    clicked = true;

                if (!string.IsNullOrEmpty(tooltip))
                    ImGui.SetTooltip(tooltip);
            }

            if (clicked)
                callback();

            ImGui.OpenPopupOnItemClick("editItem", 1);

            if (type == Shortcut.ShortcutType.Category)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, barConfig.CategorySpacing);
                PushFontScale(barConfig.CategoryScale);
                CategoryPopup(sh);
                PopFontScale();
                ImGui.PopStyleVar();
            }

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, _defaultSpacing);
            PushFontScale(1);
            ItemConfigPopup(shortcuts, i, useIcon);
            PopFontScale();
            ImGui.PopStyleVar();
        }

        private void DrawAddButton()
        {
            if (!vertical && barConfig.ShortcutList.Count > 0)
                ImGui.SameLine();

            var height = ImGui.GetFontSize() + Style.FramePadding.Y * 2;
            PushFontScale(GetFontScale() * barConfig.FontScale);
            ImGui.Button("+", new Vector2(barConfig.ButtonWidth * globalSize * barConfig.Scale, height));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                ImGui.SetTooltip("Add a new shortcut.\nRight click this (or the bar background) for options.\nRight click other shortcuts to edit them.");
            PopFontScale();

            if (_maxW < ImGui.GetItemRectSize().X)
                _maxW = ImGui.GetItemRectSize().X;

            ImGui.OpenPopupOnItemClick("addItem", 0);
            ImGui.OpenPopupOnItemClick($"BarConfig##{barNumber}", 1);

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, _defaultSpacing);
            PushFontScale(1);
            ItemCreatePopup(barConfig.ShortcutList);
            PopFontScale();
            ImGui.PopStyleVar();
        }

        private void ItemClicked(Shortcut sh, bool v, bool subItem)
        {
            Reveal();

            var type = sh.Type;
            var command = sh.Command;

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
                    SetupCategoryPosition(v, subItem);
                    ImGui.OpenPopup("ShortcutCategory");
                    break;
                default:
                    break;
            }
        }

        private void SetupCategoryPosition(bool v, bool subItem)
        {
            /*var align = 0; // Align to button (possible user option later)
            var _pos = align switch
            {
                0 => ImGui.GetItemRectMin() + (ImGui.GetItemRectSize() / 2),
                1 => ImGui.GetWindowPos() + (ImGui.GetWindowSize() / 2),
                2 => mousePos,
                _ => Vector2.Zero,
            };
            var _offset = align switch
            {
                2 => 6.0f * globalSize,
                //_ => (!vertical && !subItem) ? (ImGui.GetWindowHeight() / 2 - Style.WindowPadding.Y) : (ImGui.GetWindowWidth() / 2 - Style.WindowPadding.X),
                _ => (!vertical && !subItem) ? (ImGui.GetWindowHeight() / 2 - Style.WindowPadding.Y) : (ImGui.GetWindowWidth() / 2 - Style.WindowPadding.X),
            };*/
            var _pos = ImGui.GetItemRectMin() + (ImGui.GetItemRectSize() / 2);
            if (!subItem)
                _maincatpos = _pos; // Forces all subcategories to position based on the original category
            var _piv = Vector2.Zero;

            if (!v)
            {
                _piv.X = 0.5f;
                if (_maincatpos.Y < window.Y / 2)
                {
                    _piv.Y = 0.0f;
                    //_y += _offset;
                    _pos.Y = ImGui.GetWindowPos().Y + ImGui.GetWindowHeight() - Style.WindowPadding.Y / 2;
                }
                else
                {
                    _piv.Y = 1.0f;
                    //_y -= _offset;
                    _pos.Y = ImGui.GetWindowPos().Y + Style.WindowPadding.Y / 2;
                }
            }
            else
            {
                _piv.Y = 0.5f;
                if (_maincatpos.X < window.X / 2)
                {
                    _piv.X = 0.0f;
                    //_x += _offset;
                    _pos.X = ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - Style.WindowPadding.X / 2;
                }
                else
                {
                    _piv.X = 1.0f;
                    //_x -= _offset;
                    _pos.X = ImGui.GetWindowPos().X + Style.WindowPadding.X / 2;
                }
            }
            _catpiv = _piv;
            _catpos = _pos;
        }

        private void CategoryPopup(Shortcut sh)
        {
            ImGui.SetNextWindowPos(_catpos, ImGuiCond.Appearing, _catpiv);
            if (ImGui.BeginPopup("ShortcutCategory", (barConfig.NoCategoryBackgrounds ? ImGuiWindowFlags.NoBackground : ImGuiWindowFlags.None) | ImGuiWindowFlags.NoMove))
            {
                Reveal();

                var sublist = sh.SubList;
                var cols = Math.Max(sh.CategoryColumns, 1);
                var width = sh.CategoryWidth * globalSize * barConfig.CategoryScale;

                for (int j = 0; j < sublist.Count; j++)
                {
                    ImGui.PushID(j);

                    DrawShortcut(j, sublist, width, () =>
                    {
                        var _sh = sublist[j];

                        ItemClicked(_sh, sublist.Count >= (cols * (cols - 1) + 1), true);
                        if (!sh.CategoryStaysOpen && _sh.Type != Shortcut.ShortcutType.Category)
                            ImGui.CloseCurrentPopup();
                    });

                    if (j % cols != cols - 1)
                        ImGui.SameLine();

                    ImGui.PopID();
                }

                if (!sh.HideAdd)
                {
                    if (!barConfig.NoCategoryBackgrounds)
                        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
                    else
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.08f, 0.08f, 0.08f, 0.94f));
                    var height = ImGui.GetFontSize() + Style.FramePadding.Y * 2;
                    PushFontScale(GetFontScale() * barConfig.CategoryFontScale);
                    if (ImGui.Button("+", new Vector2(width, height)))
                        ImGui.OpenPopup("addItem");
                    PopFontScale();
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Add a new shortcut.");
                }

                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, _defaultSpacing);
                PushFontScale(1);
                ItemCreatePopup(sublist);
                PopFontScale();
                ImGui.PopStyleVar();

                ClampWindowPos();

                ImGui.EndPopup();
            }
        }

        private void ItemBaseUI(Shortcut sh, bool editing)
        {
            if (plugin.ui.iconBrowserOpen && plugin.ui.doPasteIcon)
            {
                var split = sh.Name.Split(new[] { "##" }, 2, StringSplitOptions.None);
                sh.Name = $"::{plugin.ui.pasteIcon}" + (split.Length > 1 ? $"##{split[1]}" : "");
                config.Save();
                plugin.ui.doPasteIcon = false;
            }
            if (ImGui.InputText("Name          ", ref sh.Name, 256) && editing) // Not a bug... just ImGui not extending the window to fit multiline's name...
                config.Save();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Start the name with ::x where x is a number to use icons, i.e. \"::2914\".\n" +
                    "Use ## anywhere in the name to make the text afterwards into a tooltip,\ni.e. \"Name##This is a Tooltip\".");

            var _t = (int)sh.Type;
            if (ImGui.Combo("Type", ref _t, "Single\0Multiline\0Category\0Spacer"))
            {
                sh.Type = (Shortcut.ShortcutType)_t;
                if (sh.Type == Shortcut.ShortcutType.Category)
                    sh.SubList ??= new List<Shortcut>();

                if (sh.Type == Shortcut.ShortcutType.Single)
                    sh.Command = sh.Command.Split('\n')[0];
                else if (sh.Type != Shortcut.ShortcutType.Multiline)
                    sh.Command = string.Empty;

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
                Reveal();

                _sh ??= new Shortcut();
                ItemBaseUI(_sh, false);

                if (ImGui.Button("Create"))
                {
                    shortcuts.Add(_sh);
                    config.Save();
                    _sh = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Import"))
                {
                    try
                    {
                        shortcuts.Add(plugin.ImportShortcut(ImGui.GetClipboardText()));
                        config.Save();
                        ImGui.CloseCurrentPopup();
                    }
                    catch (Exception e) // Try as a bar instead
                    {
                        try
                        {
                            var bar = plugin.ImportBar(ImGui.GetClipboardText());
                            foreach (var sh in bar.ShortcutList)
                                shortcuts.Add(sh);
                            config.Save();
                            ImGui.CloseCurrentPopup();
                        }
                        catch (Exception e2)
                        {
                            PluginLog.LogError("Invalid import string!");
                            PluginLog.LogError($"{e.GetType()}\n{e.Message}");
                            PluginLog.LogError($"{e2.GetType()}\n{e2.Message}");
                        }
                    }
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Import a shortcut from the clipboard,\n" +
                        "or import all of another bar's shortcuts.");

                ClampWindowPos();

                ImGui.EndPopup();
            }
        }

        private void ItemConfigPopup(List<Shortcut> shortcuts, int i, bool hasIcon)
        {
            if (ImGui.BeginPopup("editItem"))
            {
                Reveal();

                var sh = shortcuts[i];
                if (ImGui.BeginTabBar("Config Tabs", ImGuiTabBarFlags.NoTooltip))
                {
                    if (ImGui.BeginTabItem("Shortcut"))
                    {
                        ItemBaseUI(sh, true);

                        if (sh.Type == Shortcut.ShortcutType.Category)
                        {
                            if (ImGui.Checkbox("Hide + Button", ref sh.HideAdd))
                                config.Save();
                            ImGui.SameLine(ImGui.GetWindowWidth() / 2);
                            if (ImGui.Checkbox("Stay Open on Selection", ref sh.CategoryStaysOpen))
                                config.Save();
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Keeps the category open when pressing shortcuts within it.\nMay not work if the shortcut interacts with other plugins.");

                            if (ImGui.SliderInt("Category Width", ref sh.CategoryWidth, 0, 200))
                                config.Save();
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Set to 0 to use text width.");

                            if (ImGui.SliderInt("Columns", ref sh.CategoryColumns, 1, 12))
                                config.Save();
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Number of shortcuts in each row before starting another.");
                        }

                        if (ImGui.ColorEdit4("Color", ref sh.IconTint, ImGuiColorEditFlags.NoDragDrop | ImGuiColorEditFlags.AlphaPreviewHalf))
                            config.Save();

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
                            for (var k = 0; k < 160; k++)
                            {
                                if (16 <= k && k <= 18) continue;

                                if (keysDown[k] && ImGui.GetIO().KeysDownDuration[k] == 0)
                                {
                                    key |= k;
                                    sh.Hotkey = key;
                                    config.Save();
                                    break;
                                }
                            }
                        }
                        if (ImGui.IsItemDeactivated() && ImGui.GetIO().KeysDown[(int)Keys.Escape])
                        {
                            sh.Hotkey = 0;
                            config.Save();
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Press escape to clear the hotkey.");

                        ImGui.EndTabItem();
                    }

                    if (hasIcon && ImGui.BeginTabItem("Icon"))
                    {
                        // Name is available here for ease of access since it pertains to the icon as well
                        if (plugin.ui.iconBrowserOpen && plugin.ui.doPasteIcon)
                        {
                            var split = sh.Name.Split(new[] { "##" }, 2, StringSplitOptions.None);
                            sh.Name = $"::{plugin.ui.pasteIcon}" + (split.Length > 1 ? $"##{split[1]}" : "");
                            config.Save();
                            plugin.ui.doPasteIcon = false;
                        }
                        if (ImGui.InputText("Name", ref sh.Name, 256))
                            config.Save();
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Icons accept arguments between \"::\" and their ID. I.e. \"::f21\".\n" +
                                "\t' f ' - Applies the hotbar frame (or removes it if applied globally).\n" +
                                "\t' _ ' - Disables arguments, including implicit ones. Cannot be used with others.");

                        if (ImGui.DragFloat("Zoom", ref sh.IconZoom, 0.005f, 1.0f, 5.0f, "%.2f"))
                            config.Save();

                        if (ImGui.DragFloat2("Offset", ref sh.IconOffset, 0.002f, -0.5f, 0.5f, "%.2f"))
                            config.Save();

                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }

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
                if (ImGui.Button("Export"))
                    ImGui.SetClipboardText(plugin.ExportShortcut(sh, false));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Export to clipboard with minimal settings (May change with updates).\n" +
                        "Right click to export with every setting (Longer string, doesn't change).");

                    if (ImGui.IsMouseReleased(1))
                        ImGui.SetClipboardText(plugin.ExportShortcut(sh, true));
                }
                ImGui.SameLine();
                if (ImGui.Button(config.ExportOnDelete ? "Cut" : "Delete"))
                    plugin.ExecuteCommand("/echo <se> Right click to delete!");
                //if (ImGui.IsItemClicked(1)) // Jesus christ I hate ImGui who made this function activate on PRESS AND NOT RELEASE??? THIS ISN'T A CLICK
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Right click this button to delete the shortcut!" +
                        (config.ExportOnDelete ? "\nThe shortcut will be exported to clipboard first." : ""));

                    if (ImGui.IsMouseReleased(1))
                    {
                        if (config.ExportOnDelete)
                            ImGui.SetClipboardText(plugin.ExportShortcut(sh, false));

                        shortcuts.RemoveAt(i);
                        config.Save();
                        ImGui.CloseCurrentPopup();
                    }
                }

                var iconSize = ImGui.GetFontSize() + Style.FramePadding.Y * 2;
                ImGui.SameLine(ImGui.GetWindowContentRegionWidth() + Style.WindowPadding.X - iconSize);
                if (DrawIconButton(46, new Vector2(iconSize), 1.0f, Vector2.Zero, Vector4.One))
                    plugin.ToggleIconBrowser();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Opens up a list of all icons you can use instead of text.\n" +
                        "Warning: This will load EVERY icon available so it will probably lag for a moment.\n" +
                        "Clicking on one will copy text to be pasted into the \"Name\" field of a shortcut.\n" +
                        "Additionally, while the browser is open it will autofill the \"Name\" of shortcuts.");

                ClampWindowPos();

                ImGui.EndPopup();
            }
        }

        public void BarConfigPopup()
        {
            if (ImGui.BeginPopup($"BarConfig##{barNumber}"))
            {
                Reveal();

                if (ImGui.BeginTabBar("Config Tabs", ImGuiTabBarFlags.NoTooltip))
                {
                    if (ImGui.BeginTabItem("General"))
                    {
                        if (ImGui.InputText("Title", ref barConfig.Title, 256))
                            config.Save();

                        var _dock = (int)barConfig.DockSide;
                        if (ImGui.Combo("Side", ref _dock, "Top\0Left\0Bottom\0Right\0Undocked\0Undocked (Vertical)"))
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
                            ImGui.Text("Alignment");
                            ImGui.Indent();
                            ImGui.RadioButton(vertical ? "Top" : "Left", ref _align, 0);
                            ImGui.SameLine(ImGui.GetWindowWidth() / 3);
                            ImGui.RadioButton("Center", ref _align, 1);
                            ImGui.SameLine(ImGui.GetWindowWidth() / 3 * 2);
                            ImGui.RadioButton(vertical ? "Bottom" : "Right", ref _align, 2);
                            ImGui.Unindent();
                            if (_align != (int)barConfig.Alignment)
                            {
                                barConfig.Alignment = (BarAlign)_align;
                                config.Save();
                                SetupPosition();
                            }

                            var _visibility = (int)barConfig.Visibility;
                            ImGui.Text("Animation");
                            ImGui.Indent();
                            ImGui.RadioButton("Slide", ref _visibility, 0);
                            ImGui.SameLine(ImGui.GetWindowWidth() / 3);
                            ImGui.RadioButton("Immediate", ref _visibility, 1);
                            ImGui.SameLine(ImGui.GetWindowWidth() / 3 * 2);
                            ImGui.RadioButton("Always Visible", ref _visibility, 2);
                            ImGui.Unindent();
                            if (_visibility != (int)barConfig.Visibility)
                            {
                                barConfig.Visibility = (VisibilityMode)_visibility;
                                config.Save();
                            }

                            if ((barConfig.Visibility != VisibilityMode.Always) && ImGui.DragFloat("Reveal Area Scale", ref barConfig.RevealAreaScale, 0.01f, 0.0f, 1.0f, "%.2f"))
                                config.Save();

                            if (ImGui.DragFloat2("Offset", ref barConfig.Offset, 0.2f, -300, 300, "%.f"))
                            {
                                config.Save();
                                SetupPosition();
                            }

                            if ((barConfig.Visibility != VisibilityMode.Always) && ImGui.Checkbox("Hint", ref barConfig.Hint))
                                config.Save();
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Will prevent the bar from sleeping, increasing CPU load.");
                        }
                        else
                        {
                            var _visibility = (int)barConfig.Visibility;
                            ImGui.Text("Animation");
                            ImGui.Indent();
                            ImGui.RadioButton("Immediate", ref _visibility, 1);
                            ImGui.SameLine(ImGui.GetWindowWidth() / 2);
                            ImGui.RadioButton("Always Visible", ref _visibility, 2);
                            ImGui.Unindent();
                            if (_visibility != (int)barConfig.Visibility)
                            {
                                barConfig.Visibility = (VisibilityMode)_visibility;
                                config.Save();
                            }

                            if (ImGui.Checkbox("Lock Position", ref barConfig.LockedPosition))
                                config.Save();

                            if (!barConfig.LockedPosition && ImGui.DragFloat2("Position", ref barConfig.Position, 1, -Style.WindowPadding.X, (window.X > window.Y) ? window.X : window.Y, "%.f"))
                            {
                                config.Save();
                                _setPos = true;
                            }
                        }

                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Style"))
                    {
                        if (ImGui.DragFloat("Scale", ref barConfig.Scale, 0.002f, 0.7f, 2.0f, "%.2f"))
                            config.Save();

                        if (ImGui.SliderInt("Button Width", ref barConfig.ButtonWidth, 0, 200))
                            config.Save();
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Set to 0 to use text width.");

                        if (ImGui.DragFloat("Font Scale", ref barConfig.FontScale, 0.0018f, 0.5f, 1.0f, "%.2f"))
                            config.Save();

                        if (ImGui.SliderInt("Spacing", ref barConfig.Spacing, 0, 32))
                            config.Save();

                        if (ImGui.Checkbox("No Background", ref barConfig.NoBackground))
                            config.Save();

                        if (barConfig.ShortcutList.Count > 0)
                        {
                            ImGui.SameLine(ImGui.GetWindowWidth() / 2);
                            if (ImGui.Checkbox("Hide + Button", ref barConfig.HideAdd))
                            {
                                if (barConfig.HideAdd)
                                    plugin.ExecuteCommand("/echo <se> You can right click on the bar itself (the black background) to reopen this settings menu!");
                                config.Save();
                            }
                        }

                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Categories"))
                    {
                        if (ImGui.DragFloat("Scale", ref barConfig.CategoryScale, 0.002f, 0.7f, 1.5f, "%.2f"))
                            config.Save();

                        if (ImGui.DragFloat("Font Scale", ref barConfig.CategoryFontScale, 0.0018f, 0.5f, 1.0f, "%.2f"))
                            config.Save();

                        if (ImGui.DragFloat2("Spacing", ref barConfig.CategorySpacing, 0.12f, 0, 32, "%.f"))
                            config.Save();

                        if (ImGui.Checkbox("Open on Hover", ref barConfig.OpenCategoriesOnHover))
                            config.Save();
                        ImGui.SameLine(ImGui.GetWindowWidth() / 2);
                        if (ImGui.Checkbox("Open Subcategories on Hover", ref barConfig.OpenSubcategoriesOnHover))
                            config.Save();

                        if (ImGui.Checkbox("No Backgrounds", ref barConfig.NoCategoryBackgrounds))
                            config.Save();

                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }

                ImGui.Spacing();
                ImGui.Spacing();
                if (ImGui.Button("Export"))
                    ImGui.SetClipboardText(plugin.ExportBar(barConfig, false));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Export to clipboard with minimal settings (May change with updates).\n" +
                        "Right click to export with every setting (Longer string, doesn't change).");

                    if (ImGui.IsMouseReleased(1))
                        ImGui.SetClipboardText(plugin.ExportBar(barConfig, true));
                }
                ImGui.SameLine();
                if (ImGui.Button("QoL Bar Config"))
                    plugin.ToggleConfig();

                ClampWindowPos();

                ImGui.EndPopup();
            }
        }

        private void SetBarSize()
        {
            barSize.Y = ImGui.GetCursorPosY() + Style.WindowPadding.Y - Style.ItemSpacing.Y;
            if (!vertical)
            {
                ImGui.SameLine();
                barSize.X = ImGui.GetCursorPosX() + Style.WindowPadding.X - Style.ItemSpacing.X;
            }
            else
            {
                barSize.X = _maxW + (Style.WindowPadding.X * 2);
                _maxW = 0;
            }
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
                barPos = _reveal ? revealPos : GetHidePosition();
        }

        private void TweenBarPosition()
        {
            var _hidePos = GetHidePosition();

            if (_reveal != _lastReveal)
            {
                _lastReveal = _reveal;
                _tweenStart = barPos;
                _tweenProgress = 0;
            }

            if (_tweenProgress >= 1)
            {
                barPos = _reveal ? revealPos : _hidePos;
            }
            else
            {
                var dt = ImGui.GetIO().DeltaTime * 2;
                _tweenProgress = Math.Min(_tweenProgress + dt, 1);

                var x = -1 * ((float)Math.Pow(_tweenProgress - 1, 4) - 1); // Quartic ease out
                var deltaX = ((_reveal ? revealPos.X : _hidePos.X) - _tweenStart.X) * x;
                var deltaY = ((_reveal ? revealPos.Y : _hidePos.Y) - _tweenStart.Y) * x;

                barPos.X = _tweenStart.X + deltaX;
                barPos.Y = _tweenStart.Y + deltaY;
            }
        }

        private ImGuiScene.TextureWrap _buttonshine;
        private Vector2 _uvMin, _uvMax, _uvMinHover, _uvMaxHover;//, _uvMinHover2, _uvMaxHover2;
        public bool DrawIconButton(int icon, Vector2 size, float zoom, Vector2 offset, Vector4 tint, string args = "_", bool retExists = false, bool noButton = false)
        {
            bool ret = false;
            var texd = plugin.textureDictionary;
            var tex = texd[icon];
            if (tex == null)
            {
                if (!retExists)
                {
                    if (icon == 66001)
                        ret = ImGui.Button("  X  ##FailedTexture");
                    else
                        ret = DrawIconButton(66001, size, zoom, offset, tint, args);
                }
            }
            else
            {
                var frameArg = false;
                if (args != "_")
                {
                    frameArg = args.Contains("f");
                    if (config.UseIconFrame)
                        frameArg = !frameArg;
                }

                ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);

                if (frameArg)
                {
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
                }

                var z = 0.5f / zoom;
                var uv0 = new Vector2(0.5f - z + offset.X, 0.5f - z + offset.Y);
                var uv1 = new Vector2(0.5f + z + offset.X, 0.5f + z + offset.Y);
                if (!noButton)
                    ret = ImGui.ImageButton(tex.ImGuiHandle, size, uv0, uv1, 0, Vector4.Zero, tint);
                else
                    ImGui.Image(tex.ImGuiHandle, size, uv0, uv1, tint);

                if (frameArg && texd[QoLBar.FrameIconID] != null)
                {
                    if (_buttonshine == null)
                    {
                        _buttonshine = texd[QoLBar.FrameIconID];
                        _uvMin = new Vector2(1f / _buttonshine.Width, 0f / _buttonshine.Height);
                        _uvMax = new Vector2(47f / _buttonshine.Width, 46f / _buttonshine.Height);
                        _uvMinHover = new Vector2(49f / _buttonshine.Width, 97f / _buttonshine.Height);
                        _uvMaxHover = new Vector2(95f / _buttonshine.Width, 143f / _buttonshine.Height);
                        //_uvMinHover2 = new Vector2(248f / _buttonshine.Width, 8f / _buttonshine.Height);
                        //_uvMaxHover2 = new Vector2(304f / _buttonshine.Width, 64f / _buttonshine.Height);
                    }
                    var _sizeInc = size * 0.075f;
                    var _rMin = ImGui.GetItemRectMin() - _sizeInc;
                    var _rMax = ImGui.GetItemRectMax() + _sizeInc;
                    ImGui.GetWindowDrawList().AddImage(_buttonshine.ImGuiHandle, _rMin, _rMax, _uvMin, _uvMax); // Frame
                    if (!noButton && ImGui.IsItemHovered(ImGuiHoveredFlags.RectOnly))
                    {
                        ImGui.GetWindowDrawList().AddImage(_buttonshine.ImGuiHandle, _rMin, _rMax, _uvMinHover, _uvMaxHover, 0x85FFFFFF); // Frame Center Glow
                        //ImGui.GetWindowDrawList().AddImage(_buttonshine.ImGuiHandle, _rMin - (_sizeInc * 1.5f), _rMax + (_sizeInc * 1.5f), _uvMinHover2, _uvMaxHover2); // Edge glow // TODO: Probably somewhat impossible as is, but fix glow being clipped
                    }
                    // TODO: Find a way to do the click animation

                    ImGui.PopStyleColor(2);
                }

                ImGui.PopStyleColor();
                if (retExists)
                    ret = true;
            }
            return ret;
        }

        private Vector4 AnimateColor(Vector4 c)
        {
            float r, g, b, a, x;
            r = g = b = a = 1;
            var t = plugin.GetDrawTime();
            var anim = Math.Round(c.W * 255) - 256;

            switch (anim)
            {
                case 0: // Slow Rainbow
                    ImGui.ColorConvertHSVtoRGB(((t * 15) % 360) / 360, 1, 1, out r, out g, out b);
                    break;
                case 1: // Rainbow
                    ImGui.ColorConvertHSVtoRGB(((t * 30) % 360) / 360, 1, 1, out r, out g, out b);
                    break;
                case 2: // Fast Rainbow
                    ImGui.ColorConvertHSVtoRGB(((t * 60) % 360) / 360, 1, 1, out r, out g, out b);
                    break;
                case 3: // Slow Fade
                    r = c.X; g = c.Y; b = c.Z;
                    a = (float)(Math.Sin(((t * 30) % 360) * Math.PI / 180) + 1) / 2;
                    break;
                case 4: // Fade
                    r = c.X; g = c.Y; b = c.Z;
                    a = (float)(Math.Sin(((t * 60) % 360) * Math.PI / 180) + 1) / 2;
                    break;
                case 5: // Fast Fade
                    r = c.X; g = c.Y; b = c.Z;
                    a = (float)(Math.Sin(((t * 120) % 360) * Math.PI / 180) + 1) / 2;
                    break;
                case 6: // Red Transition
                    x = Math.Abs(((t * 60) % 360) - 180) / 180;
                    r = c.X + (1 - c.X) * x;
                    g = c.Y + (0 - c.Y) * x;
                    b = c.Z + (0 - c.Z) * x;
                    break;
                case 7: // Yellow Transition
                    x = Math.Abs(((t * 60) % 360) - 180) / 180;
                    r = c.X + (1 - c.X) * x;
                    g = c.Y + (1 - c.Y) * x;
                    b = c.Z + (0 - c.Z) * x;
                    break;
                case 8: // Green Transition
                    x = Math.Abs(((t * 60) % 360) - 180) / 180;
                    r = c.X + (0 - c.X) * x;
                    g = c.Y + (1 - c.Y) * x;
                    b = c.Z + (0 - c.Z) * x;
                    break;
                case 9: // Cyan Transition
                    x = Math.Abs(((t * 60) % 360) - 180) / 180;
                    r = c.X + (0 - c.X) * x;
                    g = c.Y + (1 - c.Y) * x;
                    b = c.Z + (1 - c.Z) * x;
                    break;
                case 10: // Blue Transition
                    x = Math.Abs(((t * 60) % 360) - 180) / 180;
                    r = c.X + (0 - c.X) * x;
                    g = c.Y + (0 - c.Y) * x;
                    b = c.Z + (1 - c.Z) * x;
                    break;
                case 11: // Purple Transition
                    x = Math.Abs(((t * 60) % 360) - 180) / 180;
                    r = c.X + (1 - c.X) * x;
                    g = c.Y + (0 - c.Y) * x;
                    b = c.Z + (1 - c.Z) * x;
                    break;
                case 12: // White Transition
                    x = Math.Abs(((t * 60) % 360) - 180) / 180;
                    r = c.X + (1 - c.X) * x;
                    g = c.Y + (1 - c.Y) * x;
                    b = c.Z + (1 - c.Z) * x;
                    break;
                case 13: // Black Transition
                    x = Math.Abs(((t * 60) % 360) - 180) / 180;
                    r = c.X + (0 - c.X) * x;
                    g = c.Y + (0 - c.Y) * x;
                    b = c.Z + (0 - c.Z) * x;
                    break;
            }

            return new Vector4(r, g, b, a);
        }

        // Why is this not a basic feature of ImGui...
        private readonly Stack<float> _fontScaleStack = new Stack<float>();
        private float _curScale = 1;
        private void PushFontScale(float scale)
        {
            _fontScaleStack.Push(_curScale);
            _curScale = scale;
            ImGui.SetWindowFontScale(_curScale);
        }

        private void PopFontScale()
        {
            _curScale = _fontScaleStack.Pop();
            ImGui.SetWindowFontScale(_curScale);
        }

        private float GetFontScale() => _curScale;

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
        private string GetKeyName(int k)
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

        public void Dispose()
        {
        }
    }
}
