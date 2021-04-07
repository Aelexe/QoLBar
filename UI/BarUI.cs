using System;
using System.Numerics;
using System.Collections.Generic;
using ImGuiNET;
using Dalamud.Interface;
using static QoLBar.BarCfg;

namespace QoLBar
{
    public class BarUI : IDisposable
    {
        private int _id;
        public int ID
        {
            get => _id;
            set
            {
                _id = value;
                Config = QoLBar.Config.BarCfgs[value];
                SetupPivot();
            }
        }
        public BarCfg Config { get; private set; }

        public bool IsVisible => !IsHidden && CheckConditionSet();
        public bool IsHidden
        {
            get => Config.Hidden;
            set
            {
                Config.Hidden = value;
                QoLBar.Config.Save();
            }
        }

        private static ImGuiStylePtr Style => ImGui.GetStyle();

        private Vector2 VectorPosition => IsDocked
            ? new Vector2((float)Math.Floor(Config.Position[0] * window.X), (float)Math.Floor(Config.Position[1] * window.Y)) //+ ImGuiHelpers.MainViewport.Pos
            : new Vector2((float)Math.Floor(Config.Position[0] * monitor.X), (float)Math.Floor(Config.Position[1] * monitor.Y));

        public bool IsVertical => (Config.Columns > 0) && children.Count >= (Config.Columns * (Config.Columns - 1) + 1);
        public bool IsDocked { get; private set; } = true;
        public bool IsDragging { get; private set; } = false;

        public List<ShortcutUI> children = new List<ShortcutUI>();
        public static ShCfg tempSh;

        private Vector2 window = ImGuiHelpers.MainViewport.Size;
        private Vector2 monitor = ImGui.GetPlatformIO().Monitors[0].MainSize;
        public Vector2 UsableArea => IsDocked ? window : monitor;

        private static Vector2 mousePos = ImGui.GetIO().MousePos;
        private static float globalSize = ImGuiHelpers.GlobalScale;
        private Vector2 barSize = new Vector2(200, 38);
        private Vector2 barPos;
        private ImGuiWindowFlags flags;
        private Vector2 piv = Vector2.Zero;
        private Vector2 hidePos = Vector2.Zero;
        private Vector2 revealPos = Vector2.Zero;

        private bool _reveal = false;
        public void Reveal() => _reveal = true;
        public void ForceReveal() => _lastReveal = _reveal = true;
        public void Hide() => _reveal = false;
        public bool IsFullyRevealed => !IsDocked || barPos == revealPos;

        private float _maxW = 0;
        public float MaxWidth
        {
            get => _maxW;
            set
            {
                if (_maxW < value || value == 0)
                    _maxW = value;
            }
        }

        private float _maxH = 0;
        public float MaxHeight
        {
            get => _maxH;
            set
            {
                if (_maxH < value || value == 0)
                    _maxH = value;
            }
        }

        private bool _activated = false;
        public bool WasActivated
        {
            get => _activated;
            set
            {
                if (!value)
                    ClearActivated();
                _activated = value;
            }
        }

        private bool _firstframe = true;
        private bool _setPos = true;
        private bool _lastReveal = true;
        private bool _mouseRevealed = false;
        private Vector2 _tweenStart;
        private float _tweenProgress = 1;
        private Vector2 _catpiv = Vector2.Zero;
        private Vector2 _catpos = Vector2.Zero;
        private Vector2 _maincatpos = Vector2.Zero;

        public BarUI(int n)
        {
            ID = n;

            for (int i = 0; i < Config.ShortcutList.Count; i++)
                children.Add(new ShortcutUI(this));
        }

        private bool CheckConditionSet()
        {
            if (Config.ConditionSet >= 0 && Config.ConditionSet < QoLBar.Config.ConditionSets.Count)
                return QoLBar.Config.ConditionSets[Config.ConditionSet].CheckConditions();
            else
                return true;
        }

        private void SetupPivot()
        {
            var alignPiv = 0.0f;

            switch (Config.Alignment)
            {
                case BarAlign.LeftOrTop:
                    alignPiv = 0.0f;
                    break;
                case BarAlign.Center:
                    alignPiv = 0.5f;
                    break;
                case BarAlign.RightOrBottom:
                    alignPiv = 1.0f;
                    break;
            }

            switch (Config.DockSide)
            {
                case BarDock.Top: //    0.0 1.0, 0.5 1.0, 1.0 1.0 // 0 0(+H),    winX/2 0(+H),    winX 0(+H)
                    piv.X = alignPiv;
                    piv.Y = 1.0f;
                    IsDocked = true;
                    break;
                case BarDock.Right: //  0.0 0.0, 0.0 0.5, 0.0 1.0 // winX(-W) 0, winX(-W) winY/2, winX(-W) winY
                    piv.X = 0.0f;
                    piv.Y = alignPiv;
                    IsDocked = true;
                    break;
                case BarDock.Bottom: // 0.0 0.0, 0.5 0.0, 1.0 0.0 // 0 winY(-H), winX/2 winY(-H), winX winY(-H)
                    piv.X = alignPiv;
                    piv.Y = 0.0f;
                    IsDocked = true;
                    break;
                case BarDock.Left: //   1.0 0.0, 1.0 0.5, 1.0 1.0 // 0(+W) 0,    0(+W) winY/2,    0(+W) winY
                    piv.X = 1.0f;
                    piv.Y = alignPiv;
                    IsDocked = true;
                    break;
                case BarDock.Undocked:
                    piv = Vector2.Zero;
                    IsDocked = false;
                    _setPos = true;
                    return;
            }

            SetupPositions();

            barPos = hidePos;
            _tweenStart = hidePos;
        }

        private void SetupPositions()
        {
            var pos = VectorPosition;
            switch (Config.DockSide)
            {
                case BarDock.Top:
                    hidePos.X = window.X * piv.X + pos.X;
                    hidePos.Y = 0;
                    revealPos.X = hidePos.X;
                    revealPos.Y = Math.Max(hidePos.Y + barSize.Y + pos.Y, GetHidePosition().Y + 1);
                    break;
                case BarDock.Right:
                    hidePos.X = window.X;
                    hidePos.Y = window.Y * piv.Y + pos.Y;
                    revealPos.X = Math.Min(hidePos.X - barSize.X + pos.X, GetHidePosition().X - 1);
                    revealPos.Y = hidePos.Y;
                    break;
                case BarDock.Bottom:
                    hidePos.X = window.X * piv.X + pos.X;
                    hidePos.Y = window.Y;
                    revealPos.X = hidePos.X;
                    revealPos.Y = Math.Min(hidePos.Y - barSize.Y + pos.Y, GetHidePosition().Y - 1);
                    break;
                case BarDock.Left:
                    hidePos.X = 0;
                    hidePos.Y = window.Y * piv.Y + pos.Y;
                    revealPos.X = Math.Max(hidePos.X + barSize.X + pos.X, GetHidePosition().X + 1);
                    revealPos.Y = hidePos.Y;
                    break;
            }
        }

        private void SetupImGuiFlags()
        {
            flags = ImGuiWindowFlags.None;

            flags |= ImGuiWindowFlags.NoDecoration;
            if (IsDocked || Config.LockedPosition)
                flags |= ImGuiWindowFlags.NoMove;
            flags |= ImGuiWindowFlags.NoScrollWithMouse;
            if (Config.NoBackground)
                flags |= ImGuiWindowFlags.NoBackground;
            flags |= ImGuiWindowFlags.NoSavedSettings;
            flags |= ImGuiWindowFlags.NoFocusOnAppearing;
        }

        private Vector2 GetHidePosition()
        {
            var _hidePos = hidePos;
            if (Config.Hint)
            {
                var _winPad = Style.WindowPadding * 2;

                switch (Config.DockSide)
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
                }
            }
            return _hidePos;
        }

        public void SetupHotkeys()
        {
            foreach (var ui in children)
                ui.SetupHotkeys();
        }

        private void ClearActivated()
        {
            if (!WasActivated)
            {
                foreach (var ui in children)
                    ui.ClearActivated();
            }
        }

        public void Draw()
        {
            CheckGameResolution();

            if (!IsVisible) return;

            var io = ImGui.GetIO();
            mousePos = io.MousePos;
            globalSize = ImGuiHelpers.GlobalScale;

            if (IsDocked || Config.Visibility == BarVisibility.Immediate)
            {
                SetupPositions();
                CheckMousePosition();
            }
            else
                Reveal();

            if (!IsDocked && !_firstframe && !_reveal && !_lastReveal)
            {
                ClearActivated();
                return;
            }

            if (_firstframe || _reveal || (barPos != hidePos) || (!IsDocked && _lastReveal)) // Don't bother to render when fully off screen
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(Config.Spacing[0], Config.Spacing[1]));
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.286f, 0.286f, 0.286f, 0.9f));

                if (IsDocked)
                {
                    ImGuiHelpers.ForceMainViewport();
                    ImGuiHelpers.SetNextWindowPosRelativeMainViewport(barPos, ImGuiCond.Always, piv);
                }
                else if (_setPos || Config.LockedPosition)
                {
                    if (!_firstframe)
                    {
                        ImGui.SetNextWindowPos(VectorPosition);
                        _setPos = false;
                    }
                    else
                        ImGui.SetNextWindowPos(monitor);
                }
                ImGui.SetNextWindowSize(barSize);

                SetupImGuiFlags();
                ImGui.Begin($"QoLBar##{ID}", flags);

                ImGuiEx.PushFontScale(Config.Scale);

                if (_mouseRevealed && ImGui.IsWindowHovered(ImGuiHoveredFlags.RectOnly))
                    Reveal();
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Right) && ImGui.IsWindowHovered())
                    ImGui.OpenPopup($"BarConfig##{ID}");

                DrawItems();

                DrawAddButton();

                if (!_firstframe && !Config.LockedPosition)
                {
                    if (IsDocked)
                    {
                        // I greatly dislike this
                        var dragging = !IsDragging
                            ? ImGui.IsWindowFocused() && ImGui.IsWindowHovered() && !ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0)
                            : IsDragging && !ImGui.IsMouseReleased(ImGuiMouseButton.Left);

                        // Began dragging
                        if (dragging && dragging != IsDragging)
                            IsDragging = true;

                        if (IsDragging)
                        {
                            Reveal();
                            var delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0) / UsableArea;
                            ImGui.ResetMouseDragDelta();
                            Config.Position[0] = Math.Min(Config.Position[0] + delta.X, 1);
                            Config.Position[1] = Math.Min(Config.Position[1] + delta.Y, 1);
                            SetupPivot();
                        }

                        // Stopped dragging
                        if (!dragging && dragging != IsDragging)
                        {
                            IsDragging = false;
                            QoLBar.Config.Save();
                        }
                    }
                    else
                    {
                        if (ImGui.GetWindowPos() != VectorPosition)
                        {
                            var newPos = ImGui.GetWindowPos() / UsableArea;
                            Config.Position[0] = newPos.X;
                            Config.Position[1] = newPos.Y;
                            QoLBar.Config.Save();
                        }
                    }
                }

                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, PluginUI.defaultSpacing);
                ImGuiEx.PushFontScale(1);
                BarConfigPopup();
                ImGuiEx.PopFontScale();
                ImGui.PopStyleVar();

                SetBarSize();

                ImGuiEx.PopFontScale();

                ImGui.End();

                ImGui.PopStyleColor();
                ImGui.PopStyleVar(3);
            }

            if (!_reveal)
                _mouseRevealed = false;

            if (IsDocked)
            {
                SetBarPosition();
                Hide(); // Allows other objects to reveal the bar
            }
            else
                _lastReveal = _reveal;

            WasActivated = false;

            _firstframe = false;
        }

        private void CheckGameResolution()
        {
            var resolution = ImGuiHelpers.MainViewport.Size;
            // Fix bar positions when the game is resized
            if (resolution != window)
            {
                window = resolution;
                SetupPivot();
            }
        }

        private (Vector2, Vector2) CalculateRevealPosition()
        {
            var pos = IsDocked ? revealPos : VectorPosition;
            var min = new Vector2(pos.X - (barSize.X * piv.X), pos.Y - (barSize.Y * piv.Y)) + ImGui.GetMainViewport().Pos;
            var max = new Vector2(pos.X + (barSize.X * (1 - piv.X)), pos.Y + (barSize.Y * (1 - piv.Y))) + ImGui.GetMainViewport().Pos;
            return (min, max);
        }

        private void CheckMousePosition()
        {
            if (IsDocked && _reveal)
                return;

            (var _min, var _max) = CalculateRevealPosition();

            switch (Config.DockSide)
            {
                case BarDock.Top:
                    _max.Y = Math.Max(Math.Max(_max.Y - barSize.Y * (1 - Config.RevealAreaScale), _min.Y + 1), GetHidePosition().Y + 1);
                    break;
                case BarDock.Left:
                    _max.X = Math.Max(Math.Max(_max.X - barSize.X * (1 - Config.RevealAreaScale), _min.X + 1), GetHidePosition().X + 1);
                    break;
                case BarDock.Bottom:
                    _min.Y = Math.Min(Math.Min(_min.Y + barSize.Y * (1 - Config.RevealAreaScale), _max.Y - 1), GetHidePosition().Y - 1);
                    break;
                case BarDock.Right:
                    _min.X = Math.Min(Math.Min(_min.X + barSize.X * (1 - Config.RevealAreaScale), _max.X - 1), GetHidePosition().X - 1);
                    break;
                default:
                    break;
            }

            var mX = mousePos.X;
            var mY = mousePos.Y;

            //if (ImGui.IsMouseHoveringRect(_min, _max, true)) // This only works in the context of a window... thanks ImGui
            if (Config.Visibility == BarVisibility.Always || (_min.X <= mX && mX < _max.X && _min.Y <= mY && mY < _max.Y))
            {
                _mouseRevealed = true;
                Reveal();
            }
            else
                Hide();
        }

        private void DrawItems()
        {
            var cols = Config.Columns;
            var width = Config.ButtonWidth * globalSize * Config.Scale;
            for (int i = 0; i < children.Count; i++)
            {
                var ui = children[i];
                ImGui.PushID(ui.ID);

                ui.DrawShortcut(width);

                if (cols <= 0 || i % cols != cols - 1)
                    ImGui.SameLine();

                ImGui.PopID();
            }
        }

        private void DrawAddButton()
        {
            if (Config.Editing || children.Count < 1)
            {
                var height = ImGui.GetFontSize() + Style.FramePadding.Y * 2;
                ImGuiEx.PushFontScale(ImGuiEx.GetFontScale() * Config.FontScale);
                if (ImGui.Button("+", new Vector2(Config.ButtonWidth * globalSize * Config.Scale, height)))
                    ImGui.OpenPopup("addItem");
                ImGuiEx.SetItemTooltip("Add a new shortcut.\nRight click this (or the bar background) for options.\nRight click other shortcuts to edit them.", ImGuiHoveredFlags.AllowWhenBlockedByPopup);
                ImGuiEx.PopFontScale();

                var size = ImGui.GetItemRectMax() - ImGui.GetWindowPos();
                MaxWidth = size.X;
                MaxHeight = size.Y;

                //ImGui.OpenPopupContextItem($"BarConfig##{barNumber}"); // Technically unneeded
            }

            if (ImGui.IsWindowHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right) && ImGui.GetIO().KeyShift)
                ImGui.OpenPopup("addItem");

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, PluginUI.defaultSpacing);
            ImGuiEx.PushFontScale(1);
            ItemCreatePopup();
            ImGuiEx.PopFontScale();
            ImGui.PopStyleVar();
        }

        // TODO: rewrite this, preferably insert into ShortcutUI
        public void SetupCategoryPosition(bool v, bool subItem)
        {
            Vector2 pos, wMin, wMax;
            if (!subItem)
            {
                (wMin, wMax) = CalculateRevealPosition();
                pos = wMin + ((ImGui.GetItemRectMin() + (ImGui.GetItemRectSize() / 2)) - ImGui.GetWindowPos());
                _maincatpos = pos; // Forces all subcategories to position based on the original category
            }
            else
            {
                wMin = ImGui.GetWindowPos();
                wMax = ImGui.GetWindowPos() + ImGui.GetWindowSize();
                pos = ImGui.GetItemRectMin() + (ImGui.GetItemRectSize() / 2);
            }

            var piv = Vector2.Zero;

            if (!v)
            {
                piv.X = 0.5f;
                if (_maincatpos.Y < UsableArea.Y / 2)
                {
                    piv.Y = 0.0f;
                    pos.Y = wMax.Y - Style.WindowPadding.Y / 2;
                }
                else
                {
                    piv.Y = 1.0f;
                    pos.Y = wMin.Y + Style.WindowPadding.Y / 2;
                }
            }
            else
            {
                piv.Y = 0.5f;
                if (_maincatpos.X < UsableArea.X / 2)
                {
                    piv.X = 0.0f;
                    pos.X = wMax.X - Style.WindowPadding.X / 2;
                }
                else
                {
                    piv.X = 1.0f;
                    pos.X = wMin.X + Style.WindowPadding.X / 2;
                }
            }
            _catpiv = piv;
            _catpos = pos;
        }

        public void SetCategoryPosition() => ImGui.SetNextWindowPos(_catpos, ImGuiCond.Appearing, _catpiv);

        // TODO: dupe code remove somehow
        private void ItemCreatePopup()
        {
            if (IsDocked)
                ImGuiHelpers.ForceMainViewport();

            if (ImGui.BeginPopup("addItem"))
            {
                Reveal();
                QoLBar.Plugin.ui.SetConfigPopupOpen();

                tempSh ??= new ShCfg();
                ShortcutUI.ItemBaseUI(tempSh, false);

                if (ImGui.Button("Create"))
                {
                    AddShortcut(tempSh);
                    tempSh = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Import"))
                {
                    var imports = Importing.TryImport(ImGui.GetClipboardText(), true);
                    if (imports.shortcut != null)
                        AddShortcut(imports.shortcut);
                    else if (imports.bar != null)
                    {
                        foreach (var sh in imports.bar.ShortcutList)
                            AddShortcut(sh);
                    }
                    QoLBar.Config.Save();
                    ImGui.CloseCurrentPopup();
                }
                ImGuiEx.SetItemTooltip("Import a shortcut from the clipboard,\n" +
                    "or import all of another bar's shortcuts.");

                ImGuiEx.ClampWindowPosToViewport();

                ImGui.EndPopup();
            }
        }

        public void BarConfigPopup()
        {
            if (IsDocked)
                ImGuiHelpers.ForceMainViewport();

            if (ImGui.BeginPopup($"BarConfig##{ID}"))
            {
                Reveal();

                if (ImGui.BeginTabBar("Config Tabs", ImGuiTabBarFlags.NoTooltip))
                {
                    if (ImGui.BeginTabItem("General"))
                    {
                        if (ImGui.InputText("Name", ref Config.Name, 256))
                            QoLBar.Config.Save();

                        var _dock = (int)Config.DockSide;
                        if (ImGui.Combo("Side", ref _dock, "Top\0Right\0Bottom\0Left"))
                        {
                            Config.DockSide = (BarDock)_dock;
                            if (Config.DockSide == BarDock.Undocked && Config.Visibility == BarVisibility.Slide)
                                Config.Visibility = BarVisibility.Always;
                            Config.Position[0] = 0;
                            Config.Position[1] = 0;
                            QoLBar.Config.Save();
                            SetupPivot();
                        }

                        if (IsDocked)
                        {
                            var topbottom = Config.DockSide == BarDock.Top || Config.DockSide == BarDock.Bottom;
                            var _align = (int)Config.Alignment;
                            ImGui.Text("Alignment");
                            ImGui.RadioButton(topbottom ? "Left" : "Top", ref _align, 0);
                            ImGui.SameLine(ImGui.GetWindowWidth() / 3);
                            ImGui.RadioButton("Center", ref _align, 1);
                            ImGui.SameLine(ImGui.GetWindowWidth() / 3 * 2);
                            ImGui.RadioButton(topbottom ? "Right" : "Bottom", ref _align, 2);
                            if (_align != (int)Config.Alignment)
                            {
                                Config.Alignment = (BarAlign)_align;
                                QoLBar.Config.Save();
                                SetupPivot();
                            }

                            var _visibility = (int)Config.Visibility;
                            ImGui.Text("Animation");
                            ImGui.RadioButton("Slide", ref _visibility, 0);
                            ImGui.SameLine(ImGui.GetWindowWidth() / 3);
                            ImGui.RadioButton("Immediate", ref _visibility, 1);
                            ImGui.SameLine(ImGui.GetWindowWidth() / 3 * 2);
                            ImGui.RadioButton("Always Visible", ref _visibility, 2);
                            if (_visibility != (int)Config.Visibility)
                            {
                                Config.Visibility = (BarVisibility)_visibility;
                                QoLBar.Config.Save();
                            }

                            if ((Config.Visibility != BarVisibility.Always) && ImGui.DragFloat("Reveal Area Scale", ref Config.RevealAreaScale, 0.01f, 0.0f, 1.0f, "%.2f"))
                                QoLBar.Config.Save();
                        }
                        else
                        {
                            var _visibility = (int)Config.Visibility;
                            ImGui.Text("Animation");
                            ImGui.RadioButton("Immediate", ref _visibility, 1);
                            ImGui.SameLine(ImGui.GetWindowWidth() / 2);
                            ImGui.RadioButton("Always Visible", ref _visibility, 2);
                            if (_visibility != (int)Config.Visibility)
                            {
                                Config.Visibility = (BarVisibility)_visibility;
                                QoLBar.Config.Save();
                            }
                        }

                        if (ImGui.Checkbox("Edit Mode", ref Config.Editing))
                        {
                            if (!Config.Editing)
                                QoLBar.Plugin.ExecuteCommand("/echo <se> You can right click on the bar itself (the black background) to reopen this settings menu! You can also use shift + right click to add a new shortcut as well.");
                            QoLBar.Config.Save();
                        }
                        ImGui.SameLine(ImGui.GetWindowWidth() / 2);
                        if (ImGui.Checkbox("Lock Position", ref Config.LockedPosition))
                            QoLBar.Config.Save();

                        if (!Config.LockedPosition)
                        {
                            var pos = VectorPosition;
                            var area = UsableArea;
                            var max = (area.X > area.Y) ? area.X : area.Y;
                            if (ImGui.DragFloat2(IsDocked ? "Offset" : "Position", ref pos, 1, -max, max, "%.f"))
                            {
                                Config.Position[0] = Math.Min(pos.X / area.X, 1);
                                Config.Position[1] = Math.Min(pos.Y / area.Y, 1);
                                QoLBar.Config.Save();
                                if (IsDocked)
                                    SetupPivot();
                                else
                                    _setPos = true;
                            }
                        }

                        if (IsDocked && Config.Visibility != BarVisibility.Always)
                        {
                            if (ImGui.Checkbox("Hint", ref Config.Hint))
                                QoLBar.Config.Save();
                            ImGuiEx.SetItemTooltip("Will prevent the bar from sleeping, increasing CPU load.");
                        }

                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Style"))
                    {
                        if (ImGui.SliderInt("Button Width", ref Config.ButtonWidth, 0, 200))
                            QoLBar.Config.Save();
                        ImGuiEx.SetItemTooltip("Set to 0 to use text width.");

                        if (ImGui.SliderInt("Columns", ref Config.Columns, 0, 12))
                            QoLBar.Config.Save();
                        ImGuiEx.SetItemTooltip("Number of shortcuts in each row before starting another.\n" +
                            "Set to 0 to specify infinite.");

                        if (ImGui.DragFloat("Scale", ref Config.Scale, 0.002f, 0.7f, 2.0f, "%.2f"))
                            QoLBar.Config.Save();

                        if (ImGui.DragFloat("Font Scale", ref Config.FontScale, 0.0018f, 0.5f, 1.0f, "%.2f"))
                            QoLBar.Config.Save();

                        var spacing = new Vector2(Config.Spacing[0], Config.Spacing[1]);
                        if (ImGui.DragFloat2("Spacing", ref spacing, 0.12f, 0, 32, "%.f"))
                        {
                            Config.Spacing[0] = (int)spacing.X;
                            Config.Spacing[1] = (int)spacing.Y;
                            QoLBar.Config.Save();
                        }

                        if (ImGui.Checkbox("No Background", ref Config.NoBackground))
                            QoLBar.Config.Save();

                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }

                ImGui.Spacing();
                ImGui.Spacing();
                if (ImGui.Button("Export"))
                    ImGui.SetClipboardText(Importing.ExportBar(Config, false));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Export to clipboard with minimal settings (May change with updates).\n" +
                        "Right click to export with every setting (Longer string, doesn't change).");

                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                        ImGui.SetClipboardText(Importing.ExportBar(Config, true));
                }
                ImGui.SameLine();
                if (ImGui.Button("QoL Bar Config"))
                    QoLBar.Plugin.ToggleConfig();

                ImGuiEx.ClampWindowPosToViewport();

                ImGui.EndPopup();
            }
        }

        private void SetBarSize()
        {
            var winPad = Style.WindowPadding;
            barSize.X = MaxWidth + winPad.X;
            barSize.Y = MaxHeight + winPad.Y;
            MaxWidth = 0;
            MaxHeight = 0;
        }

        private void SetBarPosition()
        {
            if (Config.Visibility == BarVisibility.Slide)
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

        public void AddShortcut(ShCfg sh)
        {
            Config.ShortcutList.Add(sh);
            children.Add(new ShortcutUI(this));
            QoLBar.Config.Save();
        }

        public void RemoveShortcut(int i)
        {
            if (QoLBar.Config.ExportOnDelete)
                ImGui.SetClipboardText(Importing.ExportShortcut(Config.ShortcutList[i], false));

            children[i].Dispose();
            children.RemoveAt(i);
            Config.ShortcutList.RemoveAt(i);
            QoLBar.Config.Save();
            RefreshShortcutIDs();
        }

        public void ShiftShortcut(int i, bool increment)
        {
            if (!increment ? i > 0 : i < (children.Count - 1))
            {
                var j = (increment ? i + 1 : i - 1);
                var ui = children[i];
                children.RemoveAt(i);
                children.Insert(j, ui);

                var sh = Config.ShortcutList[i];
                Config.ShortcutList.RemoveAt(i);
                Config.ShortcutList.Insert(j, sh);
                QoLBar.Config.Save();
                RefreshShortcutIDs();
            }
        }

        private void RefreshShortcutIDs()
        {
            for (int i = 0; i < children.Count; i++)
                children[i].ID = i;
        }

        public void Dispose()
        {
            foreach (var ui in children)
                ui.Dispose();
        }
    }
}
