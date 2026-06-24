using System.Windows;
using System.Windows.Media;
using LeadStoreAutoBot.Models;

namespace LeadStoreAutoBot.Services;

/// <summary>10 тем + 6 акцентов из Python-версии. Прокидывает цвета в Application.Resources.</summary>
public class ThemeService
{
    public static readonly ThemePreset[] Themes =
    {
        new("dark",       "Тёмная",       "#0d1117", "#161b22", "#21262d", "#e6edf3", "#8b949e", "#30363d", true),
        new("darker",     "Чёрная",       "#080c10", "#0d1117", "#161b22", "#e6edf3", "#8b949e", "#21262d", true),
        new("midnight",   "Полночь",      "#0a0e1a", "#111827", "#1f2937", "#e2e8f0", "#94a3b8", "#374151", true),
        new("slate",      "Сланец",       "#1e293b", "#0f172a", "#334155", "#f1f5f9", "#94a3b8", "#475569", true),
        new("forest",     "Лес",          "#0f1f0f", "#162616", "#1e3a1e", "#e8f5e8", "#86a886", "#2d5a2d", true),
        new("light",      "Светлая",      "#ffffff", "#f6f8fa", "#e8ecf0", "#1a1a2e", "#57606a", "#d0d7de", false),
        new("light_gray", "Светло-серая", "#f0f2f5", "#e4e8ee", "#d1d8e3", "#1a1a2e", "#4a5568", "#b8c2cc", false),
        new("cream",      "Кремовая",     "#fdf6ec", "#f5ede0", "#ede0ce", "#2d1b0e", "#6b4e37", "#d4b896", false),
        new("paper",      "Бумага",       "#fafafa", "#f3f4f6", "#e5e7eb", "#111827", "#6b7280", "#d1d5db", false),
        new("arctic",     "Арктика",      "#f0f8ff", "#e8f4fd", "#d6eaf8", "#0d2137", "#3d6987", "#a9cce3", false),
    };

    public static readonly AccentPreset[] Accents =
    {
        new("cyan",   "Бирюзовый",  "#00d4aa", "#0099ff"),
        new("blue",   "Синий",       "#3b82f6", "#60a5fa"),
        new("purple", "Фиолетовый", "#a855f7", "#c084fc"),
        new("orange", "Оранжевый",  "#f97316", "#fb923c"),
        new("green",  "Зелёный",    "#22c55e", "#4ade80"),
        new("red",    "Красный",    "#ef4444", "#f87171"),
    };

    public ThemePreset CurrentTheme { get; private set; } = Themes[0];
    public AccentPreset CurrentAccent { get; private set; } = Accents[0];

    public event Action? ThemeChanged;

    public void Apply(string themeKey, string accentKey)
    {
        var theme  = Array.Find(Themes,  t => t.Key == themeKey)  ?? Themes[0];
        var accent = Array.Find(Accents, a => a.Key == accentKey) ?? Accents[0];
        CurrentTheme = theme;
        CurrentAccent = accent;

        var res = Application.Current.Resources;
        res["BgBrush"]      = Brush(theme.Bg);
        res["Bg2Brush"]     = Brush(theme.Bg2);
        res["Bg3Brush"]     = Brush(theme.Bg3);
        res["TextBrush"]    = Brush(theme.Text);
        res["Text2Brush"]   = Brush(theme.Text2);
        res["BorderBrush"]  = Brush(theme.Border);
        res["AccentBrush"]  = Brush(accent.Accent);
        res["Accent2Brush"] = Brush(accent.Accent2);
        res["RedBrush"]     = Brush("#ff4757");
        res["YellowBrush"]  = Brush("#ffd32a");
        res["IsDark"]       = theme.IsDark;

        ThemeChanged?.Invoke();
    }

    private static SolidColorBrush Brush(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
