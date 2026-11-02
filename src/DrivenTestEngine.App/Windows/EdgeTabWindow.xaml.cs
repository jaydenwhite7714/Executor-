using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using DrivenTestEngine.App.Infrastructure;

namespace DrivenTestEngine.App.Windows;

/// <summary>The small handle that stays on the right edge of the screen while the overlay is hidden.</summary>
public partial class EdgeTabWindow : Window
{
    public EdgeTabWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => AnimateIn();
    }

    public event EventHandler? ExpandRequested;

    /// <summary>Pins the tab to the right edge of the work area, vertically centred on the overlay.</summary>
    public void Place(double referenceTop, double referenceHeight)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width;
        Top = Math.Clamp(referenceTop + ((referenceHeight - Height) / 2), area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ExpandRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AnimateIn()
    {
        if (!MotionSettings.Enabled)
        {
            TabChrome.Opacity = 1;
            return;
        }

        TabChrome.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }
}
