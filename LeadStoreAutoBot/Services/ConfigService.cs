using System.IO;
using System.Text.Json;
using LeadStoreAutoBot.Models;
using LeadStoreAutoBot.Resources;

namespace LeadStoreAutoBot.Services;

/// <summary>Загрузка/сохранение bot_config.json (совместим с Python-версией).</summary>
public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public BotConfig Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigPath))
            {
                var json = File.ReadAllText(AppPaths.ConfigPath);
                var cfg = JsonSerializer.Deserialize<BotConfig>(json);
                if (cfg != null) return EnsureDefaults(cfg);
            }
        }
        catch
        {
            // Поврежденный конфиг — возвращаем дефолтный.
        }
        return EnsureDefaults(new BotConfig());
    }

    public void Save(BotConfig cfg)
    {
        try
        {
            var json = JsonSerializer.Serialize(cfg, JsonOptions);
            File.WriteAllText(AppPaths.ConfigPath, json);
        }
        catch
        {
            // Не критично — продолжаем без сохранения.
        }
    }

    private static BotConfig EnsureDefaults(BotConfig cfg)
    {
        if (cfg.Days.Count == 0)
        {
            foreach (var d in Constants.AllDays) cfg.Days[d] = true;
        }
        if (cfg.Regions.Count == 0)
        {
            foreach (var r in Constants.AllRegionsOrdered) cfg.Regions[r] = false;
        }
        if (string.IsNullOrEmpty(cfg.Tag))
        {
            cfg.Tag = DateTime.Now.ToString("dd.MM");
        }
        return cfg;
    }
}
