using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XamppControl.Core;
using XamppControl.Updates;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>
/// Installs, switches and rolls back PHP and MariaDB versions through the <see cref="UpdatesViewModel"/>
/// that the caller sets as data context.
/// </summary>
public partial class UpdatesWindow : Window
{
    /// <summary>Sets up the window, checks for new versions on open and blocks closing while an update runs.</summary>
    public UpdatesWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);

        Opened += async (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                await viewModel.CheckAsync();
            }
        };

        // Don't allow closing halfway through an update.
        Closing += (_, closingArgs) =>
        {
            if (ViewModel is { IsBusy: true })
            {
                closingArgs.Cancel = true;
            }
        };
    }

    private UpdatesViewModel? ViewModel => DataContext as UpdatesViewModel;

    private async void Check_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.CheckAsync();
        }
    }

    private async void PhpLatest_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await AskToRemoveAsync(
                viewModel,
                await viewModel.InstallAndFindSupersededAsync(Component.Php, viewModel.InstallPhpLatestAsync));
        }
    }

    private async void PhpZip_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && await PickZipAsync(Loc.T("Updates.ChoosePhpZip")) is { } zip)
        {
            await AskToRemoveAsync(
                viewModel,
                await viewModel.InstallAndFindSupersededAsync(Component.Php, () => viewModel.InstallPhpFromZipAsync(zip)));
        }
    }

    private async void MariaLatest_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await AskToRemoveAsync(
                viewModel,
                await viewModel.InstallAndFindSupersededAsync(Component.MariaDb, viewModel.InstallMariaLatestAsync));
        }
    }

    private async void MariaZip_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && await PickZipAsync(Loc.T("Updates.ChooseMariaZip")) is { } zip)
        {
            await AskToRemoveAsync(
                viewModel,
                await viewModel.InstallAndFindSupersededAsync(
                    Component.MariaDb, () => viewModel.InstallMariaFromZipAsync(zip)));
        }
    }

    private void FileZillaDownload_Click(object? sender, RoutedEventArgs e) => ViewModel?.OpenFileZillaDownloadPage();

    /// <summary>For the screenshot scenes: scrolls a section to the top of the window.</summary>
    /// <param name="section">"mariadb" or "tools" (FileZilla Server and Mailpit); anything else stays at PHP.</param>
    internal void ScrollToSection(string section)
    {
        Border? card = section switch
        {
            "mariadb" => MariaCard,
            "tools" => FileZillaCard,
            _ => null,
        };
        if (card is not null && Sections is not null)
        {
            Sections.Offset = new Avalonia.Vector(0, card.Bounds.Top);
        }
    }

    private void MailpitPage_Click(object? sender, RoutedEventArgs e) => ViewModel?.OpenMailpitPage();

    private async void MailpitInstall_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.InstallMailpitAsync();
        }
    }

    private async void FileZillaInstall_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Updates.ChooseFileZillaInstaller"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.T("Updates.Installers")) { Patterns = ["FileZilla_Server*.exe", "*.exe"] },
            ],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } installer)
        {
            await viewModel.InstallFileZillaAsync(installer);
        }
    }

    private async void Rollback_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.RollbackAsync();
        }
    }

    // The buttons sit in each version's row, so the row's data context is the version they act on.
    private async void Switch_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && (sender as Control)?.DataContext is InstalledVersion version)
        {
            await viewModel.SwitchAsync(version);
        }
    }

    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && (sender as Control)?.DataContext is InstalledVersion version)
        {
            await AskToRemoveAsync(viewModel, await viewModel.UpdateActiveAsync(version.Component));
        }
    }

    /// <summary>
    /// After an update within the same branch: asks whether the build it replaced should go, since the new one
    /// likely fixes bugs or security issues in it.
    /// </summary>
    /// <param name="viewModel">The window's view model.</param>
    /// <param name="superseded">The replaced build; null (nothing was superseded) asks nothing.</param>
    /// <returns>A task that completes when the question is answered and the build removed or kept.</returns>
    private async Task AskToRemoveAsync(UpdatesViewModel viewModel, InstalledVersion? superseded)
    {
        if (superseded is null)
        {
            return;
        }

        bool isMaria = superseded.Component == Component.MariaDb;
        string active = (isMaria ? viewModel.MariaVersions : viewModel.PhpVersions)
            .FirstOrDefault(row => row.IsActive)?.Title ?? "";
        ConfirmChoice choice = await ConfirmWindow.AskAsync(
            this,
            Loc.T("Updates.RemovePreviousTitle", superseded.Title),
            Loc.T(isMaria ? "Updates.RemovePreviousMaria" : "Updates.RemovePreviousPhp", active, superseded.Title),
            Loc.T("Updates.RemoveVersion", superseded.Title),
            Loc.T("Updates.KeepIt"));
        if (choice == ConfirmChoice.Primary)
        {
            viewModel.RemoveSuperseded(superseded);
        }
        else
        {
            viewModel.KeepSuperseded(superseded);
        }
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && (sender as Control)?.DataContext is InstalledVersion version)
        {
            viewModel.Remove(version);
        }
    }

    private async void PhpImport_Click(object? sender, RoutedEventArgs e)
    {
        string title = Loc.T("Updates.ChoosePhpFolder");
        if (ViewModel is { } viewModel && await PickFolderAsync(title) is { } folder)
        {
            viewModel.Import(Component.Php, folder);
        }
    }

    private async void MariaImport_Click(object? sender, RoutedEventArgs e)
    {
        string title = Loc.T("Updates.ChooseMariaFolder");
        if (ViewModel is { } viewModel && await PickFolderAsync(title) is { } folder)
        {
            viewModel.Import(Component.MariaDb, folder);
        }
    }

    /// <summary>Asks for one folder holding an existing installation.</summary>
    /// <param name="title">The picker's title bar text.</param>
    /// <returns>The chosen folder's local path; null when the picker is canceled or it has no local path.</returns>
    private async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Asks for one downloaded .zip archive.</summary>
    /// <param name="title">The picker's title bar text, naming the build the archive must be.</param>
    /// <returns>The chosen archive's local path; null when the picker is canceled or it has no local path.</returns>
    private async Task<string?> PickZipAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Loc.T("Updates.ZipArchives")) { Patterns = ["*.zip"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
