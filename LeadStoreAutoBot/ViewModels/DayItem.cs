using CommunityToolkit.Mvvm.ComponentModel;

namespace LeadStoreAutoBot.ViewModels;

public partial class DayItem : ObservableObject
{
    public string Key { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isSelected;

    public DayItem(string key, string label, bool isSelected)
    {
        Key = key;
        Label = label;
        IsSelected = isSelected;
    }
}
