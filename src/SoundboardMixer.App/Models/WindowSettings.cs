namespace SoundboardMixer.App.Models;

/// <summary>
/// Stores the basic main window placement settings.
/// </summary>
public sealed class WindowSettings
{
    public double Width { get; set; } = 1180;

    public double Height { get; set; } = 760;

    public double? Left { get; set; }

    public double? Top { get; set; }

    public bool IsMaximized { get; set; }
}
