using System;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using SoundFilter.Resources;

namespace SoundFilter.Ui;

public class SoundLog
{
    private Plugin Plugin { get; }

    private string _search = string.Empty;

    internal SoundLog(Plugin plugin)
    {
        Plugin = plugin;
    }

    internal void Draw()
    {
        if (!Plugin.Config.ShowLog)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(500, 450), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin(Language.LogWindowTitle, ref Plugin.Config.ShowLog))
        {
            ImGui.End();
            return;
        }

        if (ImGui.Checkbox(Language.LogEnableLogging, ref Plugin.Config.LogEnabled))
        {
            Plugin.Config.Save();
        }

        ImGui.SameLine();

        if (ImGui.Checkbox(Language.LogLogFiltered, ref Plugin.Config.LogFiltered))
        {
            Plugin.Config.Save();
        }

        ImGui.InputText(Language.LogSearch, ref _search, 255);

        var entries = (int)Plugin.Config.LogEntries;
        if (ImGui.InputInt(Language.LogMaxRecentSounds, ref entries))
        {
            Plugin.Config.LogEntries = (uint)Math.Min(10_000, Math.Max(0, entries));
            Plugin.Config.Save();
        }

        ImGui.Separator();

        if (ImGui.BeginChild("sounds"))
        {
            var i = 0;
            foreach (var recent in Plugin.Filter.Recent.Reverse())
            {
                if (!string.IsNullOrWhiteSpace(_search) && !recent.ContainsIgnoreCase(_search))
                {
                    continue;
                }

                if (Util.IconButton(FontAwesomeIcon.Copy, $"copy-{recent}-{i}"))
                {
                    ImGui.SetClipboardText(recent);
                }

                ImGui.SameLine();
                ImGui.TextUnformatted(recent);
                i += 1;
            }

            ImGui.EndChild();
        }

        // god disabled this frame
        if (!Plugin.Config.ShowLog)
        {
            Plugin.Config.Save();
        }

        ImGui.End();
    }
}
