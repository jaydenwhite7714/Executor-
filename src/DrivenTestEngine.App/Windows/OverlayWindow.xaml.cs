using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DrivenTestEngine.App.Infrastructure;
using DrivenTestEngine.App.Services;
using DrivenTestEngine.App.ViewModels;

namespace DrivenTestEngine.App.Windows;

/// <summary>
/// The overlay shell. A normal borderless WPF window: topmost by choice, dragged by its title bar,
/// resizable from every edge, and never attached to another process.
/// </summary>
public partial class OverlayWindow : Window
{
    private const double SnapThreshold = 26;

    private readonly ShellViewModel _shell;
    private readonly AppServices _services;
    private readonly DispatcherTimer _saveTimer;
    private bool _restoring;
    private bool _closing;

    public OverlayWindow(ShellViewModel shell, AppServices services)
    {
        _shell = shell;
        _services = services;

        InitializeComponent();

        DataContext = shell;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(650),
        };
        _saveTimer.Tick += OnSaveTick;

        SourceInitialized += (_, _) => WindowEffects.Apply(this);
        LocationChanged += (_, _) => OnGeometryChanged();
        SizeChanged += (_, _) => OnGeometryChanged();
        Closing += OnClosing;

        _shell.CollapseRequested += (_, _) => CollapseRequested?.Invoke(this, EventArgs.Empty);
        _shell.CloseRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CollapseRequested;

    public event EventHandler? CloseRequested;

    /// <summary>Applies saved size/position, clamped to the current work area.</summary>
    public void RestoreGeometry()
    {
        _restoring = true;
        try
        {
            var settings = _services.Settings.Current;
            var area = SystemParameters.WorkArea;

            Width = Math.Clamp(settings.OverlayWidth, MinWidth, Math.Max(MinWidth, area.Width - 40));
            Height = Math.Clamp(settings.OverlayHeight, MinHeight, Math.Max(MinHeight, area.Height - 40));

            var defaultLeft = area.Right - Width - 14;
            var defaultTop = area.Top + Math.Max(40, (area.Height - Height) / 2);
            var left = settings.OverlayLeft ?? defaultLeft;
            var top = settings.OverlayTop ?? defaultTop;

            Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        }
        finally
        {
            _restoring = false;
        }
    }

    public void SaveGeometry()
    {
        if (_restoring)
        {
            return;
        }

        var left = Left;
        var top = Top;
        var width = Width;
        var height = Height;

        _services.Settings.Update(s =>
        {
            s.OverlayLeft = left;
            s.OverlayTop = top;
            s.OverlayWidth = width;
            s.OverlayHeight = height;
        });
    }

    /// <summary>Sticks the window to a screen edge when dropped close to it.</summary>
    public void SnapToEdges()
    {
        var area = SystemParameters.WorkArea;
        var left = Left;
        var top = Top;

        if (Math.Abs(left + Width - area.Right) <= SnapThreshold)
        {
            left = area.Right - Width;
        }
        else if (Math.Abs(left - area.Left) <= SnapThreshold)
        {
            left = area.Left;
        }

        if (Math.Abs(top - area.Top) <= SnapThreshold)
        {
            top = area.Top;
        }
        else if (Math.Abs(top + Height - area.Bottom) <= SnapThreshold)
        {
            top = area.Bottom - Height;
        }

        if (!left.Equals(Left))
        {
            Left = left;
        }

        if (!top.Equals(Top))
        {
            Top = top;
        }
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        SnapToEdges();
        ScheduleSave();
    }

    private void OnGeometryChanged()
    {
        if (_restoring || _closing)
        {
            return;
        }

        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        SaveGeometry();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _saveTimer.Stop();
        SaveGeometry();
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
