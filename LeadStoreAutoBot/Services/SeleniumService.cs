using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using LeadStoreAutoBot.Resources;

namespace LeadStoreAutoBot.Services;

/// <summary>
/// Автоматизация Chrome через Selenium WebDriver. Селекторы и тайминги — 1:1 с Python-версией
/// (см. selenium_login и create_project в leadstore_gui_obf.py:172-365).
/// Selenium Manager (встроен в Selenium 4.6+) сам скачает chromedriver под версию Chrome.
/// </summary>
public class SeleniumService : IDisposable
{
    private IWebDriver? _driver;
    public bool IsStarted => _driver != null;

    public IWebDriver Driver => _driver ?? throw new InvalidOperationException("Driver не запущен.");

    public void Start()
    {
        if (_driver != null) return;
        var opts = new ChromeOptions();
        opts.AddArgument("--start-maximized");
        opts.AddArgument("--disable-notifications");
        opts.AddArgument("--disable-features=RendererCodeIntegrity");
        opts.AddExcludedArgument("enable-automation");
        opts.AddAdditionalOption("useAutomationExtension", false);

        var service = ChromeDriverService.CreateDefaultService();
        service.HideCommandPromptWindow = true;

        _driver = new ChromeDriver(service, opts);
    }

    public void Quit()
    {
        try { _driver?.Quit(); } catch { }
        _driver = null;
    }

    public void Dispose() => Quit();

    /// <summary>Обычный логин по email/password.</summary>
    public void Login(string login, string password, string siteUrl)
    {
        var d = Driver;
        d.Navigate().GoToUrl($"{siteUrl}/login");
        Thread.Sleep(2000);
        d.FindElement(By.Name("LoginForm[username]")).SendKeys(login);
        var pwd = d.FindElement(By.Name("LoginForm[password]"));
        pwd.SendKeys(password);
        pwd.SendKeys(Keys.Return);
        Thread.Sleep(3000);
        if (d.Url.Contains("login"))
            throw new InvalidOperationException("Неверный логин или пароль");
    }

    /// <summary>Быстрый вход по invite-ссылке.</summary>
    public void QuickLogin(string inviteUrl)
    {
        var d = Driver;
        d.Navigate().GoToUrl(inviteUrl);
        Thread.Sleep(3000);
    }

    /// <summary>Список существующих проектов на странице /admin/visit/rt (для skip_dup).</summary>
    public HashSet<string> GetExistingProjectNames(string siteUrl)
    {
        var d = Driver;
        d.Navigate().GoToUrl($"{siteUrl}/admin/visit/rt");
        Thread.Sleep(2000);
        var rows = d.FindElements(By.CssSelector("table tbody tr td:nth-child(3)"));
        var result = new HashSet<string>();
        foreach (var r in rows)
        {
            var t = r.Text?.Trim();
            if (!string.IsNullOrEmpty(t)) result.Add(t);
        }
        return result;
    }

    /// <summary>
    /// Создать ОДИН проект. Имя: {phone}_{site}_{phoneType}.
    /// Бросает исключение если что-то не получилось.
    /// </summary>
    public void CreateProject(
        string phone, string phoneType, string sourceType, string tag,
        string[] regions, int limit, string[] days, string site, string siteUrl,
        string[]? operatorPrefixes = null)
    {
        var d = Driver;
        var js = (IJavaScriptExecutor)d;
        var projectName = $"{phone}_{site}_{phoneType}";

        d.Navigate().GoToUrl($"{siteUrl}/admin/visit/rt");
        WaitForPage(d);
        Thread.Sleep(1500);

        // Кнопка "Добавить проект"
        try
        {
            var btn = WaitFind(d, By.XPath("//span[contains(text(),'Добавить проект')]"));
            js.ExecuteScript("arguments[0].click();", btn);
        }
        catch
        {
            var btn = d.FindElement(By.CssSelector(".deal-req-is-empty-btn"));
            js.ExecuteScript("arguments[0].click();", btn);
        }
        Thread.Sleep(2000);

        // Тег
        var tagInput = WaitFind(d, By.XPath("//label[@for='tag']/following::input[1]"));
        js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", tagInput);
        tagInput.Clear();
        tagInput.SendKeys(tag);
        Thread.Sleep(400);

        // Имя
        var nameInput = WaitFind(d, By.XPath("//label[@for='name']/following::input[1]"));
        js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", nameInput);
        nameInput.Clear();
        nameInput.SendKeys(projectName);
        Thread.Sleep(400);

        // Источник
        var sourceText = sourceType == "sites" ? "Сайты" : "Звонки";
        try
        {
            var typeInput = WaitFind(d, By.XPath("//input[@placeholder='выберите Источники сбора']"));
            js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", typeInput);
            typeInput.Click();
            Thread.Sleep(800);
            var opt = WaitFind(d, By.XPath(
                $"//li[contains(@class,'el-select-dropdown__item') and contains(.,'{sourceText}')]"));
            js.ExecuteScript("arguments[0].click();", opt);
            Thread.Sleep(500);
        }
        catch { }

        // Регионы
        if (regions.Length > 0)
        {
            try
            {
                try { d.FindElement(By.XPath("//label[@for='tag']")).Click(); Thread.Sleep(300); } catch { }
                d.FindElement(By.TagName("body")).SendKeys(Keys.Escape);
                Thread.Sleep(400);

                var regionFilter = d.FindElement(By.XPath("//input[@placeholder='Фильтр по регионам']"));
                js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", regionFilter);
                foreach (var region in regions)
                {
                    js.ExecuteScript(
                        "var el=arguments[0]; el.value=arguments[1];" +
                        "el.dispatchEvent(new Event('input', {bubbles:true}));",
                        regionFilter, region);
                    Thread.Sleep(700);
                    try
                    {
                        var node = d.FindElement(By.XPath(
                            $"//span[contains(@class,'el-tree-node__label') " +
                            $"and normalize-space(text())='{region}']" +
                            $"/ancestor::div[contains(@class,'el-tree-node__content')]" +
                            $"//span[contains(@class,'el-checkbox__inner')]"));
                        js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", node);
                        js.ExecuteScript("arguments[0].click();", node);
                        Thread.Sleep(300);
                    }
                    catch { }
                }
                js.ExecuteScript(
                    "var el=arguments[0]; el.value='';" +
                    "el.dispatchEvent(new Event('input', {bubbles:true}));",
                    regionFilter);
                Thread.Sleep(300);
            }
            catch { }
        }

        // Номер телефона / домен
        Thread.Sleep(500);
        var textareas = d.FindElements(By.CssSelector("textarea.el-textarea__inner"));
        IWebElement? phoneTextarea = null;
        foreach (var ta in textareas)
        {
            if (!ta.Displayed) continue;
            var ph = (ta.GetDomAttribute("placeholder") ?? "").ToLower();
            if (ph.Contains("номер") || ph.Contains("домен") || ph.Contains("79"))
            {
                phoneTextarea = ta;
                break;
            }
        }
        phoneTextarea ??= textareas.FirstOrDefault(t => t.Displayed);
        if (phoneTextarea != null)
        {
            js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", phoneTextarea);
            Thread.Sleep(300);
            js.ExecuteScript(
                "var el=arguments[0]; el.value=arguments[1];" +
                "el.dispatchEvent(new Event('input', {bubbles:true}));" +
                "el.dispatchEvent(new Event('change', {bubbles:true}));",
                phoneTextarea, phone);
            Thread.Sleep(400);
        }

        // Лимит
        try
        {
            var limitInput = d.FindElement(By.CssSelector(".el-input-number input"));
            js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", limitInput);
            js.ExecuteScript(
                "var el=arguments[0]; el.value=arguments[1];" +
                "el.dispatchEvent(new Event('input', {bubbles:true}));" +
                "el.dispatchEvent(new Event('change', {bubbles:true}));" +
                "el.dispatchEvent(new Event('blur', {bubbles:true}));",
                limitInput, limit.ToString());
            Thread.Sleep(300);
        }
        catch { }

        // Дни недели
        Thread.Sleep(300);
        var needDays = days.Where(Constants.DayLabels.ContainsKey)
                          .Select(k => Constants.DayLabels[k]).ToHashSet();
        foreach (var lbl in Constants.DayLabels.Values)
        {
            try
            {
                var labelEl = d.FindElement(By.XPath(
                    $"//span[contains(@class,'el-checkbox__label') and contains(text(),'{lbl}')]" +
                    $"/parent::label[contains(@class,'el-checkbox')]"));
                js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", labelEl);

                var classAttr = labelEl.GetDomAttribute("class") ?? "";
                bool isChecked = classAttr.Contains("is-checked");
                bool should = needDays.Contains(lbl);

                if (isChecked != should)
                {
                    var inner = labelEl.FindElement(By.CssSelector(".el-checkbox__inner"));
                    js.ExecuteScript("arguments[0].click();", inner);
                    Thread.Sleep(150);
                }
            }
            catch { }
        }

        // Операторы (B1..B4): выставляем галочки точно по выбору.
        // Нет выбора — дефолт: B1/B2/B3 вкл, остальные выкл.
        Thread.Sleep(300);
        try
        {
            var wantOps = (operatorPrefixes != null && operatorPrefixes.Length > 0)
                ? operatorPrefixes
                : new[] { "B1", "B2", "B3" };

            foreach (var opName in new[] { "B1", "B2", "B3", "B4" })
            {
                try
                {
                    var block = d.FindElement(By.XPath(
                        "//label[@for='srcrt']/ancestor::div[contains(@class,'el-form-item')][1]" +
                        "//div[contains(@class,'el-form-item__content')]"));

                    IWebElement? lbl = null;
                    foreach (var cand in block.FindElements(By.CssSelector("label.el-checkbox")))
                    {
                        try
                        {
                            var t = cand.FindElement(By.CssSelector(".el-checkbox__label")).Text.Trim();
                            if (t == opName) { lbl = cand; break; }
                        }
                        catch { }
                    }
                    if (lbl == null) continue;

                    var classAttr = lbl.GetDomAttribute("class") ?? "";
                    bool isChecked = classAttr.Contains("is-checked");
                    bool want = Array.IndexOf(wantOps, opName) >= 0;

                    if (want != isChecked)
                    {
                        var inner = lbl.FindElement(By.CssSelector(".el-checkbox__inner"));
                        js.ExecuteScript("arguments[0].click();", inner);
                        Thread.Sleep(300);
                    }
                }
                catch { }
            }
        }
        catch { }
        Thread.Sleep(300);

        // Сохранить
        Thread.Sleep(500);
        var saveBtns = d.FindElements(By.XPath(
            "//button[contains(@class,'el-button') and contains(.,'Сохранить')]"));
        IWebElement? saveBtn = null;
        foreach (var b in saveBtns)
        {
            if (b.Displayed && !((b.GetDomAttribute("class") ?? "").Contains("disabled")))
            {
                saveBtn = b; break;
            }
        }
        if (saveBtn == null)
            throw new InvalidOperationException("Кнопка Сохранить не найдена");

        js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", saveBtn);
        Thread.Sleep(500);
        js.ExecuteScript("arguments[0].click();", saveBtn);
        try
        {
            new WebDriverWait(d, TimeSpan.FromSeconds(10)).Until(driver =>
            {
                try
                {
                    var el = driver.FindElement(By.XPath(
                        "//button[contains(@class,'el-button') and contains(.,'Сохранить') " +
                        "and not(contains(@class,'disabled'))]"));
                    return !el.Displayed;
                }
                catch (NoSuchElementException) { return true; }
            });
        }
        catch { }
        Thread.Sleep(2000);
    }

    private static void WaitForPage(IWebDriver d, int timeoutSec = 15)
    {
        new WebDriverWait(d, TimeSpan.FromSeconds(timeoutSec)).Until(drv =>
            ((IJavaScriptExecutor)drv).ExecuteScript("return document.readyState")?.ToString() == "complete");
        Thread.Sleep(500);
    }

    private static IWebElement WaitFind(IWebDriver d, By by, int timeoutSec = 15)
    {
        return new WebDriverWait(d, TimeSpan.FromSeconds(timeoutSec)).Until(drv => drv.FindElement(by));
    }
}
