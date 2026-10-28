using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace XamppControl.Core;

/// <summary>
/// The XAMPP icon, if Assets\xampp.png / xampp.ico were created with tools\Get-XamppIcon.ps1.
/// Without them the app falls back to its built-in stack glyph.
/// </summary>
public static class AppIcon
{
    private static readonly Uri _pngUri = new("avares://XamppControl/Assets/xampp.png");
    private static readonly Uri _icoUri = new("avares://XamppControl/Assets/xampp.ico");

    /// <summary>True when the build includes Assets\xampp.png.</summary>
    public static bool HasLogo => AssetLoader.Exists(_pngUri);

    /// <summary>Loads the XAMPP logo for the header.</summary>
    /// <returns>The decoded logo; null when the build has no logo or it cannot be decoded.</returns>
    public static Bitmap? LoadLogo()
    {
        try
        {
            return HasLogo ? new Bitmap(AssetLoader.Open(_pngUri)) : null;
        }
        catch
        {
            // Any failure to open or decode the asset is cosmetic: the header shows the built-in glyph instead.
            return null;
        }
    }

    /// <summary>Sets the window's icon to the XAMPP .ico, or else the .png; unchanged when neither exists.</summary>
    /// <param name="window">The window whose title bar and taskbar icon change.</param>
    public static void Apply(Window window)
    {
        if (LoadWindowIcon() is { } icon)
        {
            window.Icon = icon;
        }
    }

    /// <summary>Loads the XAMPP icon for a window or the tray, preferring the .ico with its several sizes.</summary>
    /// <returns>The icon; null when the build has neither file or it cannot be decoded.</returns>
    public static WindowIcon? LoadWindowIcon()
    {
        try
        {
            if (AssetLoader.Exists(_icoUri))
            {
                return new WindowIcon(AssetLoader.Open(_icoUri));
            }
            return HasLogo ? new WindowIcon(AssetLoader.Open(_pngUri)) : null;
        }
        catch
        {
            // Any failure to open or decode the asset is cosmetic: the default icon is used instead.
            return null;
        }
    }
}
