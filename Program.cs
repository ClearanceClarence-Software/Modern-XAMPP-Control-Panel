using Avalonia;

namespace XamppControl;

/// <summary>The entry point.</summary>
internal static class Program
{
    /// <summary>Runs the Avalonia desktop app and returns when it shuts down.</summary>
    /// <param name="args">The command line, passed on to the desktop lifetime.</param>
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>The Avalonia configuration, shared by <see cref="Main"/> and the XAML designer.</summary>
    /// <returns>A builder for <see cref="App"/> with platform detection, the Inter font and trace logging.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
