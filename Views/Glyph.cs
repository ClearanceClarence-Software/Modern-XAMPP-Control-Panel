using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace XamppControl.Views;

/// <summary>
/// One of the panel's own two-tone icons: a soft tint in the accent color under line work in the text color.
/// The geometries (in Styles/Theme.axaml) are drawn on a 24-unit grid; the control scales them to its size.
/// </summary>
public sealed class Glyph : Control
{
    /// <summary>The grid the icon geometries are drawn on.</summary>
    private const double Grid = 24;

    /// <summary>The line weight on that grid; about 1.1 px at the 16 px the buttons use.</summary>
    private const double LineWeight = 1.7;

    /// <summary>How strongly the accent color shows in the tinted area.</summary>
    private const double TintOpacity = 0.28;

    /// <summary>Defines <see cref="Data"/>.</summary>
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Glyph, Geometry?>(nameof(Data));

    /// <summary>Defines <see cref="Tint"/>.</summary>
    public static readonly StyledProperty<Geometry?> TintProperty =
        AvaloniaProperty.Register<Glyph, Geometry?>(nameof(Tint));

    /// <summary>Defines <see cref="Foreground"/>.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<Glyph, IBrush?>(nameof(Foreground));

    /// <summary>Defines <see cref="Accent"/>.</summary>
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<Glyph, IBrush?>(nameof(Accent));

    /// <summary>Defines <see cref="IsDuotone"/>.</summary>
    public static readonly StyledProperty<bool> IsDuotoneProperty =
        AvaloniaProperty.Register<Glyph, bool>(nameof(IsDuotone), defaultValue: true);

    static Glyph()
    {
        AffectsRender<Glyph>(DataProperty, TintProperty, ForegroundProperty, AccentProperty, IsDuotoneProperty);
    }

    /// <summary>
    /// True (the default) draws the tint under the lines; false draws the lines only. The theme binds it to the
    /// "Two-tone icons" setting.
    /// </summary>
    public bool IsDuotone
    {
        get => GetValue(IsDuotoneProperty);
        set => SetValue(IsDuotoneProperty, value);
    }

    /// <summary>The line work, stroked with <see cref="Foreground"/>.</summary>
    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>The area under the lines that is filled softly with <see cref="Accent"/>; null for none.</summary>
    public Geometry? Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <summary>The color of the lines.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The color of the tint.</summary>
    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Draws the tint, then the lines over it, scaled from the 24-unit grid and centered.</summary>
    /// <param name="context">The drawing context of this frame.</param>
    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        double scale = size / Grid;
        Matrix placement = Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using (context.PushTransform(placement))
        {
            if (IsDuotone && Tint is { } tint && Accent is { } accent)
            {
                using (context.PushOpacity(TintOpacity))
                {
                    context.DrawGeometry(accent, null, tint);
                }
            }

            if (Data is { } data && Foreground is { } foreground)
            {
                var pen = new Pen(foreground, LineWeight, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                context.DrawGeometry(null, pen, data);
            }
        }
    }
}
