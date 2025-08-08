using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using SoundFilter.Config;
using SoundFilter.Resources;

namespace SoundFilter.Ui;

internal class AddFilter
{
    private Guid Id { get; } = Guid.NewGuid();
    private Plugin Plugin { get; }
    private CustomFilter? Filter { get; }

    private string _filterName = string.Empty;
    private string _newSoundPath = string.Empty;
    private readonly List<string> _soundPaths = [];

    internal AddFilter(Plugin plugin)
    {
        Plugin = plugin;
        Filter = null;
    }

    internal AddFilter(Plugin plugin, CustomFilter filter)
    {
        Plugin = plugin;
        Filter = filter;

        _filterName = filter.Name;
        _soundPaths.AddRange(filter.Globs);
    }

    internal bool Draw()
    {
        ImGui.TextUnformatted(Language.SettingsAddFilterName);

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText($"##sound-filter-name-{Id}", ref _filterName, 255);

        ImGui.TextUnformatted(Language.SettingsAddPathToFilter);
        int? toRemove = null;
        for (var i = 0; i < _soundPaths.Count; i++)
        {
            var path = _soundPaths[i];
            SetNextItemWidth();
            if (ImGui.InputText($"##sound-path-edit-{i}-{Id}", ref path, 255))
            {
                _soundPaths[i] = path;
            }

            ImGui.SameLine();

            if (Util.IconButton(FontAwesomeIcon.Trash, $"sound-path-delete-{i}-{Id}"))
            {
                toRemove = i;
            }
        }

        if (toRemove != null)
        {
            _soundPaths.RemoveAt(toRemove.Value);
        }

        SetNextItemWidth();
        ImGui.InputText($"##sound-path-{Id}", ref _newSoundPath, 255);
        ImGui.SameLine();
        if (
            Util.IconButton(FontAwesomeIcon.Plus, "add")
            && !string.IsNullOrWhiteSpace(_newSoundPath)
        )
        {
            _soundPaths.Add(_newSoundPath);
            _newSoundPath = string.Empty;
        }

        if (
            Util.IconButton(FontAwesomeIcon.Save, $"save-filter-{Id}")
            && !string.IsNullOrWhiteSpace(_filterName)
        )
        {
            if (!string.IsNullOrWhiteSpace(_newSoundPath))
            {
                _soundPaths.Add(_newSoundPath);
            }

            if (_soundPaths.Any(sound => !string.IsNullOrWhiteSpace(sound)))
            {
                Save();

                _filterName = string.Empty;
                _newSoundPath = string.Empty;
                _soundPaths.Clear();

                return true;
            }
        }

        if (Filter != null)
        {
            ImGui.SameLine();
            if (Util.IconButton(FontAwesomeIcon.Ban, $"cancel-filter-{Id}"))
            {
                return true;
            }
        }

        return false;
    }

    private static void SetNextItemWidth()
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetNextItemWidth(
            ImGui.GetContentRegionAvail().X
                - ImGui.CalcTextSize(FontAwesomeIcon.Ban.ToIconString()).X
                - ImGui.GetStyle().ItemSpacing.X * 2
        );
        ImGui.PopFont();
    }

    private void Save()
    {
        _soundPaths.RemoveAll(string.IsNullOrWhiteSpace);

        if (Filter != null)
        {
            Filter.Name = _filterName;
            Filter.Globs.Clear();
            Filter.Globs.AddRange(_soundPaths);
        }
        else
        {
            Plugin.Config.Filters.Add(
                new CustomFilter
                {
                    Name = _filterName,
                    Enabled = true,
                    Globs = [.. _soundPaths],
                }
            );
        }

        Plugin.Config.Save();
    }
}
