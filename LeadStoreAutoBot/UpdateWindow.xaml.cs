using System;
using System.Threading.Tasks;
using System.Windows;
using LeadStoreAutoBot.Services;

namespace LeadStoreAutoBot;

public partial class UpdateWindow : Window
{
    public UpdateWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Версия {UpdateService.CurrentVersion}";
        Loaded += async (_, __) => await RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            SetStatus("Проверка обновлений...", true);
            var info = await UpdateService.CheckForUpdateAsync();

            if (info == null)
            {
                LaunchMainAndClose();
                return;
            }

            SetStatus($"Найдено обновление {info.Latest}. Скачивание...", false);
            var progress = new Progress<int>(p =>
            {
                if (p < 0) { Bar.IsIndeterminate = true; }
                else { Bar.IsIndeterminate = false; Bar.Value = p; }
            });

            var newExe = await UpdateService.DownloadAsync(info.DownloadUrl, progress);

            SetStatus("Установка обновления...", true);
            UpdateService.ApplyUpdateAndRestart(newExe);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            try { App.Log.Log("Обновление не удалось: " + ex.Message, LogLevel.Warn); } catch { }
            LaunchMainAndClose();
        }
    }

    private void SetStatus(string text, bool indeterminate)
    {
        StatusText.Text = text;
        Bar.IsIndeterminate = indeterminate;
    }

    private void LaunchMainAndClose()
    {
        var main = new MainWindow();
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }
}
