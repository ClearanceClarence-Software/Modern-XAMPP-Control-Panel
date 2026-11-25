using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XamppControl.Core;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>Posed screenshot scenes on the <see cref="MockMachine"/>, driven by <c>--scene</c> and <c>--shot</c>.</summary>
public partial class MainWindow
{
    /// <summary>
    /// The screenshot scenes, each a window or dialog posed on the pretend PC; <c>--scene &lt;name&gt;</c> shows one,
    /// <c>--shot &lt;file&gt;</c> also captures it. tools\screenshots.ps1 runs them all.
    /// </summary>
    public static readonly string[] Scenes =
    [
        // The main window in its states.
        "main", "stopped", "starting", "services", "new-versions", "update", "config-error", "menu", "quit", "no-xampp",
        "xampp-missing", "port-conflict",

        // Netstat.
        "netstat", "netstat-xampp", "netstat-filter", "netstat-conflict",

        // Versions: PHP, MariaDB and the tools, with several versions installed side by side.
        "versions", "versions-mariadb", "versions-tools", "versions-installing", "versions-switched",

        // Sites, PHP, databases and logs.
        "sites", "sites-untrusted", "sites-empty", "php", "php-no-xdebug", "php-xdebug-update", "databases",
        "databases-drop", "databases-stopped", "logs", "logs-filter", "logs-php",

        // Options.
        "config", "config-language", "ports", "user-files", "log-options",

        // The panel itself.
        "about", "whats-new", "whats-new-installing", "crash",
    ];

    private const string SampleCrash =
        """
        Modern XAMPP Control Panel crashed in the UI thread.
        System.InvalidOperationException: Sequence contains no matching element
           at System.Linq.ThrowHelper.ThrowNoMatchException()
           at XamppControl.ViewModels.SitesViewModel.Apply() in ViewModels\SitesViewModel.cs:line 184
           at XamppControl.Views.SitesWindow.Apply_Click(Object sender, RoutedEventArgs e) in Views\SitesWindow.axaml.cs:line 41
           at Avalonia.Interactivity.Interactive.RaiseEvent(RoutedEventArgs e)
           at Avalonia.Controls.Button.OnClick()
        """;

    private static readonly string[] _mockModules = ["Apache", "MySQL", "Mailpit"];

    /// <summary>Runs one <c>--scene</c>: sizes the window, poses the scene, and with <c>--shot</c> captures it and exits.</summary>
    /// <param name="scene">A name from <see cref="Scenes"/>; an unknown name poses the plain main window.</param>
    /// <param name="options">The switches the panel was started with: the shot file and the window size.</param>
    /// <returns>A task that completes when the scene is posed, or when the panel has been told to exit.</returns>
    internal async Task PoseAsync(string scene, MockOptions options)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (options.Size is { } size)
        {
            // The asked size exactly, centered on the screen the window is on.
            Width = size.Width;
            Height = size.Height;
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                Position = new PixelPoint(
                    screen.WorkingArea.X + (int)((screen.WorkingArea.Width - (size.Width * RenderScaling)) / 2),
                    screen.WorkingArea.Y + (int)((screen.WorkingArea.Height - (size.Height * RenderScaling)) / 2));
            }
        }

        // The panel's own startup lines land in the log first.
        await Settle(1500);
        (Window? dialog, bool isOverMain) = await PoseSceneAsync(scene, viewModel);

        if (options.Shot is not { } shotFile)
        {
            return;
        }

        // The capture is a copy of the screen, so the scene's window must be in front of everything else.
        Window front = dialog ?? this;
        front.Topmost = true;
        front.Activate();
        await Settle(900);
        try
        {
            // Menus and drop-down lists close when another window comes forward, so they open last.
            await OpenPopupAsync(scene, dialog);
            Capture(shotFile, dialog, isOverMain);
        }
        catch (Exception ex)
        {
            // The screenshot run reports and still exits cleanly, so a batch of shots goes on. The panel has no
            // console, so the reason is left next to where the picture would be.
            File.WriteAllText(shotFile + ".error.txt", ex.ToString());
        }

        // The quit scene has the "still running" question open; the exit must not ask it again.
        _isClosingConfirmed = true;
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>Opens the menu or drop-down list a scene shows; it stays open until the scene is clicked.</summary>
    /// <param name="scene">The scene's name.</param>
    /// <param name="dialog">The scene's window, when it has one.</param>
    /// <returns>A task that completes when the popup is drawn; at once for scenes without one.</returns>
    private async Task OpenPopupAsync(string scene, Window? dialog)
    {
        switch (scene)
        {
            case "menu":
                // The Config button of Apache's row is the first button with a menu.
                Button? config = this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
                    button.Flyout is MenuFlyout && button.DataContext is ModuleViewModel { Name: "Apache" });
                config?.Flyout?.ShowAt(config);
                break;

            case "config-language" when dialog is SettingsWindow settings:
                settings.OpenLanguageList();
                break;

            default:
                return;
        }
        await Settle(700);
    }

    /// <summary>Puts the mock machine and the panel into the state a scene shows.</summary>
    /// <param name="scene">The scene's name.</param>
    /// <param name="viewModel">The panel, running on the mock machine.</param>
    /// <returns>
    /// The window or dialog the scene is about, or null for the main window itself; and whether the picture should
    /// show it on top of the main window (small dialogs) or alone (the tool windows).
    /// </returns>
    private async Task<(Window? Dialog, bool IsOverMain)> PoseSceneAsync(string scene, MainViewModel viewModel)
    {
        string[] running = scene switch
        {
            "stopped" or "no-xampp" or "xampp-missing" => [],
            "port-conflict" or "netstat-conflict" or "starting" or "config-error" => ["MySQL", "Mailpit"],
            "databases-stopped" => ["Apache", "Mailpit"],
            _ => _mockModules,
        };
        await StartModulesAsync(viewModel, running);

        switch (scene)
        {
            case "starting":
                // A start on the mock machine takes minutes in this scene, so the row stays on "Starting…".
                viewModel.FindModule("Apache")?.ActionCommand.Execute(null);
                await Settle(800);
                return (null, false);

            case "config-error":
                // httpd -t answers with a syntax error in this scene; the log names the file and the line.
                viewModel.FindModule("Apache")?.ActionCommand.Execute(null);
                await Settle(1500);
                return (null, false);

            case "quit":
                // Closing with modules running brings up the question; the answer is left to whoever looks.
                Close();
                return (await WaitForAsync<ConfirmWindow>(), true);

            case "update":
                await WaitForUpdateAsync(viewModel);
                return (null, false);

            case "new-versions":
                // The startup check finds a newer PHP, MariaDB and Mailpit in this scene.
                for (int attempt = 0; attempt < 30 && !viewModel.HasComponentUpdates; attempt++)
                {
                    await Settle(150);
                }
                return (null, false);

            case "whats-new":
                await WaitForUpdateAsync(viewModel);
                return (await ShowAsync(new WhatsNewWindow { DataContext = viewModel }), false);

            case "whats-new-installing":
                {
                    await WaitForUpdateAsync(viewModel);
                    Window whatsNew = await ShowAsync(new WhatsNewWindow { DataContext = viewModel });

                    // The download is only played on the mock machine: the bar runs, then the install gives up.
                    _ = viewModel.InstallAppUpdateAsync();
                    await Settle(1500);
                    return (whatsNew, false);
                }

            case "xampp-missing":
                // Opened by the panel itself, because the mock machine has no XAMPP in this scene.
                return (await WaitForAsync<XamppMissingWindow>(), true);

            case "port-conflict":
                // Windows holds ports 80 and 443 in this scene; starting Apache brings up the dialog.
                viewModel.FindModule("Apache")?.ActionCommand.Execute(null);
                return (await WaitForAsync<ConfirmWindow>(), true);

            case "netstat" or "netstat-conflict" or "netstat-xampp" or "netstat-filter":
                {
                    var netstat = new NetstatViewModel(viewModel);
                    Window window = await ShowAsync(new NetstatWindow { DataContext = netstat });
                    if (scene == "netstat-xampp")
                    {
                        netstat.XamppOnly = true;
                    }
                    else if (scene == "netstat-filter")
                    {
                        netstat.Filter = "apache";
                    }
                    else if (scene == "netstat-conflict")
                    {
                        // The program in Apache's way, with its explanation in the detail pane.
                        netstat.Selected = netstat.Programs.FirstOrDefault(program => program.IsBlocking) ?? netstat.Selected;
                    }
                    await Settle();
                    return (window, false);
                }

            case "versions" or "versions-mariadb" or "versions-tools" or "versions-installing" or "versions-switched":
                {
                    var versions = new UpdatesViewModel(viewModel);
                    var window = new UpdatesWindow { DataContext = versions };
                    await ShowAsync(window);
                    switch (scene)
                    {
                        case "versions-mariadb":
                            window.ScrollToSection("mariadb");
                            break;
                        case "versions-tools":
                            window.ScrollToSection("tools");
                            break;
                        case "versions-installing":
                            // Played on the mock machine: the bar runs for the newest branch, nothing is installed.
                            versions.SelectedPhp = versions.PhpBranches.FirstOrDefault() ?? versions.SelectedPhp;
                            _ = versions.InstallPhpLatestAsync();
                            await Settle(1500);
                            break;
                        case "versions-switched":
                            versions.PoseSwitched();
                            break;
                        default:
                            break;
                    }
                    await Settle();
                    return (window, false);
                }

            case "sites" or "sites-untrusted" or "sites-empty":
                return (await ShowAsync(new SitesWindow { DataContext = new SitesViewModel(viewModel) }), false);

            case "php" or "php-no-xdebug" or "php-xdebug-update":
                return (await ShowAsync(new PhpSettingsWindow { DataContext = new PhpSettingsViewModel(viewModel) }), false);

            case "databases" or "databases-stopped" or "databases-drop":
                {
                    var databases = new DatabasesViewModel(viewModel);
                    Window window = await ShowAsync(new DatabasesWindow { DataContext = databases });
                    // "shop" has a dump from this morning, so the detail pane shows every field.
                    string chosen = scene == "databases-drop" ? "blog_wp" : "shop";
                    if (databases.Databases.FirstOrDefault(database => database.Name == chosen) is { } row)
                    {
                        databases.Selected = row;
                        if (scene == "databases-drop")
                        {
                            // The first click only asks; nothing is dropped.
                            await databases.DropAsync(row);
                        }
                        await Settle();
                    }
                    return (window, false);
                }

            case "logs" or "logs-filter" or "logs-php":
                {
                    var logs = new LogViewerViewModel(viewModel);
                    Window window = await ShowAsync(new LogViewerWindow { DataContext = logs });
                    if (scene == "logs-filter")
                    {
                        logs.Filter = "error";
                    }
                    else if (scene == "logs-php"
                        && logs.Sources.FirstOrDefault(source => source.Label.Contains("PHP", StringComparison.Ordinal)) is { } php)
                    {
                        logs.Selected = php;
                    }
                    await Settle(800);
                    return (window, false);
                }

            case "config" or "config-language":
                return (await ShowAsync(new SettingsWindow(viewModel.Settings.Clone(), viewModel.Root)), false);

            case "ports":
                return (await ShowAsync(new ServicePortsWindow(viewModel.Settings.Clone())), false);

            case "user-files":
                return (await ShowAsync(new UserFilesWindow(viewModel.Settings.Clone())), false);

            case "log-options":
                return (await ShowAsync(new LogOptionsWindow(viewModel.Settings.Clone())), false);

            case "about":
                return (await ShowAsync(new AboutWindow { DataContext = viewModel }), false);

            case "crash":
                return (await ShowAsync(new CrashWindow(SampleCrash)), false);

            default:
                return (null, false);
        }
    }

    /// <summary>Starts modules on the mock machine one after the other, with the log lines a real start writes.</summary>
    /// <param name="viewModel">The panel.</param>
    /// <param name="names">The modules to start, in order.</param>
    /// <returns>A task that completes when the rows show the modules running.</returns>
    private static async Task StartModulesAsync(MainViewModel viewModel, string[] names)
    {
        foreach (string name in names)
        {
            if (viewModel.FindModule(name) is { } module)
            {
                viewModel.Write(module.Driver.Tag, $"Attempting to start {name} app...");
                MockMachine.SetRunning(name, true);
                await viewModel.RefreshAsync(quiet: false);
            }
        }

        // A refresh that was already under way makes the calls above return at once; wait until the rows agree.
        for (int attempt = 0; attempt < 20 && names.Any(name => viewModel.FindModule(name) is { IsRunning: false }); attempt++)
        {
            await Settle(100);
            await viewModel.RefreshAsync(quiet: false);
        }
        await Settle();
    }

    /// <summary>Waits for the startup update check, which finds the mock release in the update scenes.</summary>
    /// <param name="viewModel">The panel.</param>
    /// <returns>A task that completes when the header shows the update, or after a few seconds without one.</returns>
    private static async Task WaitForUpdateAsync(MainViewModel viewModel)
    {
        for (int attempt = 0; attempt < 30 && !viewModel.HasAppUpdate; attempt++)
        {
            await Settle(150);
        }
    }

    /// <summary>Opens a window over the main one and waits for it to load.</summary>
    /// <param name="window">The window to show.</param>
    /// <returns>The same window, shown and settled.</returns>
    private async Task<Window> ShowAsync(Window window)
    {
        window.Show(this);
        await Settle(1200);
        return window;
    }

    /// <summary>Waits for a dialog the panel opens by itself.</summary>
    /// <typeparam name="TDialog">The dialog's window class.</typeparam>
    /// <returns>The dialog, or null when it hasn't appeared after a few seconds.</returns>
    private static async Task<Window?> WaitForAsync<TDialog>()
        where TDialog : Window
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            await Settle(150);
            if (desktop?.Windows.OfType<TDialog>().LastOrDefault() is { } dialog)
            {
                await Settle(600);
                return dialog;
            }
        }
        return null;
    }

    /// <summary>Lets pending work finish: a pause, then one pass of the dispatcher down to background priority.</summary>
    /// <param name="milliseconds">How long to wait first.</param>
    /// <returns>A task that completes when layout and rendering have caught up.</returns>
    private static async Task Settle(int milliseconds = 400)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    // ------------------------------------------------------------------ capture

    /// <summary>Writes the scene to a PNG, captured off the screen with its title bar.</summary>
    /// <param name="shotFile">The PNG to write; missing folders are created.</param>
    /// <param name="dialog">The scene's window or dialog; null captures the main window.</param>
    /// <param name="isOverMain">True captures the main window with the dialog on it; false the dialog alone.</param>
    /// <exception cref="InvalidOperationException">The screen copy failed, or the window has no native handle.</exception>
    private void Capture(string shotFile, Window? dialog, bool isOverMain)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(shotFile)) ?? ".");
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("Screenshots are captured through GDI and need Windows.");
        }

        nint mainHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        nint dialogHandle = dialog?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (mainHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The main window has no native handle to capture.");
        }

        ScreenCapture.RECT area;
        if (dialogHandle == IntPtr.Zero)
        {
            area = ScreenCapture.FrameOf(mainHandle);
        }
        else if (isOverMain)
        {
            area = ScreenCapture.Union(ScreenCapture.FrameOf(mainHandle), ScreenCapture.FrameOf(dialogHandle));
        }
        else
        {
            area = ScreenCapture.FrameOf(dialogHandle);
        }

        using WriteableBitmap bitmap = ScreenCapture.Grab(area);
        SavePng(bitmap, shotFile);
    }

    /// <summary>Writes a bitmap as PNG, the only format Avalonia's plain Save produces.</summary>
    /// <param name="bitmap">The captured image.</param>
    /// <param name="pngFile">The file to write; its folder must already exist.</param>
    private static void SavePng(Bitmap bitmap, string pngFile)
    {
#pragma warning disable CS0618 // plain Save writes a PNG
        bitmap.Save(pngFile);
#pragma warning restore CS0618
    }

    /// <summary>GDI screen capture of a window frame.</summary>
    [SupportedOSPlatform("windows")]
    private static class ScreenCapture
    {
        /// <summary>A Win32 rectangle: left, top, right and bottom edges in screen pixels, right and bottom exclusive.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int Size, Width, Height;
            public short Planes, BitCount;
            public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
        }

        private const int DwmaExtendedFrameBounds = 9;
        private const uint SrcCopy = 0x00CC0020;
        private const uint CaptureBlt = 0x40000000;

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(
            IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        /// <summary>The window's visible frame in screen pixels, without the invisible resize border.</summary>
        /// <param name="hwnd">The native window handle, as <c>TryGetPlatformHandle()</c> gives it.</param>
        /// <returns>The DWM extended frame bounds, or the plain window rectangle (border included) when DWM declines.</returns>
        public static RECT FrameOf(IntPtr hwnd)
        {
            if (DwmGetWindowAttribute(hwnd, DwmaExtendedFrameBounds, out RECT rect, Marshal.SizeOf<RECT>()) != 0)
            {
                GetWindowRect(hwnd, out rect);
            }
            return rect;
        }

        /// <summary>The smallest rectangle holding both.</summary>
        /// <param name="first">One screen rectangle.</param>
        /// <param name="second">The other; order does not matter.</param>
        /// <returns>The bounding rectangle of the two.</returns>
        public static RECT Union(RECT first, RECT second) => new()
        {
            Left = Math.Min(first.Left, second.Left),
            Top = Math.Min(first.Top, second.Top),
            Right = Math.Max(first.Right, second.Right),
            Bottom = Math.Max(first.Bottom, second.Bottom),
        };

        /// <summary>Copies a screen area into a bitmap through GDI, layered windows included.</summary>
        /// <param name="area">Screen rectangle in physical pixels; a zero or negative size is captured as one pixel.</param>
        /// <returns>An opaque BGRA bitmap at 96 dpi that the caller disposes.</returns>
        /// <exception cref="InvalidOperationException">The screen copy failed.</exception>
        public static WriteableBitmap Grab(RECT area)
        {
            int width = Math.Max(1, area.Right - area.Left);
            int height = Math.Max(1, area.Bottom - area.Top);
            nint screen = GetDC(IntPtr.Zero);
            nint memory = CreateCompatibleDC(screen);

            // A negative height makes the DIB top-down, the same row order as the bitmap's framebuffer.
            var header = new BITMAPINFOHEADER
            {
                Size = Marshal.SizeOf<BITMAPINFOHEADER>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
            };
            nint section = CreateDIBSection(memory, ref header, 0, out nint bits, IntPtr.Zero, 0);
            nint previous = SelectObject(memory, section);
            try
            {
                if (!BitBlt(memory, 0, 0, width, height, screen, area.Left, area.Top, SrcCopy | CaptureBlt))
                {
                    throw new InvalidOperationException("BitBlt failed.");
                }

                var bitmap = new WriteableBitmap(
                    new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                using (ILockedFramebuffer buffer = bitmap.Lock())
                {
                    int rowBytes = width * 4;
                    byte[] row = new byte[rowBytes];
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(bits + (y * rowBytes), row, 0, rowBytes);
                        Marshal.Copy(row, 0, buffer.Address + (y * buffer.RowBytes), rowBytes);
                    }
                }
                return bitmap;
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(section);
                DeleteDC(memory);
                _ = ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
