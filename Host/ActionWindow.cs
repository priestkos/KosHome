using Dalamud.Interface.Windowing;

namespace KosHome;

/// <summary>A window whose body is a method: for an app that drew a bare ImGui.Begin window of its own.</summary>
internal sealed class ActionWindow(string name, Action draw) : Window(name)
{
    public override void Draw() => draw();
}
