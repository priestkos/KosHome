using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KosHome;

public sealed class MainWindow : Window
{
    public MainWindow() : base("KosHome##KosHome")
    {
        Size = new Vector2(420, 260);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("KosHome: modules from the other PriestKos plugins will live here.");
    }
}
