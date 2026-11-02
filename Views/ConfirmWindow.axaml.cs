using Avalonia.Controls;
using Avalonia.Interactivity;
using XamppControl.Core;

namespace XamppControl.Views;

/// <summary>What the user chose in a <see cref="ConfirmWindow"/>.</summary>
public enum ConfirmChoice
{
    /// <summary>The window was closed or Cancel was clicked.</summary>
    Cancel,

    /// <summary>The second, optional button.</summary>
    Secondary,

    /// <summary>The main button.</summary>
    Primary,
}

/// <summary>A small question dialog with up to three buttons, in the panel's own style.</summary>
public partial class ConfirmWindow : Window
{
    /// <summary>Opens an empty dialog, for the XAML previewer.</summary>
    public ConfirmWindow() : this("Question", "", "OK", null)
    {
    }

    /// <summary>Opens the dialog with a title, a message and its buttons.</summary>
    /// <param name="title">One line saying what is being decided.</param>
    /// <param name="message">What each choice does.</param>
    /// <param name="primary">The main button's text.</param>
    /// <param name="secondary">The second button's text; null hides it.</param>
    public ConfirmWindow(string title, string message, string primary, string? secondary)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        Title = BuildInfo.AppName;
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primary;
        SecondaryButton.Content = secondary ?? "";
        SecondaryButton.IsVisible = secondary is not null;
        CancelButton.Content = "Cancel";
    }

    /// <summary>Shows the dialog over a window and waits for the choice.</summary>
    /// <param name="owner">The window to block.</param>
    /// <param name="title">One line saying what is being decided.</param>
    /// <param name="message">What each choice does.</param>
    /// <param name="primary">The main button's text.</param>
    /// <param name="secondary">The second button's text; null hides it.</param>
    /// <returns>The choice; <see cref="ConfirmChoice.Cancel"/> when the dialog was closed another way.</returns>
    public static async Task<ConfirmChoice> AskAsync(
        Window owner,
        string title,
        string message,
        string primary,
        string? secondary = null)
    {
        var dialog = new ConfirmWindow(title, message, primary, secondary);
        ConfirmChoice? choice = await dialog.ShowDialog<ConfirmChoice?>(owner);
        return choice ?? ConfirmChoice.Cancel;
    }

    private void Primary_Click(object? sender, RoutedEventArgs e) => Close(ConfirmChoice.Primary);

    private void Secondary_Click(object? sender, RoutedEventArgs e) => Close(ConfirmChoice.Secondary);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(ConfirmChoice.Cancel);
}
