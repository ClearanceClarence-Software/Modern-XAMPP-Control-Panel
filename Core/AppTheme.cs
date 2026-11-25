using Avalonia;
using Avalonia.Styling;

namespace XamppControl.Core;

/// <summary>The panel's color themes and switching between them while it runs.</summary>
public static class AppTheme
{
    /// <summary>Follows the Windows setting (Settings › Personalization › Colors) and changes with it.</summary>
    public const string System = "System";

    /// <summary>Always light.</summary>
    public const string Light = "Light";

    /// <summary>Always dark.</summary>
    public const string Dark = "Dark";

    /// <summary>
    /// The application resource the icons read: true draws them two-tone, false as plain line icons.
    /// </summary>
    public const string DuotoneIconsKey = "IconsDuotone";

    /// <summary>Switches every icon between two-tone and plain; open windows repaint at once.</summary>
    /// <param name="isDuotone">True adds the soft accent wash under the lines; false draws the lines only.</param>
    public static void ApplyIcons(bool isDuotone)
    {
        if (Application.Current is { } application)
        {
            application.Resources[DuotoneIconsKey] = isDuotone;
        }
    }

    /// <summary>A stored theme name made safe to use.</summary>
    /// <param name="theme">The value from settings.json; null, empty or unknown values mean System.</param>
    /// <returns><see cref="Light"/>, <see cref="Dark"/> or <see cref="System"/>.</returns>
    public static string Normalize(string? theme)
    {
        if (string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase))
        {
            return Light;
        }
        return string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase) ? Dark : System;
    }

    /// <summary>Switches the whole app to a theme; open windows repaint at once.</summary>
    /// <param name="theme">One of the theme names; anything else follows Windows.</param>
    public static void Apply(string? theme)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = Normalize(theme) switch
        {
            Light => ThemeVariant.Light,
            Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
