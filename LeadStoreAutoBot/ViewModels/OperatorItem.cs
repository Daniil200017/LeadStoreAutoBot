using CommunityToolkit.Mvvm.ComponentModel;

namespace LeadStoreAutoBot.ViewModels;

public partial class OperatorItem : ObservableObject
{
    /// <summary>B1..B4 — текст галочки на сайте и ключ в конфиге.</summary>
    public string Prefix { get; }
    /// <summary>src-код оператора для API-режима (mt/bl/rt/...).</summary>
    public string Code { get; }
    /// <summary>Человекочитаемое имя, напр. "МТС".</summary>
    public string Label { get; }

    /// <summary>Подпись для UI: "B1 · МТС".</summary>
    public string Display => $"{Prefix} · {Label}";

    [ObservableProperty] private bool _isSelected;

    public OperatorItem(string prefix, string code, string label, bool isSelected)
    {
        Prefix = prefix;
        Code = code;
        Label = label;
        IsSelected = isSelected;
    }
}
