# AGENTS.md — LeadStoreAutoBot (C# / WPF)

Этот файл — память для AI-агентов (Devin/Claude/Cursor), работающих с C#-портом
LeadStore Bot. Всё, что нужно знать про сборку, архитектуру и сделанные правки.

> Корневой `../CLAUDE.md` описывает старую Python-версию. **Активная разработка
> ведётся здесь, в `csharp/`.**

---

## 1. Структура проекта

```
csharp/
├── LeadStoreAutoBot.sln
├── LeadStoreAutoBot_Setup.iss      # Inno Setup-скрипт установщика
├── LeadStoreAutoBot_Setup.exe      # Готовый установщик (артефакт сборки)
├── build_installer.bat             # Полный пайплайн: publish + iscc
└── LeadStoreAutoBot/
    ├── App.xaml(.cs)               # Точка входа, ResourceDictionaries
    ├── MainWindow.xaml(.cs)        # Окно + табы (RadioButton-вкладки)
    ├── Models/
    │   ├── PhoneRecord.cs          # ObservableObject, поля: Site/Op/Mg/Status/...
    │   ├── BotConfig.cs
    │   ├── ProjectInfo.cs
    │   ├── SessionHistory.cs
    │   ├── ThemePreset.cs
    │   └── Api/                    # DTO для prostats API
    ├── ViewModels/
    │   ├── MainViewModel.cs        # Главная VM (Records, Logs, Start/Pause/Stop)
    │   ├── ViewModelBase.cs
    │   ├── DayItem.cs / RegionItem.cs
    ├── Views/
    │   ├── MainTab.xaml(.cs)       # Лог + статистика + кнопки запуска
    │   ├── TableTab.xaml(.cs)      # DataGrid с записями
    │   ├── SettingsTab.xaml(.cs)
    │   ├── SettingsPanel.xaml(.cs) # Левая панель главной
    │   ├── HistoryTab.xaml(.cs)
    │   └── HelpTab.xaml(.cs)
    ├── Services/
    │   ├── BotRunner.cs            # Главная оркестрация (Selenium + API режимы)
    │   ├── SeleniumService.cs
    │   ├── ProstatsApi.cs
    │   ├── GoogleSheetsService.cs
    │   ├── ConfigService.cs / HistoryService.cs / LogService.cs
    │   ├── ThemeService.cs / SoundService.cs / AppPaths.cs
    │   └── BouncyCastleRsa.cs      # JWT для Google Service Account
    ├── Themes/
    │   ├── DefaultBrushes.xaml     # Кисти темы (Bg/Bg2/Bg3/Text/Accent...)
    │   └── Styles.xaml             # Card, MutedText, TabRadio и т.п.
    ├── Helpers/
    │   └── Converters.cs           # BoolToVis, InverseBool, StatusToBrush
    └── Resources/                  # Constants (Sites, Days, RegionCodes), сэмплы
```

UI-фреймворк: **WPF + WPF-UI (`<ui:ControlsDictionary/>`) + CommunityToolkit.Mvvm**.

---

## 2. Команды (всегда выполнять из `csharp/`)

### Билд (быстрая проверка компиляции)
```bash
dotnet build LeadStoreAutoBot.sln -c Debug -v minimal
```
Артефакт: `LeadStoreAutoBot/bin/Debug/net9.0-windows/win-x64/LeadStoreAutoBot.exe`

### Publish — single-file self-contained (для установщика)
```bash
dotnet publish LeadStoreAutoBot/LeadStoreAutoBot.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -v minimal
```
Артефакт: `LeadStoreAutoBot/bin/Release/net9.0-windows/win-x64/publish/LeadStoreAutoBot.exe` (~92 МБ)

### Сборка установщика (Inno Setup)
```bash
"/c/Program Files (x86)/Inno Setup 6/iscc.exe" LeadStoreAutoBot_Setup.iss
```
Артефакт: `csharp/LeadStoreAutoBot_Setup.exe` (~88 МБ)

### Полный пайплайн одной командой
```bash
cmd.exe /c build_installer.bat
```
**ВАЖНО**: bat-файл заканчивается `pause` — при запуске из агента уходит фоном
и не возвращает контроль. Лучше выполнять `dotnet publish` и `iscc` по
отдельности (см. выше).

---

## 3. Сделанные изменения в этой сессии (2026-05-08)

### 3.1 «Живая» подсветка строк во вкладке Таблица + автоскролл

**Что было**: при старте бота в режиме Google Sheets таблица в UI оставалась
пустой; статусы строк (`active/ok/err/dup/skip`) не отрисовывались, потому что
`BotRunner` грузил записи в локальный `workRecs`, а UI смотрел в свой
`MainViewModel.Records` — это были **разные экземпляры** `PhoneRecord`.

**Решение**:
- В `Services/BotRunner.cs` → `BotRunCallbacks` добавлен колбэк
  `Action<IReadOnlyList<PhoneRecord>>? RowsLoaded`. Он вызывается сразу после
  загрузки Sheets (и в Selenium, и в API режиме) и передаёт **те самые
  экземпляры**, которые потом будут красить через `RowStatusChanged`.
- В `ViewModels/MainViewModel.cs.StartAsync`:
  - сбрасывает `Status`/`StatusMessage` всех существующих записей (кроме `no_site`);
  - на `RowsLoaded` отписывает старые `Records`, чистит коллекцию, добавляет
    новые `r` (с `r.PropertyChanged += Record_PropertyChanged`), и зовёт
    `EnsureEmptyRows()`;
  - на `RowStatusChanged` со `status == "active"` обновляет
    `[ObservableProperty] PhoneRecord? ActiveRecord`.
- В `Views/TableTab.xaml.cs` слушает `MainViewModel.PropertyChanged` →
  при изменении `ActiveRecord` зовёт `Grid.ScrollIntoView(rec)`.

### 3.2 Подсветка ячеек, а не строк (важно!)

**Проблема**: WPF-UI (`<ui:ControlsDictionary/>` в App.xaml) подменяет
`ControlTemplate` у `DataGridRow`. `DataTrigger` с `Setter Property="Background"`
на `DataGridRow` через их шаблон **не пробивается** — Background задан
жёстко через TemplateBinding на BackgroundBrush.

**Решение** (в `Views/TableTab.xaml`): красим **каждую ячейку** через
собственный `Style TargetType="DataGridCell"` со своим `ControlTemplate`
(Border вокруг ContentPresenter) и `DataTrigger`-ами на `Status`. Визуально
строка выглядит полностью окрашенной, потому что её ячейки закрашены одинаково.

Цвета:
| Status   | Hex       | Что значит |
|----------|-----------|------------|
| `active` | `#3a3a00` | сейчас обрабатывается (жёлтый) |
| `ok`     | `#1a3a1a` | создан (зелёный) |
| `err`    | `#3a1a1a` | ошибка (красный) |
| `dup`    | `#3a1a2a` | дубль (розовый) |
| `skip`   | `#3a1a2a` | пропущен (розовый) |
| `no_site`| `#1a2a3a` | нет источника (синий) |

Если в будущем понадобится трогать стили DataGrid — **никогда не задавай
Background через RowStyle**, всегда через CellStyle (или ставь
`<Style TargetType="DataGridRow" BasedOn="{StaticResource {x:Type DataGridRow}}">`
и переопределяй Template).

### 3.3 Умный автоскролл логов + плавающая кнопка «вниз» (как в Telegram)

В `Views/MainTab.xaml` лог обёрнут в `<Grid>` поверх `RichTextBox` положена
круглая кнопка `▼` (`Ellipse` с `DropShadowEffect`).

В `Views/MainTab.xaml.cs`:
- при `Loaded` через `VisualTreeHelper` достаём `ScrollViewer` из шаблона
  RichTextBox;
- подписываемся на `ScrollChanged`:
  - `ExtentHeightChange > 0` (новый лог) + `_logAutoScroll == true` →
    `ScrollToEnd()`;
  - `VerticalChange != 0` (юзер сам крутит) → пересчитываем `_logAutoScroll`
    как `atBottom = VerticalOffset >= ScrollableHeight - 4`;
- кнопка видна когда `_logAutoScroll == false`;
- клик → `ScrollToEnd()` + сброс `_logAutoScroll = true`.

---

## 4. Архитектурные нюансы (чтобы не наступить)

### 4.1 BotRunner и UI — два набора PhoneRecord

В `BotRunner.RunSeleniumAsync`/`RunApiAsync` **локальная** переменная
`workRecs: List<PhoneRecord>`. Источник:
- если `opts.UseManualTable` — копируется из `opts.Records` (строки, что
  пользователь ввёл руками во вкладке Таблица);
- иначе — загружается через `App.Sheets.LoadPhonesAsync(...)` (новые экземпляры).

UI знает только про `MainViewModel.Records`. Чтобы **визуальные** статусы
работали — `BotRunner` обязан вызвать `cb.RowsLoaded?.Invoke(workRecs)` после
загрузки Sheets, чтобы UI заменил `Records` на эти же экземпляры. Не ломай
этот контракт.

### 4.2 Логи

`Services/LogService.cs` — событие `EntryAdded`. `MainViewModel.OnLogEntry`
делает `Application.Current.Dispatcher.BeginInvoke(...)` и пушит `Paragraph`
в `LogDocument` (FlowDocument). `MainTab.OnDataContextChanged` присваивает
`LogBox.Document = vm.LogDocument`.

`LogLevel`: `Info`, `Dim`, `Warn`, `Ok`, `Err`, `Bold`. Цвета берутся из
ResourceDictionary (`AccentBrush`, `RedBrush`, `YellowBrush`, ...).

### 4.3 Тёмная тема

`<ui:ThemesDictionary Theme="Dark"/>` — фиксированно тёмная. Все цвета через
`{DynamicResource Bg2Brush}` и т.п. Локальные кисти переопределяются в
`Themes/DefaultBrushes.xaml` (их подменяет `ThemeService.Apply` через
`Application.Current.Resources` для смены акцентов).

### 4.4 Selenium vs API

В `MainViewModel.StartAsync`:
```csharp
if (UseApiMode)
    await Task.Run(() => App.Bot.RunApiAsync(opts, callbacks));
else
    await Task.Run(() => App.Bot.RunSeleniumAsync(opts, callbacks));
```
`UseApiMode` — выбирается через комбобокс «🚀 API режим» в `SiteNames`.
`UseQuickUrl` — «⚡ Быстрый вход» (Selenium с готовой session-ссылкой).

API эндпоинт по умолчанию: `prostats.info` (см. `Services/ProstatsApi.cs`).
Если у юзера сетевой таймаут на `prostats.info:443` — **это не баг бота**,
проблема в провайдере / антивирусе / DNS.

---

## 5. Привычки пользователя (выявлено в практике)

- **Запускает бота через установщик** `LeadStoreAutoBot_Setup.exe`, а не
  напрямую из `bin/Release/.../publish/`. После любых правок надо
  пересобирать установщик, иначе он будет тестировать старую версию.
- Любит автоматизацию для **визуальной** обратной связи (подсветка строк,
  скролл к активной, кнопка-вниз). НЕ любит принудительное переключение
  вкладок — делать всё ненавязчиво.
- Часто отправляет логи приложения как доказательство — это удобно для
  отладки, ориентируйся на них.
- Общается на русском, можно отвечать так же.

---

## 6. Текущий BouncyCastle warning (не критично)

`Services/BouncyCastleRsa.cs(53,35): warning CS8604` — null-safety
предупреждение в `CreateDigest(string name)`. Не трогаем; оно не влияет
на работу. Если будем рефакторить — добавить `name ?? throw...` или
`?` в сигнатуру.

---

*Последнее обновление: 2026-05-08, после правки подсветки таблицы и
автоскролла логов.*
