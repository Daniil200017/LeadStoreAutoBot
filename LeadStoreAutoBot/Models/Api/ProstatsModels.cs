using System.Text.Json.Serialization;

namespace LeadStoreAutoBot.Models.Api;

/// <summary>Стандартный ответ prostats.info API: { "status": "success" | "error", "result": ..., "message": "..." }.</summary>
public class ApiResponse<T>
{
    [JsonPropertyName("status")]  public string Status  { get; set; } = "";
    [JsonPropertyName("result")]  public T?     Result  { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }

    public bool IsSuccess => Status == "success";
}

/// <summary>Проект из gck_projects.</summary>
public class ProstatsProject
{
    [JsonPropertyName("id")]      public int    Id      { get; set; }
    [JsonPropertyName("name")]    public string Name    { get; set; } = "";
    [JsonPropertyName("src")]     public string Src     { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("status")]  public int    Status  { get; set; }
    [JsonPropertyName("type")]    public string? Type   { get; set; }
}

/// <summary>Параметры команды gck_project_create.</summary>
public class ProjectCreateRequest
{
    [JsonPropertyName("command")]  public string Command  { get; set; } = "gck_project_create";
    [JsonPropertyName("type")]     public string Type     { get; set; } = "calls";   // hosts | calls
    [JsonPropertyName("src")]      public string Src      { get; set; } = "mt";      // mt | bl | rt
    [JsonPropertyName("name")]     public string Name     { get; set; } = "";
    [JsonPropertyName("limit")]    public int    Limit    { get; set; } = 10;
    [JsonPropertyName("content")]  public string Content  { get; set; } = "";
    [JsonPropertyName("status")]   public int    Status   { get; set; } = 1;
    [JsonPropertyName("tag")]      public string Tag      { get; set; } = "";
    [JsonPropertyName("workdays")] public string Workdays { get; set; } = "1234567";

    [JsonPropertyName("regions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int[]? Regions { get; set; }
}

/// <summary>Снэпшот существующих проектов — 4 индекса для разных уровней проверки дублей (как в Python).</summary>
public class ExistingProjectsSnapshot
{
    /// <summary>Полные имена как пришли (часто с префиксом B1_/B2_/B3_).</summary>
    public HashSet<string> FullNames { get; } = new();

    /// <summary>Имена без B-префикса — для проверки дублей по телефону/типу.</summary>
    public HashSet<string> BaseNames { get; } = new();

    /// <summary>(name, src) — точная проверка по оператору без зависимости от формата имени.</summary>
    public HashSet<(string Name, string Src)> NameSrc { get; } = new();

    /// <summary>(content, src) — самая надёжная: реальное содержимое + оператор.</summary>
    public HashSet<(string Content, string Src)> ContentSrc { get; } = new();
}
