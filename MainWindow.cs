using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KosHome;

public sealed class MainWindow : Window
{
    private readonly KosHomePlugin plugin;

    internal MainWindow(KosHomePlugin plugin) : base("KosHome##KosHome")
    {
        this.plugin = plugin;
        Size = new Vector2(640, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        using var table = Dalamud.Interface.Utility.Raii.ImRaii.Table("##apps", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH);
        if (!table)
            return;

        ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("App", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Commands", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        foreach (var app in plugin.Apps)
        {
            using var id = Dalamud.Interface.Utility.Raii.ImRaii.PushId(app.Info.Id);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var on = !plugin.Config.DisabledApps.Contains(app.Info.Id);
            if (ImGui.Checkbox("##on", ref on))
                plugin.SetEnabled(app, on);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(app.Info.Name);
            ImGui.TextDisabled(app.Info.Summary);
            if (app.Status is { } status)
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), status);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(app.Info.Commands);

            ImGui.TableNextColumn();
            if (app.Instance is { } instance)
            {
                if (ImGui.SmallButton("Open"))
                    instance.OpenMain();
                if (app.Info.HasSettings)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Settings"))
                        instance.OpenSettings();
                }
            }
            else if (on && ImGui.SmallButton("Retry"))
            {
                plugin.Start(app);
            }
        }
    }
}
