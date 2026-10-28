using Avalonia.Controls;
using XamppControl.Core;

namespace XamppControl.Views;

public partial class NetstatWindow : Window
{
    public NetstatWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);
        // Stop the auto-refresh timer when the window closes.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
}
