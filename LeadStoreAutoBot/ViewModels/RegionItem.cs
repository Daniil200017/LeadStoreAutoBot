using CommunityToolkit.Mvvm.ComponentModel;

namespace LeadStoreAutoBot.ViewModels;

public partial class RegionItem : ObservableObject
{
    public string Name { get; }
    public int Code { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible = true;

    public RegionItem(string name, int code, bool isSelected)
    {
        Name = name;
        Code = code;
        IsSelected = isSelected;
    }
}
