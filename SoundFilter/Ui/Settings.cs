using System;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using SoundFilter.Resources;

namespace SoundFilter.Ui;

public class Settings : IDisposable
{
    private Plugin Plugin { get; }
    private AddFilter AddFilter { get; }
    private AddFilter? EditFilter { get; set; }

    private bool _showWindow;
    private int _dragging = -1;

    internal Settings(Plugin plugin)
    {
        Plugin = plugin;
        AddFilter = new AddFilter(plugin);

        Services.PluginInterface.UiBuilder.OpenConfigUi += Toggle;
    }

    public void Dispose()
    {
        Services.PluginInterface.UiBuilder.OpenConfigUi -= Toggle;
    }

    internal void Toggle()
    {
        _showWindow = !_showWindow;
    }

    internal void Draw()
    {
        if (!_showWindow)
        {
            return;
        }

        if (EditFilter != null)
        {
            ImGui.SetNextWindowSize(new Vector2(ImGui.GetWindowSize().X, -1));
            if (ImGui.BeginPopupModal($"{Language.SettingsEditFilter}###edit-filter-modal"))
            {
                if (EditFilter.Draw())
                {
                    EditFilter = null;
                }

                ImGui.EndPopup();
            }

            ImGui.OpenPopup("###edit-filter-modal");
        }

        ImGui.SetNextWindowSize(new Vector2(500, 450), ImGuiCond.FirstUseEver);

        var windowTitle = string.Format(Language.SettingsWindowTitle, Plugin.Name);
        if (!ImGui.Begin($"{windowTitle}###soundfilter-settings", ref _showWindow))
        {
            ImGui.End();
            return;
        }

        var shouldSave = false;
        if (ImGui.Checkbox(Language.SettingsEnableSoundFilter, ref Plugin.Config.Enabled))
        {
            if (Plugin.Config.Enabled)
            {
                Plugin.Filter.Enable();
            }
            else
            {
                Plugin.Filter.Disable();
            }

            shouldSave = true;
        }

        shouldSave |= ImGui.Checkbox(
            Language.SettingsShowSoundLogWindow,
            ref Plugin.Config.ShowLog
        );

        ImGui.Separator();

        if (ImGui.CollapsingHeader(Language.SettingsAddFilter))
        {
            AddFilter.Draw();
        }

        ImGui.Separator();

        if (ImGui.BeginChild("filtered-sounds"))
        {
            int? toRemove = null;
            (int src, int dst)? drag = null;
            for (var i = 0; i < Plugin.Config.Filters.Count; i++)
            {
                var filter = Plugin.Config.Filters[i];

                if (Util.IconButton(FontAwesomeIcon.Trash, $"delete-filter-{i}"))
                {
                    toRemove = i;
                    shouldSave = true;
                }

                ImGui.SameLine();
                if (Util.IconButton(FontAwesomeIcon.PencilAlt, $"edit-filter-{i}"))
                {
                    EditFilter = new AddFilter(Plugin, filter);
                }

                ImGui.SameLine();

                if (Util.IconButton(FontAwesomeIcon.Copy, $"copy-filter-{i}"))
                {
                    ImGui.SetClipboardText(string.Join("\n", filter.Globs));
                }

                ImGui.SameLine();

                shouldSave |= ImGui.Checkbox($"{filter.Name}##{i}", ref filter.Enabled);

                if (ImGui.IsItemActive() || _dragging == i)
                {
                    _dragging = i;
                    var step = 0;
                    if (
                        ImGui.GetIO().MouseDelta.Y < 0
                        && ImGui.GetMousePos().Y < ImGui.GetItemRectMin().Y
                    )
                    {
                        step = -1;
                    }

                    if (
                        ImGui.GetIO().MouseDelta.Y > 0
                        && ImGui.GetMousePos().Y > ImGui.GetItemRectMax().Y
                    )
                    {
                        step = 1;
                    }

                    if (step != 0)
                    {
                        drag = (i, i + step);
                    }
                }

                if (!ImGui.IsItemHovered())
                {
                    continue;
                }

                ImGui.BeginTooltip();
                foreach (var glob in filter.Globs)
                {
                    ImGui.TextUnformatted(glob);
                }

                ImGui.EndTooltip();
            }

            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && _dragging != -1)
            {
                _dragging = -1;
                Plugin.Config.Save();
            }

            if (drag != null && drag.Value.dst < Plugin.Config.Filters.Count && drag.Value.dst >= 0)
            {
                _dragging = drag.Value.dst;

                (Plugin.Config.Filters[drag.Value.src], Plugin.Config.Filters[drag.Value.dst]) = (
                    Plugin.Config.Filters[drag.Value.dst],
                    Plugin.Config.Filters[drag.Value.src]
                );
            }

            if (toRemove != null)
            {
                Plugin.Config.Filters.RemoveAt(toRemove.Value);
            }

            ImGui.EndChild();
        }

        if (shouldSave)
        {
            Plugin.Config.Save();
        }

        ImGui.End();
    }
}
