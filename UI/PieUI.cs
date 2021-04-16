using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface;
using static QoLBar.ShCfg;

namespace QoLBar
{
    public static class PieUI
    {
        // No reason anyone should want or need more than this, and it starts to look horrible
        public const int maxLevels = 3;
        public const int maxItems = 6;

        public static bool enabled = false;
        private static Vector2 _prevMousePos;
        public static void Draw()
        {
            enabled = true;

            foreach (var bar in QoLBar.Plugin.ui.bars)
            {
                if (bar.Config.Hotkey > 0 && bar.CheckConditionSet())
                {
                    ImGui.PushID(bar.ID);

                    if (bar.openPie)
                        ImGui.OpenPopup("PieBar");

                    if (ImGuiPie.BeginPiePopup("PieBar", bar.openPie))
                    {
                        if (ImGuiPie.IsPieAppearing() && QoLBar.Config.PiesAlwaysCenter)
                        {
                            ImGuiPie.SetPieCenter(ImGuiHelpers.MainViewport.GetCenter());

                            if (QoLBar.Config.PiesMoveMouse)
                            {
                                var io = ImGui.GetIO();
                                _prevMousePos = io.MousePos;

                                io.WantSetMousePos = true;
                                io.MousePos = ImGuiHelpers.MainViewport.GetCenter();
                            }
                        }

                        if (!bar.Config.NoBackground)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Button, 0x70000000);
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xC0404040);
                        }
                        else
                        {
                            ImGui.PushStyleColor(ImGuiCol.Button, 0);
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0);
                        }

                        ImGuiPie.SetPieRadius(50);
                        ImGuiPie.SetPieScale(ImGuiHelpers.GlobalScale * bar.Config.Scale);
                        ImGuiPie.DisableRepositioning();

                        DrawChildren(bar.children);

                        ImGuiPie.EndPiePopup();

                        ImGui.PopStyleColor(2);

                        if (!bar.openPie && QoLBar.Config.PiesReturnMouse)
                        {
                            var io = ImGui.GetIO();
                            io.WantSetMousePos = true;
                            io.MousePos = QoLBar.Config.PiesReadjustMouse ? (_prevMousePos + (io.MousePos - ImGuiHelpers.MainViewport.GetCenter())) : _prevMousePos;
                        }
                    }

                    ImGui.PopID();
                }
            }
        }

        static int totalLevels = 0;
        public static void DrawChildren(List<ShortcutUI> children)
        {
            ++totalLevels;
            var totalItems = 0;
            foreach (var sh in children)
            {
                ImGui.PushID(sh.ID);

                if (totalLevels < maxLevels && sh.Config.Type == ShortcutType.Category && sh.Config.Mode == ShortcutMode.Default)
                {
                    var open = ImGuiPie.BeginPieMenu($"{sh.Config.Name}");
                    ImGuiPie.PieDrawOverride(DrawShortcut(sh));
                    if (open)
                    {
                        if (ImGuiPie.IsItemActivated())
                            sh.OnClick(false, false);

                        DrawChildren(sh.children);

                        ImGuiPie.EndPieMenu();
                    }

                    totalItems++;
                }
                else if (sh.Config.Type != ShortcutType.Spacer)
                {
                    if (ImGuiPie.PieMenuItem($"{sh.Config.Name}"))
                        sh.OnClick(false, false);
                    ImGuiPie.PieDrawOverride(DrawShortcut(sh));

                    totalItems++;
                }

                ImGui.PopID();

                if (totalItems >= maxItems) break;
            }
            --totalLevels;
        }

        // TODO: what the fuck
        private static ImGuiScene.TextureWrap _buttonshine;
        private static Vector2 _uvMin, _uvMax, _uvMinHover, _uvMaxHover;//, _uvMinHover2, _uvMaxHover2;
        public static Action<Vector2, bool> DrawShortcut(ShortcutUI ui)
        {
            var sh = ui.DisplayedUI.Config;
            var bar = ui.parentBar.Config;
            return (Vector2 center, bool hovered) =>
            {
                var name = sh.Name;
                var useIcon = ShortcutUI.ParseName(ref name, out string tooltip, out int icon, out string args);

                var c = ImGui.ColorConvertU32ToFloat4(sh.Color);
                c.W += sh.ColorAnimation / 255f; // Temporary
                if (c.W > 1)
                    c = ShortcutUI.AnimateColor(c);
                var color = ImGui.ColorConvertFloat4ToU32(c);

                var drawList = ImGui.GetWindowDrawList();
                if (useIcon)
                {
                    ImGui.SetWindowFontScale(bar.Scale);

                    var texd = QoLBar.textureDictionary;
                    var tex = texd[icon];
                    if (tex != null)
                    {
                        var z = 0.5f / sh.IconZoom;
                        var offsetX = sh.IconOffset[0];
                        var offsetY = sh.IconOffset[1];
                        var size = new Vector2(ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.Y * 2);
                        var texMin = center - size / 2;
                        var texMax = texMin + size;
                        var uv0 = new Vector2(0.5f - z + offsetX, 0.5f - z + offsetY);
                        var uv1 = new Vector2(0.5f + z + offsetX, 0.5f + z + offsetY);

                        drawList.AddImage(tex.ImGuiHandle, texMin, texMax, uv0, uv1, color);

                        var frameArg = false;
                        if (args != "_")
                        {
                            frameArg = args.Contains("f");
                            if (QoLBar.Config.UseIconFrame)
                                frameArg = !frameArg;
                        }

                        if (frameArg && texd[QoLBar.FrameIconID] != null)
                        {
                            if (_buttonshine == null || _buttonshine.ImGuiHandle == IntPtr.Zero)
                            {
                                _buttonshine = texd[QoLBar.FrameIconID];
                                _uvMin = new Vector2(1f / 426f, 0f / 144f);
                                _uvMax = new Vector2(47f / 426f, 46f / 144f);
                                _uvMinHover = new Vector2(49f / 426f, 97f / 144f);
                                _uvMaxHover = new Vector2(95f / 426f, 143f / 144f);
                                //_uvMinHover2 = new Vector2(248f / 426f, 8f / 144f);
                                //_uvMaxHover2 = new Vector2(304f / 426f, 64f / 144f);
                            }
                            var _sizeInc = size * 0.075f;
                            var _rMin = texMin - _sizeInc;
                            var _rMax = texMax + _sizeInc;
                            drawList.AddImage(_buttonshine.ImGuiHandle, _rMin, _rMax, _uvMin, _uvMax); // Frame
                            if (hovered)
                                drawList.AddImage(_buttonshine.ImGuiHandle, _rMin, _rMax, _uvMinHover, _uvMaxHover, 0x85FFFFFF); // Frame Center Glow
                        }
                    }
                }
                else
                {
                    ImGui.SetWindowFontScale(bar.Scale * bar.FontScale);

                    var textSize = ImGui.CalcTextSize(name);
                    drawList.AddText(center - (textSize / 2), color, name);
                }

                ImGui.SetWindowFontScale(1);

                if (hovered && tooltip != string.Empty)
                    ImGui.SetTooltip(tooltip);
            };
        }
    }
}