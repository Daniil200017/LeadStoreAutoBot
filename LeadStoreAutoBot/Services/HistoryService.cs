using System.IO;
using System.Text.Json;
using LeadStoreAutoBot.Models;

namespace LeadStoreAutoBot.Services;

/// <summary>История сессий — последние 50 запусков.</summary>
public class HistoryService
{
    private const int MaxRecords = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public List<SessionHistory> Load()
    {
        try
        {
            if (File.Exists(AppPaths.HistoryPath))
            {
                var json = File.ReadAllText(AppPaths.HistoryPath);
                var list = JsonSerializer.Deserialize<List<SessionHistory>>(json);
                if (list != null) return list;
            }
        }
        catch { }
        return new List<SessionHistory>();
    }

    public void Add(SessionHistory record)
    {
        var list = Load();
        list.Insert(0, record);
        if (list.Count > MaxRecords) list = list.GetRange(0, MaxRecords);
        try
        {
            File.WriteAllText(AppPaths.HistoryPath, JsonSerializer.Serialize(list, JsonOptions));
        }
        catch { }
    }
}
