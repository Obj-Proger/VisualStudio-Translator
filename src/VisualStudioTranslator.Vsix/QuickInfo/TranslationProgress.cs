using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.VisualStudio.PlatformUI;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>
/// The strip under a tooltip whose translation is on its way: a thin track with a short stretch of the accent colour
/// sliding along it, the way Visual Studio shows work in progress. Colours come from the current theme. Where
/// Windows is set to show no animations, the stretch stands still.
/// </summary>
internal sealed class TranslationProgress : Grid
{
    private static readonly Duration Pass = new(TimeSpan.FromSeconds(1.2));

    private readonly Rectangle _stretch;
    private readonly TranslateTransform _offset = new();

    public TranslationProgress()
    {
        Height = 2;
        MinWidth = 160;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;

        Border track = new() { Opacity = 0.6 };
        track.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolTipBorderBrushKey);
        Children.Add(track);

        _stretch = new Rectangle { HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = _offset };
        _stretch.SetResourceReference(Shape.FillProperty, EnvironmentColors.SystemHighlightBrushKey);
        Children.Add(_stretch);

        SizeChanged += (_, _) => Animate();
        Unloaded += (_, _) => _offset.BeginAnimation(TranslateTransform.XProperty, null);
    }

    private void Animate()
    {
        double width = ActualWidth;
        _stretch.Width = width * 0.3;

        if (!SystemParameters.ClientAreaAnimation)
        {
            _offset.BeginAnimation(TranslateTransform.XProperty, null);
            _offset.X = width * 0.35;
            return;
        }

        DoubleAnimation slide = new(-_stretch.Width, width, Pass)
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };

        _offset.BeginAnimation(TranslateTransform.XProperty, slide);
    }
}