namespace LeadStoreAutoBot.Models;

public record ThemePreset(string Key, string DisplayName, string Bg, string Bg2, string Bg3, string Text, string Text2, string Border, bool IsDark);

public record AccentPreset(string Key, string DisplayName, string Accent, string Accent2);
