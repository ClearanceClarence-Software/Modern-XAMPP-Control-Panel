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

    public static bool HasLogo => AssetLoader.Exists(_pngUri);

    public static Bitmap? LoadLogo()
    {
        try
        {
            return HasLogo ? new Bitmap(AssetLoader.Open(_pngUri)) : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Apply(Window window)
    {
        try
        {
            if (AssetLoader.Exists(_icoUri))
                window.Icon = new WindowIcon(AssetLoader.Open(_icoUri));
            else if (HasLogo)
                window.Icon = new WindowIcon(AssetLoader.Open(_pngUri));
        }
        catch
        {
            // Keep the default icon.
        }
    }
}
