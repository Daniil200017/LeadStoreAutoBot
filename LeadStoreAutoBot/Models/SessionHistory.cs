using System.Text.Json.Serialization;

namespace LeadStoreAutoBot.Models;

/// <summary>Запись истории работы (bot_history.json).</summary>
public class SessionHistory
{
    [JsonPropertyName("date")]    public string Date { get; set; } = "";
    [JsonPropertyName("site")]    public string Site { get; set; } = "";
    [JsonPropertyName("mode")]    public string Mode { get; set; } = "";   // "API" / "Selenium"
    [JsonPropertyName("created")] public int Created { get; set; }
    [JsonPropertyName("skipped")] public int Skipped { get; set; }
    [JsonPropertyName("errors")]  public int Errors { get; set; }
    [JsonPropertyName("elapsed")] public int ElapsedSeconds { get; set; }

    /// <summary>Подробный отчёт: какие проекты созданы, какие пропущены, какие упали.</summary>
    [JsonPropertyName("details")] public string Details { get; set; } = "";
}
