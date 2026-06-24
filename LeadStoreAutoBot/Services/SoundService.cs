using System.IO;
using System.Media;
using NAudio.Wave;

namespace LeadStoreAutoBot.Services;

/// <summary>
/// Воспроизведение WAV из embedded resources. 13 пресетов (12 файлов + "без звука") как в Python.
/// Громкость 0-100. Используем NAudio для контроля громкости (SoundPlayer не умеет).
/// </summary>
public class SoundService
{
    /// <summary>Имена → файл WAV (имя файла в Resources/Sounds).</summary>
    public static readonly Dictionary<string, string> Sounds = new()
    {
        { "✅ Успех 1 (аккорд)",   "sound_done.wav" },
        { "✅ Успех 2 (трель)",    "snd_done2.wav" },
        { "✅ Успех 3 (динь)",     "snd_done3.wav" },
        { "✅ Успех 4 (два пинга)","snd_done4.wav" },
        { "✅ Успех 5 (До-Ре-Ми)", "snd_done5.wav" },
        { "❌ Ошибка 1 (мягкая)",  "sound_error.wav" },
        { "❌ Ошибка 2 (два бум)", "snd_err2.wav" },
        { "❌ Ошибка 3 (трель)",   "snd_err3.wav" },
        { "❌ Ошибка 4 (бум)",     "snd_err4.wav" },
        { "🔔 Сигнал",             "snd_signal.wav" },
        { "🔔 Пинг",               "snd_ping.wav" },
        { "🔔 Тихий",              "snd_quiet.wav" },
        { "🔕 Без звука",          "" },
    };

    /// <summary>Воспроизвести по имени пресета. Громкость 0-100.</summary>
    public void Play(string presetName, int volumePct)
    {
        if (!App.CurrentConfig.Sound) return;
        if (string.IsNullOrEmpty(presetName)) return;
        if (!Sounds.TryGetValue(presetName, out var fname) || string.IsNullOrEmpty(fname)) return;

        var path = Path.Combine(AppPaths.BaseDirectory, "Resources", "Sounds", fname);
        if (!File.Exists(path)) return;

        var vol = Math.Clamp(volumePct / 100f, 0f, 1f);

        // Запускаем в отдельном потоке чтобы не блокировать UI
        Task.Run(() =>
        {
            try
            {
                using var reader = new WaveFileReader(path);
                using var output = new WaveOutEvent { Volume = vol };
                output.Init(reader);
                output.Play();
                while (output.PlaybackState == PlaybackState.Playing)
                    Thread.Sleep(50);
            }
            catch
            {
                // Падаем тихо — звук не критично
            }
        });
    }

    /// <summary>Удобный шорткат — взять пресет из конфига.</summary>
    public void PlayDone() => Play(App.CurrentConfig.SoundDone, App.CurrentConfig.Volume);
    public void PlayError() => Play(App.CurrentConfig.SoundError, App.CurrentConfig.Volume);
}
