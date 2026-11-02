using System.Windows.Media;
using DrivenTestEngine.App.Infrastructure;
using DrivenTestEngine.Core.Mvvm;

namespace DrivenTestEngine.App.ViewModels;

public sealed class NavItem : ObservableObject
{
    private bool _isActive;

    public NavItem(string key, string label, string iconKey)
    {
        Key = key;
        Label = label;
        IconKey = iconKey;
    }

    public string Key { get; }

    public string Label { get; }

    public string IconKey { get; }

    public Geometry? IconGeometry => IconLibrary.Get(IconKey);

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
