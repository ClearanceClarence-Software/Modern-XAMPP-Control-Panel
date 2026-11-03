using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XamppControl.Core;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>Follows server log files live through the <see cref="LogViewerViewModel"/> set as data context.</summary>
public partial class LogViewerWindow : Window
{
    private LogViewerViewModel? _subscribed;

    /// <summary>Sets up the window; the newest line stays in view as lines arrive.</summary>
    public LogViewerWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);
        DataContextChanged += (_, _) => Subscribe();
        Closed += (_, _) =>
        {
            Unsubscribe();
            ViewModel?.Dispose();
        };
    }

    private LogViewerViewModel? ViewModel => DataContext as LogViewerViewModel;

    /// <summary>Moves the scroll subscription to the current view model.</summary>
    private void Subscribe()
    {
        Unsubscribe();
        _subscribed = ViewModel;
        if (_subscribed is not null)
        {
            _subscribed.Lines.CollectionChanged += OnLinesChanged;
        }
    }

    /// <summary>Stops following the previous view model's lines.</summary>
    private void Unsubscribe()
    {
        if (_subscribed is not null)
        {
            _subscribed.Lines.CollectionChanged -= OnLinesChanged;
            _subscribed = null;
        }
    }

    /// <summary>Keeps the newest line in view as lines arrive.</summary>
    /// <param name="sender">The view model's line collection.</param>
    /// <param name="e">Additions and rebuilds scroll to the end; a cleared view has nothing to scroll to.</param>
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Remove || ViewModel is not { Lines.Count: > 0 } viewModel)
        {
            return;
        }

        // Background priority lets the list lay out the new items before it is asked to scroll to the last one.
        // After a reload or a filter change the list is rebuilt, so the scroll waits for the last add of the batch.
        LogLine newest = viewModel.Lines[^1];
        Dispatcher.UIThread.Post(() => ScrollToEnd(newest), DispatcherPriority.Background);
    }

    /// <summary>Scrolls the list to its last line, when that line is still the last one.</summary>
    /// <param name="newest">The line that was last when the scroll was requested.</param>
    private void ScrollToEnd(LogLine newest)
    {
        if (ViewModel is { Lines.Count: > 0 } viewModel && ReferenceEquals(viewModel.Lines[^1], newest))
        {
            LinesList.ScrollIntoView(newest);
        }
    }

    private async void CopySelected_Click(object? sender, RoutedEventArgs e)
    {
        IEnumerable<string> lines = LinesList.SelectedItems?.OfType<LogLine>().Select(line => line.Text) ?? [];
        await CopyAsync(string.Join(Environment.NewLine, lines));
    }

    private async void CopyAll_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await CopyAsync(string.Join(Environment.NewLine, viewModel.Lines.Select(line => line.Text)));
        }
    }

    /// <summary>Puts the text on the clipboard; does nothing when it is empty or no clipboard is available.</summary>
    /// <param name="text">The lines to copy, already joined with newlines.</param>
    /// <returns>A task that completes when the clipboard has been set or skipped.</returns>
    private async Task CopyAsync(string text)
    {
        if (text.Length > 0 && GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
