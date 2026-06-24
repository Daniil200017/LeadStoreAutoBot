using System.Text.Json.Serialization;

namespace LeadStoreAutoBot.Models;

/// <summary>
/// Структура bot_config.json — совместима с Python-версией.
/// </summary>
public class BotConfig
{
    [JsonPropertyName("theme")]      public string Theme { get; set; } = "dark";
    [JsonPropertyName("accent")]     public string Accent { get; set; } = "cyan";
    [JsonPropertyName("fontsize")]   public int FontSize { get; set; } = 9;
    [JsonPropertyName("lang")]       public string Lang { get; set; } = "ru";

    [JsonPropertyName("sound")]       public bool Sound { get; set; } = true;
    [JsonPropertyName("sound_done")]  public string SoundDone { get; set; } = "Успех 1";
    [JsonPropertyName("sound_error")] public string SoundError { get; set; } = "Ошибка 1";
    [JsonPropertyName("volume")]      public int Volume { get; set; } = 80;

    [JsonPropertyName("skip_dup")]   public bool SkipDuplicates { get; set; } = true;
    [JsonPropertyName("export_log")] public bool ExportLog { get; set; } = true;

    [JsonPropertyName("login")]      public string Login { get; set; } = "";
    [JsonPropertyName("password")]   public string Password { get; set; } = "";
    [JsonPropertyName("quick_url")]  public string QuickUrl { get; set; } = "";
    [JsonPropertyName("use_quick")]  public bool UseQuickUrl { get; set; }
    [JsonPropertyName("api_token")]  public string ApiToken { get; set; } = "";
    [JsonPropertyName("use_api")]    public bool UseApiMode { get; set; }

    [JsonPropertyName("use_manual_tbl")] public bool UseManualTable { get; set; }
    [JsonPropertyName("sheet")]      public string SheetUrl { get; set; } = "";

    [JsonPropertyName("tag")]        public string Tag { get; set; } = "";
    [JsonPropertyName("source")]     public string Source { get; set; } = "Звонки";
    [JsonPropertyName("limit")]      public string Limit { get; set; } = "10";
    [JsonPropertyName("site")]       public string Site { get; set; } = "LeadStore";
    [JsonPropertyName("range_from")] public string RangeFrom { get; set; } = "1";
    [JsonPropertyName("range_to")]   public string RangeTo { get; set; } = "";

    [JsonPropertyName("operators")]   public Dictionary<string, bool> Operators { get; set; } = new();
    [JsonPropertyName("days")]        public Dictionary<string, bool> Days { get; set; } = new();
    [JsonPropertyName("all_regions")] public bool AllRegions { get; set; } = true;
    [JsonPropertyName("regions")]     public Dictionary<string, bool> Regions { get; set; } = new();

    [JsonPropertyName("sheet_queue")] public List<string> SheetQueue { get; set; } = new();
}
