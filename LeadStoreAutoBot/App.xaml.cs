using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using LeadStoreAutoBot.Services;

namespace LeadStoreAutoBot;

public partial class App : Application
{
    public static ConfigService        Config  { get; } = new();
    public static HistoryService       History { get; } = new();
    public static LogService           Log     { get; } = new();
    public static ThemeService         Theme   { get; } = new();
    public static GoogleSheetsService  Sheets  { get; } = new();
    public static ProstatsApi          Api     { get; } = new();
    public static BotRunner            Bot     { get; } = new(Api);
    public static SoundService         Sound   { get; } = new();

    public static Models.BotConfig CurrentConfig { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Диагностика операторов: LeadStoreAutoBot.exe --detect-operators
        if (e.Args.Any(a => a.Equals("--detect-operators", StringComparison.OrdinalIgnoreCase)))
        {
            RunOperatorDetection();
            Shutdown();
            return;
        }

        CurrentConfig = Config.Load();
        Theme.Apply(CurrentConfig.Theme, CurrentConfig.Accent);

        Log.Log($"Загружен конфиг: theme={CurrentConfig.Theme}, accent={CurrentConfig.Accent}", LogLevel.Dim);

        // Окно проверки обновлений — оно само откроет MainWindow.
        var splash = new UpdateWindow();
        splash.Show();
    }

    /// <summary>Диагностика src-кодов операторов.</summary>
    private static void RunOperatorDetection()
    {
        var sb = new StringBuilder();
        try
        {
            var cfg = Config.Load();
            var token = cfg.ApiToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                sb.AppendLine("ОШИБКА: в bot_config.json пустой api_token.");
            }
            else
            {
                var task = System.Threading.Tasks.Task.Run(() => Services.OperatorDetector.DetectAsync(Api, token));
                if (!task.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException("API не ответил за 60 сек.");
                var guesses = task.Result;
                if (guesses.Count == 0)
                {
                    sb.AppendLine("Не найдено проектов с префиксом B1_/B2_/B3_/B4_.");
                }
                else
                {
                    sb.AppendLine("Операторы, найденные на аккаунте:");
                    sb.AppendLine("");
                    foreach (var g in guesses)
                        sb.AppendLine($"{g.Prefix}   (проектов: {g.Count}, пример: {g.Example})");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ОШИБКА: " + ex.Message);
        }

        var text = sb.ToString();
        try { File.WriteAllText(Services.AppPaths.OperatorsDetectPath, text, Encoding.UTF8); } catch { }
        try { Console.Out.Write(text); Console.Out.Flush(); } catch { }
    }


    protected override void OnExit(ExitEventArgs e)
    {
        Config.Save(CurrentConfig);
        base.OnExit(e);
    }
}
