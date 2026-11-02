using DrivenTestEngine.Core.Mvvm;

namespace DrivenTestEngine.App.ViewModels;

/// <summary>One chip in a segmented filter control.</summary>
public sealed class FilterOption : ObservableObject
{
    private bool _isSelected;

    public FilterOption(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
