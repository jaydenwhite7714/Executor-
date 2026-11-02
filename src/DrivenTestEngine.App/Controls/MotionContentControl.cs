using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DrivenTestEngine.App.Infrastructure;

namespace DrivenTestEngine.App.Controls;

/// <summary>Content host that fades and slides each new page in (skipped when motion is reduced).</summary>
public sealed class MotionContentControl : ContentControl
{
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(210));
    private readonly TranslateTransform _translate = new(0, 0);

    public MotionContentControl()
    {
        RenderTransform = _translate;
        ClipToBounds = true;
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        if (newContent is null)
        {
            return;
        }

        if (!MotionSettings.Enabled)
        {
            _translate.BeginAnimation(TranslateTransform.XProperty, null);
            BeginAnimation(OpacityProperty, null);
            _translate.X = 0;
            Opacity = 1;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _translate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(14, 0, SlideDuration) { EasingFunction = ease });
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.35, 1, SlideDuration) { EasingFunction = ease });
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ClipToBounds = true;
    }
}
