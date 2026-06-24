using System;
using System.IO;

namespace LeadStoreAutoBot.Services;

// Рабочая папка — папка реального .exe.
// Для single-file AppContext.BaseDirectory = временная папка, поэтому берём ProcessPath.
public static class AppPaths
{
    public static string BaseDirectory
    {
        get
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    var dir = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrEmpty(dir)) return dir;
                }
            }
            catch { }
            return AppContext.BaseDirectory;
        }
    }

    public static string ConfigPath  => Path.Combine(BaseDirectory, "bot_config.json");
    public static string HistoryPath => Path.Combine(BaseDirectory, "bot_history.json");
    public static string LogPath     => Path.Combine(BaseDirectory, "bot_log.txt");

    public static string OperatorsDetectPath => Path.Combine(BaseDirectory, "operators_detected.txt");
}
