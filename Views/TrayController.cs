using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using XamppControl.Core;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>
/// The notification area icon: shows which modules run, starts and stops them from its menu, and brings the window
/// back when it was minimized to the tray.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly Window _window;
    private readonly MainViewModel _viewModel;
    private readonly TrayIcon _trayIcon;
    private readonly Dictionary<ModuleViewModel, NativeMenuItem> _moduleItems = [];

    /// <summary>Adds the icon to the notification area with a menu built from the current modules.</summary>
    /// <param name="application">The app the icon belongs to; it is removed when the app shuts down.</param>
    /// <param name="window">The main window, shown again from the icon.</param>
    /// <param name="viewModel">Supplies the modules; the menu is rebuilt whenever they are reloaded.</param>
    public TrayController(Application application, Window window, MainViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;
        _trayIcon = new TrayIcon
        {
            Icon = AppIcon.LoadWindowIcon(),
            ToolTipText = BuildInfo.AppName,
        };
        _trayIcon.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(application, new TrayIcons { _trayIcon });

        viewModel.Modules.CollectionChanged += OnModulesChanged;
        RebuildMenu();
    }

    /// <summary>Shows the main window, restored and in front, after it was hidden to the tray or minimized.</summary>
    public void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }
        _window.Activate();
    }

    /// <summary>Removes the icon from the notification area and stops following the modules.</summary>
    public void Dispose()
    {
        _viewModel.Modules.CollectionChanged -= OnModulesChanged;
        StopWatchingModules();
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }

    private void OnModulesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildMenu();

    /// <summary>Builds the menu: Show, one start/stop entry per installed module, then Quit.</summary>
    private void RebuildMenu()
    {
        StopWatchingModules();

        var menu = new NativeMenu();
        var showItem = new NativeMenuItem("Show Control Panel");
        showItem.Click += (_, _) => ShowWindow();
        menu.Add(showItem);
        menu.Add(new NativeMenuItemSeparator());

        foreach (ModuleViewModel module in _viewModel.Modules.Where(candidate => candidate.IsInstalled))
        {
            var moduleItem = new NativeMenuItem(ModuleItemHeader(module))
            {
                IsEnabled = module.ActionCommand.CanExecute(null),
            };
            moduleItem.Click += (_, _) => module.ActionCommand.Execute(null);
            menu.Add(moduleItem);

            _moduleItems[module] = moduleItem;
            module.PropertyChanged += OnModuleChanged;
            module.ActionCommand.CanExecuteChanged += OnActionAvailabilityChanged;
        }

        menu.Add(new NativeMenuItemSeparator());
        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => _window.Close();
        menu.Add(quitItem);

        _trayIcon.Menu = menu;
        UpdateToolTip();
    }

    /// <summary>Unsubscribes from every module the menu follows.</summary>
    private void StopWatchingModules()
    {
        foreach (ModuleViewModel module in _moduleItems.Keys)
        {
            module.PropertyChanged -= OnModuleChanged;
            module.ActionCommand.CanExecuteChanged -= OnActionAvailabilityChanged;
        }
        _moduleItems.Clear();
    }

    /// <summary>Updates a module's menu entry and the tooltip when its state changes.</summary>
    /// <param name="sender">The module whose property changed.</param>
    /// <param name="e">Only changes of the action label, running or busy state matter.</param>
    private void OnModuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        bool isStateChange = e.PropertyName is nameof(ModuleViewModel.ActionLabel)
            or nameof(ModuleViewModel.IsRunning)
            or nameof(ModuleViewModel.IsBusy);
        if (!isStateChange || sender is not ModuleViewModel module)
        {
            return;
        }

        if (_moduleItems.TryGetValue(module, out NativeMenuItem? moduleItem))
        {
            moduleItem.Header = ModuleItemHeader(module);
            moduleItem.IsEnabled = module.ActionCommand.CanExecute(null);
        }
        UpdateToolTip();
    }

    /// <summary>
    /// Enables or disables the module entries when an action button becomes available again; the command only
    /// re-enables after its action and the following refresh have finished, after the property changes.
    /// </summary>
    /// <param name="sender">The action command whose availability changed.</param>
    /// <param name="e">Carries nothing; the sender says which command changed.</param>
    private void OnActionAvailabilityChanged(object? sender, EventArgs e)
    {
        foreach ((ModuleViewModel module, NativeMenuItem moduleItem) in _moduleItems)
        {
            if (ReferenceEquals(module.ActionCommand, sender))
            {
                moduleItem.IsEnabled = module.ActionCommand.CanExecute(null);
            }
        }
    }

    /// <summary>The tooltip: the app's name and which modules run.</summary>
    private void UpdateToolTip()
    {
        List<string> running = _viewModel.Modules
            .Where(module => module.IsRunning)
            .Select(module => module.Name)
            .ToList();
        string state = running.Count == 0 ? "nothing running" : string.Join(", ", running) + " running";
        _trayIcon.ToolTipText = $"{BuildInfo.AppName}\n{state}";
    }

    /// <summary>A module's menu entry, such as "Start Apache", "Stop MySQL" or "Apache: Starting…".</summary>
    /// <param name="module">The module the entry acts on.</param>
    /// <returns>The entry's text, following the module's action button.</returns>
    private static string ModuleItemHeader(ModuleViewModel module) =>
        module.IsBusy ? $"{module.Name}: {module.ActionLabel}" : $"{module.ActionLabel} {module.Name}";
}
