using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadStoreAutoBot.Models;
using LeadStoreAutoBot.Resources;
using LeadStoreAutoBot.Services;
using ApiModels = LeadStoreAutoBot.Models.Api;

namespace LeadStoreAutoBot.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly BotConfig _cfg;

    public const string QuickLoginOption = "⚡ Быстрый вход";
    public const string ApiModeOption    = "🚀 API режим";

    public MainViewModel()
    {
        _cfg = App.CurrentConfig;

        // Тег — всегда актуальная дата при запуске (как в Python)
        _cfg.Tag = DateTime.Now.ToString("dd.MM");
        _tag = _cfg.Tag;

        // Сайты + спец-режимы (как в Python)
        SiteNames = new ObservableCollection<string>(Constants.Sites.Keys);
        SiteNames.Add(QuickLoginOption);
        SiteNames.Add(ApiModeOption);

        // Восстанавливаем выбранный пункт по флагам
        if (_cfg.UseApiMode)        _selectedSite = ApiModeOption;
        else if (_cfg.UseQuickUrl)  _selectedSite = QuickLoginOption;
        else                        _selectedSite = _cfg.Site;

        // Источники
        SourceNames = new ObservableCollection<string> { "Сайты", "Звонки" };

        // Дни недели
        Days = new ObservableCollection<DayItem>(
            Constants.AllDays.Select(d => new DayItem(d, Constants.DayLabels[d], _cfg.Days.GetValueOrDefault(d, true)))
        );
        foreach (var d in Days) d.PropertyChanged += (_, __) => SaveConfig();

        // Операторы (B1..B4) — выбор пользователя, по умолчанию B1/B2/B3
        Operators = new ObservableCollection<OperatorItem>(
            Constants.AllOperators.Select(o =>
                new OperatorItem(o.Prefix, o.Code, o.Label,
                    _cfg.Operators.GetValueOrDefault(o.Prefix, o.DefaultEnabled)))
        );
        foreach (var op in Operators) op.PropertyChanged += (_, __) => SaveOperators();

        // Регионы (в порядке Python-версии)
        Regions = new ObservableCollection<RegionItem>(
            Constants.AllRegionsOrdered
                .Where(r => Constants.RegionCodes.ContainsKey(r))
                .Select(r => new RegionItem(r, Constants.RegionCodes[r], _cfg.Regions.GetValueOrDefault(r, false)))
        );
        foreach (var r in Regions) r.PropertyChanged += (_, __) => SaveConfig();

        // Темы и акценты
        Themes = new ObservableCollection<ThemePreset>(ThemeService.Themes);
        Accents = new ObservableCollection<AccentPreset>(ThemeService.Accents);

        // Звуки
        SoundDonePresets  = new ObservableCollection<string>(SoundService.Sounds.Keys);
        SoundErrorPresets = new ObservableCollection<string>(SoundService.Sounds.Keys);

        // Подключение к логам
        App.Log.EntryAdded += OnLogEntry;

        // 50 пустых строк в таблице по умолчанию (Excel-like)
        EnsureEmptyRows();

        // Базовый приветственный лог
        App.Log.Log("Готов к работе.", LogLevel.Ok);
    }

    // ── Платформа ────────────────────────────────────────
    public ObservableCollection<string> SiteNames { get; }

    [ObservableProperty] private string _selectedSite = "";
    partial void OnSelectedSiteChanged(string value)
    {
        if (value == QuickLoginOption)
        {
            UseQuickUrl = true;
            UseApiMode = false;
        }
        else if (value == ApiModeOption)
        {
            UseApiMode = true;
            UseQuickUrl = false;
        }
        else
        {
            UseQuickUrl = false;
            UseApiMode = false;
            _cfg.Site = value;
        }
        SaveConfig();
    }

    // ── Авторизация ──────────────────────────────────────
    [ObservableProperty] private string _login = App.CurrentConfig.Login;
    partial void OnLoginChanged(string value) { _cfg.Login = value; SaveConfig(); }

    [ObservableProperty] private string _password = App.CurrentConfig.Password;
    partial void OnPasswordChanged(string value) { _cfg.Password = value; SaveConfig(); }

    [ObservableProperty] private string _quickUrl = App.CurrentConfig.QuickUrl;
    partial void OnQuickUrlChanged(string value) { _cfg.QuickUrl = value; SaveConfig(); }

    [ObservableProperty] private bool _useQuickUrl = App.CurrentConfig.UseQuickUrl;
    partial void OnUseQuickUrlChanged(bool value) { _cfg.UseQuickUrl = value; SaveConfig(); }

    [ObservableProperty] private string _apiToken = App.CurrentConfig.ApiToken;
    partial void OnApiTokenChanged(string value) { _cfg.ApiToken = value; SaveConfig(); }

    [ObservableProperty] private bool _useApiMode = App.CurrentConfig.UseApiMode;
    partial void OnUseApiModeChanged(bool value) { _cfg.UseApiMode = value; SaveConfig(); }

    // ── Источник данных ──────────────────────────────────
    [ObservableProperty] private bool _useManualTable = App.CurrentConfig.UseManualTable;
    partial void OnUseManualTableChanged(bool value) { _cfg.UseManualTable = value; SaveConfig(); }

    [ObservableProperty] private string _sheetUrl = App.CurrentConfig.SheetUrl;
    partial void OnSheetUrlChanged(string value) { _cfg.SheetUrl = value; SaveConfig(); }

    // ── Параметры проекта ────────────────────────────────
    [ObservableProperty] private string _tag = App.CurrentConfig.Tag;
    partial void OnTagChanged(string value) { _cfg.Tag = value; SaveConfig(); }

    public ObservableCollection<string> SourceNames { get; }

    [ObservableProperty] private string _selectedSource = App.CurrentConfig.Source;
    partial void OnSelectedSourceChanged(string value) { _cfg.Source = value; SaveConfig(); }

    [ObservableProperty] private string _limit = App.CurrentConfig.Limit;
    partial void OnLimitChanged(string value) { _cfg.Limit = value; SaveConfig(); }

    [ObservableProperty] private string _rangeFrom = App.CurrentConfig.RangeFrom;
    partial void OnRangeFromChanged(string value) { _cfg.RangeFrom = value; SaveConfig(); }

    [ObservableProperty] private string _rangeTo = App.CurrentConfig.RangeTo;
    partial void OnRangeToChanged(string value) { _cfg.RangeTo = value; SaveConfig(); }

    // ── Дни и регионы ────────────────────────────────────
    public ObservableCollection<DayItem> Days { get; }

    /// <summary>Операторы (B1..B4), которых можно вкл/выкл на главном экране.</summary>
    public ObservableCollection<OperatorItem> Operators { get; }

    [ObservableProperty] private bool _allRegions = App.CurrentConfig.AllRegions;
    partial void OnAllRegionsChanged(bool value) { _cfg.AllRegions = value; SaveConfig(); }

    public ObservableCollection<RegionItem> Regions { get; }

    [ObservableProperty] private string _regionFilter = "";
    partial void OnRegionFilterChanged(string value)
    {
        var f = (value ?? "").Trim().ToLowerInvariant();
        foreach (var r in Regions)
            r.IsVisible = string.IsNullOrEmpty(f) || r.Name.ToLowerInvariant().Contains(f);
    }

    [RelayCommand]
    private void SelectAllRegions()
    {
        foreach (var r in Regions)
            if (r.IsVisible) r.IsSelected = true;
    }

    [RelayCommand]
    private void ClearAllRegions()
    {
        foreach (var r in Regions)
            if (r.IsVisible) r.IsSelected = false;
    }

    // ── Настройки (вкладка) ──────────────────────────────
    public ObservableCollection<ThemePreset> Themes { get; }
    public ObservableCollection<AccentPreset> Accents { get; }

    [ObservableProperty] private ThemePreset _selectedTheme =
        ThemeService.Themes.FirstOrDefault(t => t.Key == App.CurrentConfig.Theme) ?? ThemeService.Themes[0];
    partial void OnSelectedThemeChanged(ThemePreset value)
    {
        _cfg.Theme = value.Key;
        App.Theme.Apply(_cfg.Theme, _cfg.Accent);
        SaveConfig();
    }

    [ObservableProperty] private AccentPreset _selectedAccent =
        ThemeService.Accents.FirstOrDefault(a => a.Key == App.CurrentConfig.Accent) ?? ThemeService.Accents[0];
    partial void OnSelectedAccentChanged(AccentPreset value)
    {
        _cfg.Accent = value.Key;
        App.Theme.Apply(_cfg.Theme, _cfg.Accent);
        SaveConfig();
    }

    [ObservableProperty] private bool _soundEnabled = App.CurrentConfig.Sound;
    partial void OnSoundEnabledChanged(bool value) { _cfg.Sound = value; SaveConfig(); }

    [ObservableProperty] private int _volume = App.CurrentConfig.Volume;
    partial void OnVolumeChanged(int value) { _cfg.Volume = value; SaveConfig(); }

    public ObservableCollection<string> SoundDonePresets  { get; }
    public ObservableCollection<string> SoundErrorPresets { get; }

    [ObservableProperty] private string _selectedSoundDone = App.CurrentConfig.SoundDone;
    partial void OnSelectedSoundDoneChanged(string value)
    {
        _cfg.SoundDone = value; SaveConfig();
        App.Sound.Play(value, _cfg.Volume); // превью
    }

    [ObservableProperty] private string _selectedSoundError = App.CurrentConfig.SoundError;
    partial void OnSelectedSoundErrorChanged(string value)
    {
        _cfg.SoundError = value; SaveConfig();
        App.Sound.Play(value, _cfg.Volume);
    }

    [ObservableProperty] private bool _skipDuplicates = App.CurrentConfig.SkipDuplicates;
    partial void OnSkipDuplicatesChanged(bool value) { _cfg.SkipDuplicates = value; SaveConfig(); }

    [ObservableProperty] private bool _exportLog = App.CurrentConfig.ExportLog;
    partial void OnExportLogChanged(bool value) { _cfg.ExportLog = value; SaveConfig(); }

    // ── Таблица ──────────────────────────────────────────
    public ObservableCollection<PhoneRecord> Records { get; } = new();

    public const int MinDisplayRows = 50;

    /// <summary>Наполняет таблицу пустыми строками до MinDisplayRows.</summary>
    public void EnsureEmptyRows()
    {
        while (Records.Count < MinDisplayRows)
        {
            var rec = new PhoneRecord { RowNum = Records.Count + 1 };
            rec.PropertyChanged += Record_PropertyChanged;
            Records.Add(rec);
        }
    }

    /// <summary>Когда пользователь редактирует одну из последних строк — добавляем ещё пустых.</summary>
    private void Record_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PhoneRecord.Site)
                            or nameof(PhoneRecord.Op)
                            or nameof(PhoneRecord.Mg))
        {
            // если последние 5 строк не пустые — добавляем ещё 10
            int tailEmpty = 0;
            for (int i = Records.Count - 1; i >= 0 && tailEmpty < 5; i--)
            {
                if (Records[i].HasData) break;
                tailEmpty++;
            }
            if (tailEmpty < 5)
            {
                for (int k = 0; k < 10; k++)
                {
                    var rec = new PhoneRecord { RowNum = Records.Count + 1 };
                    rec.PropertyChanged += Record_PropertyChanged;
                    Records.Add(rec);
                }
            }
        }
    }

    [RelayCommand]
    private void AddManualRow()
    {
        var rec = new PhoneRecord { RowNum = Records.Count + 1 };
        rec.PropertyChanged += Record_PropertyChanged;
        Records.Add(rec);
    }

    [RelayCommand]
    private void ClearRecords()
    {
        Records.Clear();
        EnsureEmptyRows();
    }

    [ObservableProperty] private bool _isLoadingSheet;

    [RelayCommand]
    private async Task LoadFromSheetsAsync()
    {
        if (UseManualTable)
        {
            App.Log.Log("Включён режим ручной таблицы — загрузка из Sheets отключена.", LogLevel.Warn);
            return;
        }
        var url = SheetUrl?.Trim() ?? "";
        if (string.IsNullOrEmpty(url))
        {
            App.Log.Log("Введите ссылку на Google Таблицу.", LogLevel.Err);
            return;
        }

        IsLoadingSheet = true;
        try
        {
            App.Log.Log($"📥 Загружаю Google Таблицу...", LogLevel.Bold);

            void OnProgress(string s) => App.Log.Log("  → " + s, LogLevel.Dim);

            // Жёсткий таймаут 60 секунд — отменяет HTTP-запрос внутри Google.Apis
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
            var loaded = await App.Sheets.LoadPhonesAsync(url, OnProgress, cts.Token);

            foreach (var r in Records) r.PropertyChanged -= Record_PropertyChanged;
            Records.Clear();

            foreach (var r in loaded)
            {
                r.PropertyChanged += Record_PropertyChanged;
                Records.Add(r);
            }
            EnsureEmptyRows();

            int withPhone = loaded.Count(r => r.Status != "no_site");
            int noSite    = loaded.Count(r => r.Status == "no_site");
            App.Log.Log($"✅ Загружено: {withPhone} строк с источником, {noSite} без источника", LogLevel.Ok);
        }
        catch (OperationCanceledException)
        {
            App.Log.Log("❌ Таймаут 60 секунд — Google не ответил. Проверь интернет/доступ к таблице.", LogLevel.Err);
        }
        catch (Exception ex)
        {
            App.Log.Log($"❌ Ошибка загрузки: {ex.GetType().Name}: {ex.Message}", LogLevel.Err);
            var inner = ex.InnerException;
            int depth = 1;
            while (inner != null && depth < 5)
            {
                App.Log.Log($"   └ inner #{depth}: {inner.GetType().Name}: {inner.Message}", LogLevel.Err);
                inner = inner.InnerException;
                depth++;
            }
        }
        finally
        {
            IsLoadingSheet = false;
        }
    }

    // ── История ──────────────────────────────────────────
    public ObservableCollection<SessionHistory> History { get; } = new();
    private List<SessionHistory> _historyAll = new();

    [ObservableProperty] private SessionHistory? _selectedHistory;
    [ObservableProperty] private string _historySearch = "";
    partial void OnHistorySearchChanged(string value)
    {
        var f = (value ?? "").Trim().ToLowerInvariant();
        History.Clear();
        var src = string.IsNullOrEmpty(f)
            ? _historyAll
            : _historyAll.Where(h =>
                  h.Date.ToLowerInvariant().Contains(f) ||
                  h.Site.ToLowerInvariant().Contains(f) ||
                  h.Mode.ToLowerInvariant().Contains(f) ||
                  h.Details.ToLowerInvariant().Contains(f));
        foreach (var h in src) History.Add(h);
    }

    public void RefreshHistory()
    {
        _historyAll = App.History.Load();
        History.Clear();
        foreach (var h in _historyAll) History.Add(h);
        if (!string.IsNullOrEmpty(HistorySearch))
            OnHistorySearchChanged(HistorySearch);
    }

    // ── Статистика и состояние работы ───────────────────
    [ObservableProperty] private int _statCreated;
    [ObservableProperty] private int _statSkipped;
    [ObservableProperty] private int _statErrors;
    [ObservableProperty] private string _timerText = "⏱ 00:00";
    [ObservableProperty] private string _etaText = "";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _statusText = "Готов";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isPaused;

    /// <summary>
    /// Сейчас обрабатываемая строка (последняя для которой пришёл status="active").
    /// Используется TableTab чтобы скроллить DataGrid к этой строке.
    /// </summary>
    [ObservableProperty] private PhoneRecord? _activeRecord;

    private DateTime? _runStartedAt;
    private System.Windows.Threading.DispatcherTimer? _timer;
    private int _lastDoneForEta;
    private int _lastTotalForEta;

    private void StartTimer()
    {
        _runStartedAt = DateTime.Now;
        _lastDoneForEta = 0; _lastTotalForEta = 0;
        TimerText = "⏱ 00:00";
        EtaText = "";
        if (_timer == null)
        {
            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (_, _) => TickTimer();
        }
        _timer.Start();
    }

    private void StopTimer() { _timer?.Stop(); _runStartedAt = null; EtaText = ""; }

    private void TickTimer()
    {
        if (_runStartedAt is not DateTime t) return;
        var elapsed = (int)(DateTime.Now - t).TotalSeconds;
        TimerText = "⏱ " + FormatHms(elapsed);

        if (_lastDoneForEta > 0 && _lastTotalForEta > 0 && _lastDoneForEta < _lastTotalForEta)
        {
            double avgPerOp = elapsed / (double)_lastDoneForEta;
            int remain = (int)(avgPerOp * (_lastTotalForEta - _lastDoneForEta));
            EtaText = "осталось ~" + FormatHms(remain);
        }
        else
        {
            EtaText = "";
        }
    }

    private static string FormatHms(int s) => s >= 3600
        ? $"{s / 3600:0}:{(s % 3600) / 60:00}:{s % 60:00}"
        : $"{s / 60:00}:{s % 60:00}";

    [RelayCommand]
    private async Task StartAsync()
    {
        if (App.Bot.IsRunning)
        {
            App.Log.Log("Бот уже запущен.", LogLevel.Warn);
            return;
        }

        // Готовим опции
        int.TryParse(Limit, out var limitInt);
        if (limitInt <= 0) limitInt = 10;

        var selectedDays = Days.Where(d => d.IsSelected).Select(d => d.Key).ToArray();
        if (selectedDays.Length == 0) selectedDays = Constants.AllDays;

        // Выбранные операторы; если сняты все — откат к дефолту (B1/B2/B3)
        var selectedOperators = Operators.Where(o => o.IsSelected)
            .Select(o => (o.Prefix, o.Code)).ToArray();
        if (selectedOperators.Length == 0) selectedOperators = Constants.Operators;

        var regionCodes = AllRegions
            ? Array.Empty<int>()
            : Regions.Where(r => r.IsSelected).Select(r => r.Code).ToArray();
        var regionNames = AllRegions
            ? Array.Empty<string>()
            : Regions.Where(r => r.IsSelected).Select(r => r.Name).ToArray();

        // URL сайта (для Selenium)
        var siteUrl = Constants.Sites.GetValueOrDefault(_cfg.Site, "https://crm.lead.store");

        var opts = new BotRunOptions
        {
            ApiToken       = ApiToken ?? "",
            Tag            = Tag ?? "",
            Site           = _cfg.Site,
            SourceType     = SelectedSource == "Сайты" ? "sites" : "calls",
            Limit          = limitInt,
            Days           = selectedDays,
            Operators      = selectedOperators,
            RegionCodes    = regionCodes,
            RegionNames    = regionNames,
            SkipDuplicates = SkipDuplicates,
            UseManualTable = UseManualTable,
            SheetUrl       = SheetUrl ?? "",
            SiteUrl        = siteUrl,
            UseQuickUrl    = UseQuickUrl,
            QuickUrl       = QuickUrl ?? "",
            Login          = Login ?? "",
            Password       = Password ?? "",
            Records        = Records.Where(r => r.HasData).ToList(),
        };

        // Сброс статистики
        StatCreated = 0; StatSkipped = 0; StatErrors = 0;
        ProgressValue = 0;
        IsRunning = true;
        IsPaused = false;
        StatusText = "Запуск...";
        StartTimer();

        // Сбрасываем подсветку прошлой сессии — иначе строки будут гореть зелёным
        // ещё до того как новый прогон что-то сделает. Сохраняем "no_site" — это
        // пометка из загрузки Sheets (нет источника), её не трогаем.
        foreach (var r in Records)
        {
            if (r.Status == "no_site") continue;
            r.Status = "";
            r.StatusMessage = "";
        }
        ActiveRecord = null;

        var dispatcher = Application.Current.Dispatcher;

        var callbacks = new BotRunCallbacks
        {
            Log = (msg, lvl) => App.Log.Log(msg, lvl),
            StatsChanged = (c, s, e) => dispatcher.BeginInvoke(() =>
            {
                StatCreated = c; StatSkipped = s; StatErrors = e;
            }),
            ProgressChanged = (done, total) => dispatcher.BeginInvoke(() =>
            {
                ProgressValue = total > 0 ? done * 100.0 / total : 0;
                _lastDoneForEta = done;
                _lastTotalForEta = total;
            }),
            // Бот загрузил данные из Google Sheets → пушим их в UI-таблицу.
            // Используем ТЕ ЖЕ экземпляры PhoneRecord что и сам бот будет дальше
            // обновлять через RowStatusChanged — иначе подсветка не работала.
            RowsLoaded = recs => dispatcher.BeginInvoke(() =>
            {
                foreach (var old in Records) old.PropertyChanged -= Record_PropertyChanged;
                Records.Clear();
                foreach (var r in recs)
                {
                    // подстраховка — на случай если файл/листы вернули старые статусы
                    if (r.Status != "no_site")
                    {
                        r.Status = "";
                        r.StatusMessage = "";
                    }
                    r.PropertyChanged += Record_PropertyChanged;
                    Records.Add(r);
                }
                EnsureEmptyRows();
            }),
            RowStatusChanged = (rec, status) => dispatcher.BeginInvoke(() =>
            {
                rec.Status = status;
                rec.StatusMessage = status switch
                {
                    "ok"     => "✓ создан",
                    "err"    => "✗ ошибка",
                    "active" => "→ активен",
                    "dup"    => "⏭ дубль",
                    "skip"   => "⏭ пропущен",
                    _ => "",
                };
                if (status == "active") ActiveRecord = rec;
            }),
            StatusTextChanged = txt => dispatcher.BeginInvoke(() => StatusText = txt),
        };

        try
        {
            if (UseApiMode)
                await Task.Run(() => App.Bot.RunApiAsync(opts, callbacks));
            else
                await Task.Run(() => App.Bot.RunSeleniumAsync(opts, callbacks));
        }
        finally
        {
            IsRunning = false;
            IsPaused = false;
            StopTimer();
        }
    }

    [RelayCommand]
    private void Pause()
    {
        if (!App.Bot.IsRunning) return;
        if (App.Bot.IsPaused)
        {
            App.Bot.Resume();
            IsPaused = false;
            StatusText = "▶ Продолжаю...";
            App.Log.Log("⏵ Продолжаем", LogLevel.Warn);
        }
        else
        {
            App.Bot.Pause();
            IsPaused = true;
            StatusText = "⏸ На паузе";
            App.Log.Log("⏸ Пауза", LogLevel.Warn);
        }
    }

    [RelayCommand]
    private void Stop()
    {
        if (!App.Bot.IsRunning) return;
        App.Bot.Stop();
        StatusText = "⏹ Остановка...";
        App.Log.Log("⏹ Стоп запрошен", LogLevel.Err);
    }

    // ── Лог в FlowDocument для RichTextBox ──────────────
    public FlowDocument LogDocument { get; } = new()
    {
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        PagePadding = new System.Windows.Thickness(8),
        LineHeight = 18,
    };

    private void OnLogEntry(LogEntry entry)
    {
        var app = Application.Current;
        if (app == null) return;
        app.Dispatcher.BeginInvoke(() =>
        {
            var res = app.Resources;
            var brush = entry.Level switch
            {
                LogLevel.Ok   => (Brush)res["AccentBrush"],
                LogLevel.Err  => (Brush)res["RedBrush"],
                LogLevel.Warn => (Brush)res["YellowBrush"],
                LogLevel.Dim  => (Brush)res["Text2Brush"],
                LogLevel.Bold => (Brush)res["Accent2Brush"],
                _             => (Brush)res["TextBrush"],
            };
            var p = new Paragraph { Margin = new Thickness(0) };
            p.Inlines.Add(new Run($"[{entry.Timestamp:HH:mm:ss}] ") { Foreground = (Brush)res["Text2Brush"] });
            p.Inlines.Add(new Run(entry.Message) { Foreground = brush, FontWeight = entry.Level == LogLevel.Bold ? FontWeights.SemiBold : FontWeights.Normal });
            LogDocument.Blocks.Add(p);
        });
    }

    /// <summary>Сохраняет выбор операторов в конфиг.</summary>
    private void SaveOperators()
    {
        _cfg.Operators = Operators.ToDictionary(o => o.Prefix, o => o.IsSelected);
        SaveConfig();
    }

    private void SaveConfig() => App.Config.Save(_cfg);
}
