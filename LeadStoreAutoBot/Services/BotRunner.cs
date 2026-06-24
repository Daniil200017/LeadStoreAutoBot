using System.Diagnostics;
using LeadStoreAutoBot.Models;
using LeadStoreAutoBot.Models.Api;
using LeadStoreAutoBot.Resources;

namespace LeadStoreAutoBot.Services;

/// <summary>Параметры запуска бота — то что выбрано в UI.</summary>
public class BotRunOptions
{
    public required string ApiToken    { get; init; }
    public required string Tag         { get; init; }
    public required string Site        { get; init; }
    public required string SourceType  { get; init; }     // "sites" | "calls"
    public required int    Limit       { get; init; }
    public required string[] Days      { get; init; }     // ["Пн","Вт",...]
    /// <summary>Выбранные операторы (Prefix, Code). Пустой = дефолт B1/B2/B3.</summary>
    public (string Prefix, string Code)[] Operators { get; init; } = Constants.Operators;
    public required int[]  RegionCodes { get; init; }     // пустой = все
    public required string[] RegionNames { get; init; }   // нужно для Selenium (он ищет по тексту)
    public required bool   SkipDuplicates { get; init; }

    // Источник данных
    public bool   UseManualTable { get; init; }
    public string SheetUrl       { get; init; } = "";

    // Selenium-специфичное
    public string SiteUrl    { get; init; } = "";
    public bool   UseQuickUrl { get; init; }
    public string QuickUrl   { get; init; } = "";
    public string Login      { get; init; } = "";
    public string Password   { get; init; } = "";

    public required IReadOnlyList<PhoneRecord> Records { get; init; }
}

/// <summary>Колбэки для отчётов о прогрессе в UI.</summary>
public class BotRunCallbacks
{
    public Action<string, LogLevel>? Log              { get; init; }
    public Action<int, int, int>?    StatsChanged     { get; init; } // created, skipped, errors
    public Action<int, int>?         ProgressChanged  { get; init; } // done, total
    public Action<PhoneRecord, string>? RowStatusChanged { get; init; } // row, status
    public Action<string>?           StatusTextChanged { get; init; }
    /// <summary>
    /// Вызывается после загрузки данных из Google Sheets — UI должен заменить свою таблицу
    /// этими записями (чтобы те же экземпляры PhoneRecord использовались и ботом и UI,
    /// и подсветка строк через RowStatusChanged реально что-то меняла на вкладке Таблица).
    /// Как в Python: self.records = ...; self.table_queue.put("refresh").
    /// </summary>
    public Action<IReadOnlyList<PhoneRecord>>? RowsLoaded { get; init; }
}

/// <summary>
/// Полная оркестрация API-режима — 4 фазы как в Python:
/// 1. Создание + skip_dup
/// 2. Retry упавших
/// 3. Final verify по полному списку
/// 4. Подсчёт статистики, окраска строк
/// </summary>
public class BotRunner
{
    private readonly ProstatsApi _api;
    private CancellationTokenSource? _cts;
    private readonly ManualResetEventSlim _pauseGate = new(true);

    public bool IsRunning { get; private set; }
    public bool IsPaused  { get; private set; }

    public BotRunner(ProstatsApi api) { _api = api; }

    /// <summary>Строит подробный текстовый отчёт для истории.</summary>
    private static string BuildDetailsReport(BotRunOptions opts,
        int created, int skipped, int errors,
        int phonesOk, int phonesDup,
        List<string> detailsCreated, List<string> detailsSkipped, List<string> detailsFailed,
        int elapsedSec)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("══════════════════════════════════════════════");
        sb.AppendLine($" Дата:     {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine($" Сайт:     {opts.Site}");
        sb.AppendLine($" Тег:      {opts.Tag}");
        sb.AppendLine($" Источник: {(opts.SourceType == "sites" ? "Сайты" : "Звонки")}");
        sb.AppendLine($" Лимит:    {opts.Limit}");
        sb.AppendLine($" Время:    {elapsedSec / 60:00}:{elapsedSec % 60:00}");
        sb.AppendLine("══════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("📊 ИТОГИ");
        sb.AppendLine($"  ✅ Создано проектов:     {created}");
        sb.AppendLine($"  📞 Полностью телефонов:  {phonesOk}");
        sb.AppendLine($"  ⏭  Пропущено (дубли):    {skipped} проектов · {phonesDup} тел.");
        sb.AppendLine($"  ❌ Ошибок:               {errors}");
        sb.AppendLine();

        if (detailsCreated.Count > 0)
        {
            sb.AppendLine($"✅ СОЗДАНО ({detailsCreated.Count}):");
            foreach (var n in detailsCreated) sb.AppendLine($"  • {n}");
            sb.AppendLine();
        }
        if (detailsSkipped.Count > 0)
        {
            sb.AppendLine($"⏭ ПРОПУЩЕНО ({detailsSkipped.Count}):");
            foreach (var n in detailsSkipped) sb.AppendLine($"  • {n}");
            sb.AppendLine();
        }
        if (detailsFailed.Count > 0)
        {
            sb.AppendLine($"❌ НЕ СОЗДАНЫ ({detailsFailed.Count}):");
            foreach (var n in detailsFailed) sb.AppendLine($"  • {n}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// True если исключение от Selenium означает что браузер мёртв (юзер закрыл / chrome упал).
    /// Используется в Selenium-режиме чтобы не падать всей программой и корректно закончить с покраской.
    /// </summary>
    private static bool IsBrowserDead(Exception ex)
    {
        if (ex is OpenQA.Selenium.NoSuchWindowException) return true;
        if (ex is OpenQA.Selenium.WebDriverException wde)
        {
            var m = (wde.Message ?? "").ToLowerInvariant();
            if (m.Contains("no such window") || m.Contains("target window already closed")
                || m.Contains("session deleted") || m.Contains("not connected to devtools")
                || m.Contains("chrome not reachable") || m.Contains("disconnected")
                || m.Contains("invalid session id") || m.Contains("session not created"))
                return true;
        }
        return false;
    }

    /// <summary>Selenium-режим — открывает Chrome, логинится, заполняет форму создания проекта.</summary>
    public async Task RunSeleniumAsync(BotRunOptions opts, BotRunCallbacks cb)
    {
        if (IsRunning) throw new InvalidOperationException("Бот уже запущен.");

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsRunning = true;
        IsPaused = false;
        _pauseGate.Set();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        const string SEP = "──────────────────────────────────────────────────";
        var log = cb.Log ?? ((_, _) => { });
        var selenium = new SeleniumService();

        // Объявлены на уровне метода (не внутри try) — чтобы catch (Stop / закрытие браузера)
        // могли вызвать ColorSheetAndSaveHistoryAsync с актуальными статами.
        bool browserClosed = false;
        bool finalizeRan = false;
        var cellStatus = new Dictionary<(int Row, string Ptype), string>();   // "ok" | "err" | "dup"
        List<PhoneRecord> workRecs = new();
        string siteUrlForFinalize = opts.SiteUrl;
        int statsCreated = 0, statsSkipped = 0;
        var failedList = new List<(string Phone, string Type, PhoneRecord Rec)>();
        var detailsCreated = new List<string>();
        var detailsSkipped = new List<string>();
        var detailsFailed  = new List<string>();

        try
        {
            // ── Загрузка таблицы ──────────────────────────────────────────
            if (opts.UseManualTable)
            {
                workRecs = opts.Records.Where(r => r.HasData).ToList();
                if (workRecs.Count == 0)
                {
                    log("❌ Таблица пуста. В ручном режиме нужно заполнить данные на вкладке ТАБЛИЦА.", LogLevel.Err);
                    return;
                }
                log($"📋 Ручной режим: {workRecs.Count} строк", LogLevel.Info);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(opts.SheetUrl))
                {
                    log("❌ Не указана ссылка на Google Таблицу.", LogLevel.Err);
                    return;
                }
                log("📊 Загружаю данные из Google Sheets...", LogLevel.Info);
                cb.StatusTextChanged?.Invoke("📊 Загрузка таблицы...");
                try
                {
                    // Таймаут 60 сек на загрузку таблицы — чтобы не висеть бесконечно
                    using var sheetsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    sheetsCts.CancelAfter(TimeSpan.FromSeconds(60));
                    var loaded = await App.Sheets.LoadPhonesAsync(opts.SheetUrl,
                        s => log("  → " + s, LogLevel.Dim), sheetsCts.Token);
                    workRecs = loaded.Where(r => r.HasData).ToList();
                    log($"✅ Загружено: {workRecs.Count} строк", LogLevel.Ok);
                }
                catch (Exception ex)
                {
                    log($"❌ Ошибка загрузки Google Sheets: {ex.Message}", LogLevel.Err);
                    return;
                }
                if (workRecs.Count == 0)
                {
                    log("❌ В таблице нет данных для работы.", LogLevel.Err);
                    return;
                }
                // Показываем загруженные данные на вкладке "Таблица" — чтобы пользователь
                // видел что бот сейчас обрабатывает (в Python было self.records = ...).
                cb.RowsLoaded?.Invoke(workRecs);
            }

            string siteUrl = opts.SiteUrl;
            log($"🌐 Открываю Chrome...", LogLevel.Info);
            cb.StatusTextChanged?.Invoke("🌐 Запуск браузера...");
            await Task.Run(() => selenium.Start(), ct);

            log("🔐 Авторизация...", LogLevel.Info);
            cb.StatusTextChanged?.Invoke("🔐 Авторизация...");
            try
            {
                if (opts.UseQuickUrl)
                {
                    if (string.IsNullOrWhiteSpace(opts.QuickUrl))
                        throw new InvalidOperationException("Не указана ссылка для быстрого входа.");
                    log("⚡ Быстрый вход по ссылке...", LogLevel.Info);
                    await Task.Run(() => selenium.QuickLogin(opts.QuickUrl), ct);
                    var cur = new Uri(selenium.Driver.Url);
                    siteUrl = $"{cur.Scheme}://{cur.Host}";
                    log($"✅ Вошёл! Сайт: {siteUrl}", LogLevel.Ok);
                }
                else
                {
                    if (string.IsNullOrEmpty(opts.Login)) throw new InvalidOperationException("Не указан Email.");
                    if (string.IsNullOrEmpty(opts.Password)) throw new InvalidOperationException("Не указан пароль.");
                    await Task.Run(() => selenium.Login(opts.Login, opts.Password, siteUrl), ct);
                    log("✅ Успешно вошёл!", LogLevel.Ok);
                }
            }
            catch (Exception ex)
            {
                log($"❌ Ошибка входа: {ex.Message}", LogLevel.Err);
                return;
            }

            // Список существующих проектов (для skip_dup)
            var existing = new HashSet<string>();
            if (opts.SkipDuplicates)
            {
                log("🔍 Загружаю список существующих проектов...", LogLevel.Info);
                try
                {
                    existing = await Task.Run(() => selenium.GetExistingProjectNames(siteUrl), ct);
                    log($"  Найдено существующих: {existing.Count}", LogLevel.Dim);
                }
                catch (Exception ex)
                {
                    log($"⚠ Не удалось загрузить список: {ex.Message}", LogLevel.Warn);
                }
            }

            int totalProjects = workRecs.Sum(r =>
                (string.IsNullOrEmpty(r.Op) ? 0 : 1) + (string.IsNullOrEmpty(r.Mg) ? 0 : 1));
            log(SEP, LogLevel.Dim);
            log($"🚀 СТАРТ · Selenium · {opts.Site}", LogLevel.Bold);
            log($"   {workRecs.Count} строк · ~{totalProjects} проектов · Тег: {opts.Tag}", LogLevel.Info);
            log(SEP, LogLevel.Dim);
            cb.StatusTextChanged?.Invoke($"🚀 Работаю... 0/{totalProjects}");

            int doneCount = 0;
            int statsErrors = 0;

            void UpdateProgress()
            {
                cb.StatsChanged?.Invoke(statsCreated, statsSkipped, statsErrors);
                cb.ProgressChanged?.Invoke(doneCount, totalProjects);
                int es = (int)sw.Elapsed.TotalSeconds;
                cb.StatusTextChanged?.Invoke(
                    $"⏳ Создаю... {doneCount}/{totalProjects}  ⏱ {es / 60:00}:{es % 60:00}");
            }

            // ── Фаза 1 ───────────────────────────────────────────────────
            for (int i = 0; i < workRecs.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                if (browserClosed) break;
                _pauseGate.Wait(ct);
                if (ct.IsCancellationRequested) break;

                var rec = workRecs[i];
                cb.RowStatusChanged?.Invoke(rec, "active");
                log($"━━ [{i + 1}/{workRecs.Count}] {rec.Site}", LogLevel.Info);

                var phones = new List<(string Phone, string Type)>();
                if (!string.IsNullOrEmpty(rec.Op)) phones.Add((rec.Op, "O"));
                if (!string.IsNullOrEmpty(rec.Mg)) phones.Add((rec.Mg, "M"));
                if (phones.Count == 0)
                {
                    log("  ⏭ Нет телефонов, пропускаю", LogLevel.Dim);
                    statsSkipped++;
                    cb.StatsChanged?.Invoke(statsCreated, statsSkipped, statsErrors);
                    cb.RowStatusChanged?.Invoke(rec, "skip");
                    continue;
                }

                bool anyOk = false, anyErr = false;
                foreach (var (phone, ptype) in phones)
                {
                    if (ct.IsCancellationRequested) break;
                    if (browserClosed) break;
                    _pauseGate.Wait(ct);
                    if (ct.IsCancellationRequested) break;

                    var projName = $"{phone}_{rec.Site}_{ptype}";
                    if (opts.SkipDuplicates && existing.Contains(projName))
                    {
                        log($"  ⏭ Дубль: {phone} | {rec.Site}", LogLevel.Dim);
                        statsSkipped++;
                        detailsSkipped.Add($"{projName} (дубль)");
                        cellStatus[(rec.RowNum, ptype)] = "dup";
                        UpdateProgress();
                        continue;
                    }

                    try
                    {
                        await Task.Run(() => selenium.CreateProject(
                            phone, ptype, opts.SourceType, opts.Tag, opts.RegionNames,
                            opts.Limit, opts.Days, rec.Site, siteUrl,
                            System.Array.ConvertAll(opts.Operators, o => o.Prefix)), ct);
                        log($"  ✅ {projName}", LogLevel.Ok);
                        statsCreated++; doneCount++; anyOk = true;
                        detailsCreated.Add(projName);
                        cellStatus[(rec.RowNum, ptype)] = "ok";
                    }
                    catch (Exception ex) when (IsBrowserDead(ex))
                    {
                        log($"  🛑 {projName}: браузер закрыт пользователем — прерываю", LogLevel.Err);
                        statsErrors++; anyErr = true;
                        cellStatus[(rec.RowNum, ptype)] = "err";
                        detailsFailed.Add($"{projName} (браузер закрыт)");
                        browserClosed = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message.Length > 80 ? ex.Message[..80] : ex.Message;
                        log($"  ⚠ {projName}: {msg} → в повтор", LogLevel.Err);
                        statsErrors++; anyErr = true;
                        failedList.Add((phone, ptype, rec));
                        cellStatus[(rec.RowNum, ptype)] = "err";
                        try
                        {
                            var cancel = selenium.Driver.FindElement(OpenQA.Selenium.By.XPath(
                                "//button[contains(@class,'el-button') and contains(.,'Отмена')]"));
                            ((OpenQA.Selenium.IJavaScriptExecutor)selenium.Driver)
                                .ExecuteScript("arguments[0].click();", cancel);
                            await Task.Delay(1000, ct);
                        }
                        catch { }
                    }

                    UpdateProgress();
                    try { await Task.Delay(1000, ct); } catch (TaskCanceledException) { }
                }

                cb.RowStatusChanged?.Invoke(rec, anyErr ? "err" : anyOk ? "ok" : "skip");
            }

            if (browserClosed)
            {
                log("🛑 Дальнейшее создание прервано — браузер закрыт.", LogLevel.Warn);
            }

            // ── Фаза 2: Retry упавших ────────────────────────────────────
            if (failedList.Count > 0 && !ct.IsCancellationRequested && !browserClosed)
            {
                log(SEP, LogLevel.Dim);
                log($"🔄 Повтор {failedList.Count} ошибочных...", LogLevel.Bold);
                cb.StatusTextChanged?.Invoke($"🔄 Повтор {failedList.Count}...");
                await Task.Delay(3000, ct);

                var stillFailed = new List<(string Phone, string Type, PhoneRecord Rec)>();
                for (int idx = 0; idx < failedList.Count; idx++)
                {
                    if (ct.IsCancellationRequested) break;
                    if (browserClosed) break;
                    _pauseGate.Wait(ct);
                    var (phone, ptype, rec) = failedList[idx];
                    log($"  🔄 [{idx + 1}/{failedList.Count}] Повтор: {phone}_{rec.Site}_{ptype}", LogLevel.Info);
                    try
                    {
                        await Task.Run(() => selenium.CreateProject(
                            phone, ptype, opts.SourceType, opts.Tag, opts.RegionNames,
                            opts.Limit, opts.Days, rec.Site, siteUrl,
                            System.Array.ConvertAll(opts.Operators, o => o.Prefix)), ct);
                        log($"  ✅ Повтор успешен", LogLevel.Ok);
                        statsCreated++;
                        statsErrors = Math.Max(0, statsErrors - 1);
                        detailsCreated.Add($"{phone}_{rec.Site}_{ptype} (повтор)");
                        cellStatus[(rec.RowNum, ptype)] = "ok";
                    }
                    catch (Exception ex2) when (IsBrowserDead(ex2))
                    {
                        log($"  🛑 Повтор: браузер закрыт — прерываю", LogLevel.Err);
                        stillFailed.Add((phone, ptype, rec));
                        detailsFailed.Add($"{phone}_{rec.Site}_{ptype} (браузер закрыт)");
                        browserClosed = true;
                        break;
                    }
                    catch (Exception ex2)
                    {
                        var msg = ex2.Message.Length > 60 ? ex2.Message[..60] : ex2.Message;
                        log($"  ❌ Повтор провален: {msg}", LogLevel.Err);
                        stillFailed.Add((phone, ptype, rec));
                        detailsFailed.Add($"{phone}_{rec.Site}_{ptype}");
                        try
                        {
                            var cancel = selenium.Driver.FindElement(OpenQA.Selenium.By.XPath(
                                "//button[contains(@class,'el-button') and contains(.,'Отмена')]"));
                            ((OpenQA.Selenium.IJavaScriptExecutor)selenium.Driver)
                                .ExecuteScript("arguments[0].click();", cancel);
                            await Task.Delay(1000, ct);
                        }
                        catch { }
                    }
                    cb.StatsChanged?.Invoke(statsCreated, statsSkipped, statsErrors);
                    try { await Task.Delay(1000, ct); } catch (TaskCanceledException) { }
                }
                failedList = stillFailed;
            }

            siteUrlForFinalize = siteUrl;

            // ── Итог + раскраска Google Таблицы ─────────────────────────
            sw.Stop();
            var elapsed = (int)sw.Elapsed.TotalSeconds;
            log(SEP, LogLevel.Dim);
            log($"🏁 ГОТОВО!  ✅ {statsCreated}   ⏭ {statsSkipped}   ❌ {failedList.Count}   ⏱ {elapsed / 60:00}:{elapsed % 60:00}",
                LogLevel.Bold);
            if (failedList.Count > 0)
            {
                log($"❌ Не удалось создать {failedList.Count}:", LogLevel.Err);
                foreach (var (p, t, r) in failedList.Take(20))
                    log($"  ❌ {p} | {r.Site} | тип:{t}", LogLevel.Err);
                if (failedList.Count > 20)
                    log($"  ... и ещё {failedList.Count - 20}", LogLevel.Err);
            }
            log(SEP, LogLevel.Dim);

            cb.StatusTextChanged?.Invoke($"✅ Готово! Создано {statsCreated}  ⏱ {elapsed / 60:00}:{elapsed % 60:00}");
            cb.ProgressChanged?.Invoke(totalProjects, totalProjects);

            await ColorSheetAndSaveHistoryAsync(elapsed, false);

            if (failedList.Count > 0) App.Sound.PlayError();
            else App.Sound.PlayDone();
        }
        catch (OperationCanceledException)
        {
            log("⏹ Остановлено пользователем", LogLevel.Warn);
            cb.StatusTextChanged?.Invoke("⏹ Остановлено");
            sw.Stop();
            try { await ColorSheetAndSaveHistoryAsync((int)sw.Elapsed.TotalSeconds, true); } catch { }
        }
        catch (Exception ex)
        {
            log($"💥 Критическая ошибка: {ex.GetType().Name}: {ex.Message}", LogLevel.Err);
            sw.Stop();
            try { await ColorSheetAndSaveHistoryAsync((int)sw.Elapsed.TotalSeconds, true); } catch { }
        }
        finally
        {
            try { selenium.Quit(); } catch { }
            IsRunning = false;
            IsPaused = false;
            _cts?.Dispose();
            _cts = null;
        }

        // ── Локальный финализатор: верифицирует с сервером (если можем),
        //    раскрашивает ячейки и сохраняет историю. Запускается ровно 1 раз. ──
        async Task ColorSheetAndSaveHistoryAsync(int elapsedSec, bool wasInterrupted)
        {
            if (finalizeRan) return;
            finalizeRan = true;

            // Раскраска нужна только если мы загружали из Google Таблицы
            // и было хоть что-то обработано
            if (App.Sheets.CurrentSpreadsheetId != null && cellStatus.Count > 0 && workRecs.Count > 0)
            {
                bool verified = false;
                if (!browserClosed)
                {
                    try
                    {
                        log("🔍 Сверяю результат с сервером перед раскраской...", LogLevel.Info);
                        var serverNames = await Task.Run(() => selenium.GetExistingProjectNames(siteUrlForFinalize));
                        log($"   На сервере: {serverNames.Count} проектов", LogLevel.Dim);
                        // Согласовываем cellStatus с тем что реально есть на сервере
                        foreach (var rec in workRecs)
                        {
                            foreach (var ptype in new[] { "O", "M" })
                            {
                                var phone = ptype == "O" ? rec.Op : rec.Mg;
                                if (string.IsNullOrEmpty(phone)) continue;
                                var name = $"{phone}_{rec.Site}_{ptype}";
                                bool onServer = serverNames.Contains(name);
                                cellStatus.TryGetValue((rec.RowNum, ptype), out var cur);
                                if (onServer && cur != "ok") cellStatus[(rec.RowNum, ptype)] = "ok";
                                else if (!onServer && cur == "ok") cellStatus[(rec.RowNum, ptype)] = "err";
                            }
                        }
                        verified = true;
                    }
                    catch (Exception ex)
                    {
                        log($"⚠ Сверка не удалась: {ex.Message}", LogLevel.Warn);
                    }
                }

                if (!verified)
                    log("⚠ Раскрашиваю БЕЗ сверки с сервером — проверьте Google Таблицу вручную!", LogLevel.Warn);
                else
                    log("🎨 Раскрашиваю Google Таблицу...", LogLevel.Info);

                int colored = 0;
                foreach (var rec in workRecs)
                {
                    cellStatus.TryGetValue((rec.RowNum, "O"), out var opSt);
                    cellStatus.TryGetValue((rec.RowNum, "M"), out var mgSt);

                    string OpColor(string? s) => s switch
                    {
                        "ok"  => "yellow",
                        "err" => "red",
                        "dup" => "red",
                        _     => "",
                    };

                    if (!string.IsNullOrEmpty(rec.Op) && !string.IsNullOrEmpty(opSt))
                    {
                        var c = OpColor(opSt);
                        if (c != "") { _ = App.Sheets.ColorCellAsync(rec.RowNum, 2, c); colored++; }
                    }

                    if (!string.IsNullOrEmpty(rec.Mg) && !string.IsNullOrEmpty(mgSt))
                    {
                        var c = OpColor(mgSt);
                        if (rec.Mg == rec.Op && !string.IsNullOrEmpty(rec.Op) && c != "") c = "pink";
                        if (c != "") { _ = App.Sheets.ColorCellAsync(rec.RowNum, 3, c); colored++; }
                    }
                }
                log($"   Закрашено {colored} ячеек", LogLevel.Dim);
            }

            // Сохраняем историю один раз
            int errorsCount = failedList.Count;
            App.History.Add(new SessionHistory
            {
                Date           = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
                Site           = opts.Site,
                Mode           = wasInterrupted ? "🌐 Selenium (прервано)" : "🌐 Selenium",
                Created        = statsCreated,
                Skipped        = statsSkipped,
                Errors         = errorsCount,
                ElapsedSeconds = elapsedSec,
                Details        = BuildDetailsReport(opts, statsCreated, statsSkipped, errorsCount,
                                                    statsCreated, statsSkipped,
                                                    detailsCreated, detailsSkipped, detailsFailed,
                                                    elapsedSec),
            });
        }
    }

    public void Pause()
    {
        if (!IsRunning) return;
        IsPaused = true;
        _pauseGate.Reset();
    }

    public void Resume()
    {
        if (!IsRunning) return;
        IsPaused = false;
        _pauseGate.Set();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _pauseGate.Set(); // разбудить если на паузе
    }

    /// <summary>Главная точка входа — API-режим.</summary>
    public async Task RunApiAsync(BotRunOptions opts, BotRunCallbacks cb)
    {
        if (IsRunning) throw new InvalidOperationException("Бот уже запущен.");

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsRunning = true;
        IsPaused = false;
        _pauseGate.Set();

        var sw = Stopwatch.StartNew();
        const string SEP = "──────────────────────────────────────────────────";
        var log = cb.Log ?? ((_, _) => { });

        try
        {
            // ── Базовая валидация ────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(opts.ApiToken))
            {
                log("❌ Не указан API токен.", LogLevel.Err);
                return;
            }

            // ── Загрузка таблицы (если не ручной режим) ──────────────────
            List<PhoneRecord> workRecs;
            if (opts.UseManualTable)
            {
                workRecs = opts.Records.Where(r => r.HasData).ToList();
                if (workRecs.Count == 0)
                {
                    log("❌ Таблица пуста. В ручном режиме нужно заполнить данные на вкладке ТАБЛИЦА.", LogLevel.Err);
                    return;
                }
                log($"📋 Ручной режим: {workRecs.Count} строк", LogLevel.Info);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(opts.SheetUrl))
                {
                    log("❌ Не указана ссылка на Google Таблицу. Вставь её на левой панели → Источник данных.", LogLevel.Err);
                    return;
                }
                log("📊 Загружаю данные из Google Sheets...", LogLevel.Info);
                cb.StatusTextChanged?.Invoke("📊 Загрузка таблицы...");
                try
                {
                    // Таймаут 60 сек на загрузку таблицы — чтобы не висеть бесконечно
                    using var sheetsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    sheetsCts.CancelAfter(TimeSpan.FromSeconds(60));
                    var loaded = await App.Sheets.LoadPhonesAsync(opts.SheetUrl,
                        s => log("  → " + s, LogLevel.Dim), sheetsCts.Token);
                    workRecs = loaded.Where(r => r.HasData).ToList();
                    log($"✅ Загружено: {workRecs.Count} строк с данными", LogLevel.Ok);
                }
                catch (Exception ex)
                {
                    log($"❌ Ошибка загрузки Google Sheets: {ex.Message}", LogLevel.Err);
                    return;
                }
                if (workRecs.Count == 0)
                {
                    log("❌ В таблице нет данных для работы.", LogLevel.Err);
                    return;
                }
                // Показываем загруженные данные на вкладке "Таблица" — чтобы пользователь
                // видел что бот сейчас обрабатывает (в Python было self.records = ...).
                cb.RowsLoaded?.Invoke(workRecs);
            }

            // ── Проверка токена ──────────────────────────────────────────
            log("🔑 Проверяю API токен...", LogLevel.Info);
            cb.StatusTextChanged?.Invoke("🔑 Проверка токена...");
            ExistingProjectsSnapshot snapshot;
            try
            {
                snapshot = await _api.GetExistingProjectsAsync(opts.ApiToken, ct);
                log($"✅ Токен принят! На аккаунте {snapshot.FullNames.Count} существующих проектов", LogLevel.Ok);
            }
            catch (Exception ex)
            {
                log($"❌ Ошибка токена: {ex.Message}", LogLevel.Err);
                return;
            }

            // Если skip_dup выключен — снэпшот не используем для проверки
            if (!opts.SkipDuplicates)
                snapshot = new ExistingProjectsSnapshot();

            // ── Подсчёт планируемых проектов ─────────────────────────────
            int totalPhones = workRecs.Sum(r =>
                (string.IsNullOrEmpty(r.Op) ? 0 : 1) + (string.IsNullOrEmpty(r.Mg) ? 0 : 1));
            int totalProjects = totalPhones * opts.Operators.Length;
            int perOpLimit = Math.Max(1, opts.Limit / opts.Operators.Length);
            var opsSummary = string.Join(" ", opts.Operators.Select(o => o.Prefix));

            log(SEP, LogLevel.Dim);
            log($"🚀 СТАРТ · API режим · операторы: {opsSummary}", LogLevel.Bold);
            log($"   {workRecs.Count} строк · {totalPhones} телефонов · ~{totalProjects} проектов", LogLevel.Info);
            log($"   Тег: {opts.Tag} · Лимит: {opts.Limit} ({perOpLimit}/оп.) · {opts.SourceType}", LogLevel.Dim);
            log(SEP, LogLevel.Dim);
            log("⏳ Создаю проекты...", LogLevel.Info);
            cb.StatusTextChanged?.Invoke($"⏳ Создаю... 0/{totalProjects}");

            // ── Трекинг ─────────────────────────────────────────────────
            var attemptedSet = new HashSet<(string OpPrefix, string BaseName, int RowNum)>();
            var successSet   = new HashSet<(string OpPrefix, string BaseName, int RowNum)>();
            var dupSet       = new HashSet<(string Phone, string Type, int RowNum)>();
            var failedList   = new List<(string Phone, string Type, string OpPrefix, string OpSrc, PhoneRecord Rec)>();
            // Детализация для истории: список всех операторов и что с ними стало
            var detailsCreated = new List<string>();   // успешно
            var detailsSkipped = new List<string>();   // пропущены как дубли
            var detailsFailed  = new List<string>();   // упали
            int doneCount   = 0;
            int statsCreated = 0, statsSkipped = 0, statsErrors = 0;

            void UpdateProgress()
            {
                cb.StatsChanged?.Invoke(statsCreated, statsSkipped, statsErrors);
                cb.ProgressChanged?.Invoke(doneCount, totalProjects);
                int es = (int)sw.Elapsed.TotalSeconds;
                cb.StatusTextChanged?.Invoke(
                    $"⏳ Создаю... {doneCount}/{totalProjects}  ⏱ {es / 60:00}:{es % 60:00}");
            }

            var workdays = string.Concat(Constants.AllDays
                .Select((d, i) => opts.Days.Contains(d) ? (i + 1).ToString() : ""));
            if (workdays.Length == 0) workdays = "1234567";

            // ── Фаза 1: Создание ────────────────────────────────────────
            foreach (var rec in workRecs)
            {
                if (ct.IsCancellationRequested) break;

                int rowNum = rec.RowNum;
                string site = rec.Site;

                cb.RowStatusChanged?.Invoke(rec, "active");

                var phones = new List<(string Phone, string Type)>();
                if (!string.IsNullOrEmpty(rec.Op)) phones.Add((rec.Op, "O"));
                if (!string.IsNullOrEmpty(rec.Mg))
                {
                    if (rec.Mg == rec.Op && !string.IsNullOrEmpty(rec.Op))
                    {
                        // mg == op → пропускаем mg, как в Python
                    }
                    else
                    {
                        phones.Add((rec.Mg, "M"));
                    }
                }
                if (phones.Count == 0)
                {
                    cb.RowStatusChanged?.Invoke(rec, "");
                    continue;
                }

                foreach (var (phone, ptype) in phones)
                {
                    if (ct.IsCancellationRequested) break;

                    var baseName = $"{phone}_{site}_{ptype}";

                    // Проверка дубля по базовому имени → весь телефон пропускаем
                    if (opts.SkipDuplicates && snapshot.BaseNames.Contains(baseName))
                    {
                        dupSet.Add((phone, ptype, rowNum));
                        // считаем как пропущенные все 3 оператора + добавляем в detailsSkipped
                        statsSkipped += opts.Operators.Length;
                        foreach (var op in opts.Operators)
                            detailsSkipped.Add($"{op.Prefix}_{baseName} (дубль)");
                        UpdateProgress();
                        continue;
                    }

                    foreach (var (opPrefix, opSrc) in opts.Operators)
                    {
                        if (ct.IsCancellationRequested) break;
                        _pauseGate.Wait(ct);
                        if (ct.IsCancellationRequested) break;

                        var fullNameChk = $"{opPrefix}_{baseName}";
                        if (opts.SkipDuplicates && (
                            snapshot.FullNames.Contains(fullNameChk)
                            || snapshot.NameSrc.Contains((baseName, opSrc))))
                        {
                            statsSkipped++;
                            detailsSkipped.Add($"{fullNameChk} (уже существует)");
                            UpdateProgress();
                            continue;
                        }

                        attemptedSet.Add((opPrefix, baseName, rowNum));

                        var req = new ProjectCreateRequest
                        {
                            Type     = opts.SourceType == "sites" ? "hosts" : "calls",
                            Src      = opSrc,
                            Name     = baseName,
                            Limit    = perOpLimit,
                            Content  = phone,
                            Status   = 1,
                            Tag      = opts.Tag,
                            Workdays = workdays,
                            Regions  = opts.RegionCodes.Length > 0 ? opts.RegionCodes : null,
                        };

                        var (ok, _, err) = await _api.CreateProjectAsync(opts.ApiToken, req, ct);
                        if (ok)
                        {
                            successSet.Add((opPrefix, baseName, rowNum));
                            doneCount++;
                            statsCreated++;
                            detailsCreated.Add(fullNameChk);
                            snapshot.FullNames.Add(fullNameChk);
                            snapshot.BaseNames.Add(baseName);
                            snapshot.NameSrc.Add((baseName, opSrc));
                            snapshot.ContentSrc.Add((phone, opSrc));
                        }
                        else
                        {
                            failedList.Add((phone, ptype, opPrefix, opSrc, rec));
                            statsErrors++;
                            // detailsFailed обновится после Phase 2 (могут восстановиться)
                        }

                        UpdateProgress();
                        try { await Task.Delay(500, ct); } catch (TaskCanceledException) { }
                    }
                }
            }

            // ── Фаза 2: Retry упавших ────────────────────────────────────
            if (failedList.Count > 0 && !ct.IsCancellationRequested)
            {
                log($"🔄 Повтор {failedList.Count} запросов...", LogLevel.Dim);
                cb.StatusTextChanged?.Invoke($"🔄 Повтор {failedList.Count}...");
                try { await Task.Delay(1000, ct); } catch (TaskCanceledException) { }

                var stillFailed = new List<(string Phone, string Type, string OpPrefix, string OpSrc, PhoneRecord Rec)>();
                foreach (var (phone, ptype, opPrefix, opSrc, rec) in failedList)
                {
                    if (ct.IsCancellationRequested) break;
                    _pauseGate.Wait(ct);

                    var baseName = $"{phone}_{rec.Site}_{ptype}";
                    var req = new ProjectCreateRequest
                    {
                        Type     = opts.SourceType == "sites" ? "hosts" : "calls",
                        Src      = opSrc,
                        Name     = baseName,
                        Limit    = perOpLimit,
                        Content  = phone,
                        Status   = 1,
                        Tag      = opts.Tag,
                        Workdays = workdays,
                        Regions  = opts.RegionCodes.Length > 0 ? opts.RegionCodes : null,
                    };

                    var (ok, _, _) = await _api.CreateProjectAsync(opts.ApiToken, req, ct);
                    if (ok)
                    {
                        successSet.Add((opPrefix, baseName, rec.RowNum));
                        doneCount++;
                        statsCreated++;
                        statsErrors = Math.Max(0, statsErrors - 1);
                        detailsCreated.Add($"{opPrefix}_{baseName} (повтор)");
                        snapshot.ContentSrc.Add((phone, opSrc));
                    }
                    else
                    {
                        stillFailed.Add((phone, ptype, opPrefix, opSrc, rec));
                    }
                    UpdateProgress();
                    try { await Task.Delay(500, ct); } catch (TaskCanceledException) { }
                }
                failedList = stillFailed;
            }

            // ── Фаза 3: Финальная проверка по полному списку ─────────────
            log(SEP, LogLevel.Dim);
            log("🔍 Финальная проверка — запрашиваю полный список проектов...", LogLevel.Info);
            cb.StatusTextChanged?.Invoke("🔍 Проверка результатов...");
            try { await Task.Delay(1000, ct); } catch (TaskCanceledException) { }

            ExistingProjectsSnapshot verify;
            try
            {
                verify = await _api.GetExistingProjectsAsync(opts.ApiToken, ct);
                log($"   Сервер вернул {verify.FullNames.Count} проектов", LogLevel.Dim);
            }
            catch (Exception ex)
            {
                log($"⚠️  Не удалось получить список проектов: {ex.Message}", LogLevel.Warn);
                verify = snapshot;
            }

            // ── Фаза 4: Подсветка строк (статистика уже посчитана в Phase 1+2) ──
            int cntPhonesOk = 0, cntPhonesDup = 0;
            var reallyFailed = new List<string>();

            foreach (var rec in workRecs)
            {
                int rowNum = rec.RowNum;
                string site = rec.Site;

                var phones = new List<(string Phone, string Type)>();
                if (!string.IsNullOrEmpty(rec.Op)) phones.Add((rec.Op, "O"));
                if (!string.IsNullOrEmpty(rec.Mg)) phones.Add((rec.Mg, "M"));

                bool anyDup = false, anyFail = false, anyOk = false;

                foreach (var (phone, ptype) in phones)
                {
                    var baseName = $"{phone}_{site}_{ptype}";
                    var key = (phone, ptype, rowNum);

                    if (dupSet.Contains(key)) { cntPhonesDup++; anyDup = true; continue; }

                    var opsAttempted = opts.Operators
                        .Where(o => attemptedSet.Contains((o.Prefix, baseName, rowNum)))
                        .ToList();
                    if (opsAttempted.Count == 0) continue;

                    int okHere = 0, failHere = 0;
                    foreach (var (opPrefix, opSrc) in opsAttempted)
                    {
                        bool found =
                            successSet.Contains((opPrefix, baseName, rowNum))
                            || verify.FullNames.Contains($"{opPrefix}_{baseName}")
                            || verify.NameSrc.Contains((baseName, opSrc))
                            || verify.ContentSrc.Contains((phone, opSrc));
                        if (found) okHere++;
                        else { failHere++; reallyFailed.Add($"{opPrefix}_{baseName}"); }
                    }

                    if (okHere > 0) cntPhonesOk++;
                    anyOk   |= okHere > 0;
                    anyFail |= failHere > 0;
                }

                // Подсветка строки в UI
                string status = anyFail ? "err" : anyDup && !anyOk ? "dup" : anyOk ? "ok" : "";
                cb.RowStatusChanged?.Invoke(rec, status);

                // Раскраска в Google Sheets
                if (App.Sheets.CurrentSpreadsheetId != null)
                {
                    string opColor = anyFail ? "red" : anyDup && !anyOk ? "red" : anyOk ? "yellow" : "";
                    if (!string.IsNullOrEmpty(opColor) && !string.IsNullOrEmpty(rec.Op))
                        _ = App.Sheets.ColorCellAsync(rec.RowNum, 2, opColor);
                    if (!string.IsNullOrEmpty(opColor) && !string.IsNullOrEmpty(rec.Mg))
                    {
                        var mgColor = (rec.Mg == rec.Op) ? "pink" : opColor;
                        _ = App.Sheets.ColorCellAsync(rec.RowNum, 3, mgColor);
                    }
                }
            }

            // detailsFailed строится из reallyFailed (то что после ВСЕХ фаз не нашлось)
            detailsFailed.AddRange(reallyFailed);
            // Скорректируем итоговую статистику: если verify не нашёл — это ошибка
            // (но в API режиме редкий случай — верификация по 4 признакам)
            if (reallyFailed.Count > statsErrors)
                statsErrors = reallyFailed.Count;

            // ── Итог ────────────────────────────────────────────────────
            sw.Stop();
            var elapsed = (int)sw.Elapsed.TotalSeconds;
            log(SEP, LogLevel.Dim);
            log($"  ✅  Создано проектов:    {statsCreated}", LogLevel.Ok);
            log($"  📞  Полностью телефонов: {cntPhonesOk}", LogLevel.Info);
            if (cntPhonesDup > 0)
                log($"  ⏭   Пропущено (дубли):  {cntPhonesDup} тел.", LogLevel.Dim);
            if (reallyFailed.Count > 0)
            {
                log($"  ❌  Не созданы:          {reallyFailed.Count} проектов", LogLevel.Err);
                foreach (var name in reallyFailed.Take(20))
                    log($"       ❌ {name}", LogLevel.Err);
                if (reallyFailed.Count > 20)
                    log($"       ... и ещё {reallyFailed.Count - 20}", LogLevel.Err);
            }
            log(SEP, LogLevel.Dim);
            log($"🏁 ГОТОВО!  ⏱ {elapsed / 60:00}:{elapsed % 60:00}  ·  🚀 API · операторы: {opsSummary}", LogLevel.Bold);
            log(SEP, LogLevel.Dim);

            cb.StatsChanged?.Invoke(statsCreated, statsSkipped, statsErrors);
            cb.StatusTextChanged?.Invoke($"✅ Готово! Создано {statsCreated}, пропущено {statsSkipped}, ошибок {statsErrors}  ⏱ {elapsed / 60:00}:{elapsed % 60:00}");
            cb.ProgressChanged?.Invoke(totalProjects, totalProjects);

            // ── Сохраняем в историю с полной детализацией ──────────────
            App.History.Add(new SessionHistory
            {
                Date           = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
                Site           = opts.Site,
                Mode           = "🚀 API",
                Created        = statsCreated,
                Skipped        = statsSkipped,
                Errors         = statsErrors,
                ElapsedSeconds = elapsed,
                Details        = BuildDetailsReport(opts, statsCreated, statsSkipped, statsErrors,
                                                    cntPhonesOk, cntPhonesDup,
                                                    detailsCreated, detailsSkipped, detailsFailed,
                                                    elapsed),
            });

            if (statsErrors > 0 || reallyFailed.Count > 0) App.Sound.PlayError();
            else App.Sound.PlayDone();
        }
        catch (OperationCanceledException)
        {
            log("⏹ Остановлено пользователем", LogLevel.Warn);
            cb.StatusTextChanged?.Invoke("⏹ Остановлено");
        }
        catch (Exception ex)
        {
            log($"💥 Непредвиденная ошибка: {ex.GetType().Name}: {ex.Message}", LogLevel.Err);
        }
        finally
        {
            IsRunning = false;
            IsPaused = false;
            _cts?.Dispose();
            _cts = null;
        }
    }
}
