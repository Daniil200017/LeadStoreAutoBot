using CommunityToolkit.Mvvm.ComponentModel;

namespace LeadStoreAutoBot.Models;

/// <summary>
/// Строка таблицы данных. 3 колонки: Сайт, Тел. О (op), Тел. М (mg).
/// Как в исходной Python-версии (COL_KEYS = ["site", "op", "mg"]).
/// </summary>
public partial class PhoneRecord : ObservableObject
{
    [ObservableProperty] private int _rowNum;
    [ObservableProperty] private string _site = "";
    [ObservableProperty] private string _op = "";
    [ObservableProperty] private string _mg = "";

    /// <summary>"" / "wait" / "active" / "ok" / "err" / "skip" / "dup" — для подсветки строки.</summary>
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>true если в строке есть какие-то данные.</summary>
    public bool HasData => !string.IsNullOrWhiteSpace(Site)
                        || !string.IsNullOrWhiteSpace(Op)
                        || !string.IsNullOrWhiteSpace(Mg);
}
