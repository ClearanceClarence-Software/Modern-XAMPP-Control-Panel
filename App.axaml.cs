using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using XamppControl.Core;
using XamppControl.ViewModels;
using XamppControl.Views;

namespace XamppControl;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel(AppSettings.Load());
            var settings = viewModel.Settings;
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
                WindowState = settings.StartMinimized
                    ? Avalonia.Controls.WindowState.Minimized
                    : Avalonia.Controls.WindowState.Normal,
            };
            desktop.ShutdownRequested += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
