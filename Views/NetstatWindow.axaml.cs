using Avalonia.Controls;
using XamppControl.Core;

namespace XamppControl.Views;

/// <summary>
/// Lists the listening TCP sockets and their processes; the caller sets a <c>NetstatViewModel</c> as data context.
/// </summary>
public partial class NetstatWindow : Window
{
    /// <summary>Sets up the window and disposes the data context when the window closes.</summary>
    public NetstatWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);

        // Disposing the view model stops its auto-refresh timer.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
}
