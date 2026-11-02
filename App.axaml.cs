using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.ViewModels;
using XamppControl.Views;

namespace XamppControl;

/// <summary>The Avalonia application: loads the settings and opens the main window.</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel(AppSettings.Load());
            AppSettings settings = viewModel.Settings;
            AppTheme.Apply(settings.Theme);
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
                WindowState = settings.StartMinimized
                    ? Avalonia.Controls.WindowState.Minimized
                    : Avalonia.Controls.WindowState.Normal,
            };
            desktop.MainWindow = mainWindow;

            // Faulted tasks nobody awaited are shown on the UI thread. A crash that unwinds the UI thread or a
            // plain thread ends the process before any window can show, so those reports only reach crash.log.
            CrashReport.Shown = details => Dispatcher.UIThread.Post(() => new CrashWindow(details).Show());
            RelayCommand.UnhandledError += exception => viewModel.Write("main", exception.Message, LogLevel.Error);

            var tray = new TrayController(this, mainWindow, viewModel);
            mainWindow.Tray = tray;
            desktop.ShutdownRequested += (_, _) =>
            {
                tray.Dispose();
                viewModel.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
