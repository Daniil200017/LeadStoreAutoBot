using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LeadStoreAutoBot.Models;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace LeadStoreAutoBot.Services;

/// <summary>
/// Загрузка данных из Google Таблиц через прямые HTTP-запросы (без Google.Apis SDK).
///
/// Зачем не SDK: на машине пользователя Google.Apis.Sheets.v4 молча зависал
/// внутри ExecuteAsync — TCP до Google проходил за 100-300 мс (диагностический
/// пробинг сокета подтвердил), но дальше внутри их HttpClient запрос уходил
/// в никуда. Python-gspread на той же машине работает. Воспроизводимости пока
/// нет, поэтому идём в обход — используем собственный HttpClient,
/// JWT собираем вручную и подписываем через BouncyCastleRsa.
/// </summary>
public class GoogleSheetsService
{
    private const string CredsBase64 =
        "ewogICJ0eXBlIjogInNlcnZpY2VfYWNjb3VudCIsCiAgInByb2plY3RfaWQiOiAibGVhZHN0b3JlLTQ4OTEyMCIsCiAgInByaXZhdGVfa2V5X2lkIjogImQyYjJjOTJlNjBjYjQxODViZjk3M2UwYmMzYzQ5NzMzOWI0YWFlNTAiLAogICJwcml2YXRlX2tleSI6ICItLS0tLUJFR0lOIFBSSVZBVEUgS0VZLS0tLS1cbk1JSUV2QUlCQURBTkJna3Foa2lHOXcwQkFRRUZBQVNDQktZd2dnU2lBZ0VBQW9JQkFRQzJsSHpGN3pEdlUxQnFcbklkRjNWRThlTkdXVDFWYTVSZ0Q4MlJkYVI5OWZiQUtPR1o2bit2aVVYSWc0djc2VTdCazU4NHppV05obXBKS3ZcbnNwYzZZaU02YTBLZVc2cU5ERk9Gd2pjREx5ZzdsRThQQUQzcjZlSGg3NkJBLzU0SHpKSzNRUnM4T0ZhQjROZTdcbjVZK3ZkYXUxOGJZcHJBTjRzMUhnaXlzL2lhZmxhV1BmSFVsNFM3M3NLSkxPeVRHYlNSM3MxTmdMUngwOG1IQXNcbnpQVEdQRnovYmVybEdoSFc0dk9HeXljUTEyMy9LS0pNbmlncFoxb0tOS29IRy85M1ZLeHZ4WkxZOHpSaVRBTi9cbjNPdDU2WjhSME5Kdm5lY00xSEcyYTRQQ1ZrbldjcjROVFZtMURHQk5LT1FoL3JTdU1vK0tScEFacmFLbDVGbTZcbnFZMS9hTkQvQWdNQkFBRUNnZ0VBVlQyTDUwa2R1bzVXRzhiQUtZc0dDUjhEVVhxbnE0WWdUZXY0dUNDWUM4KzhcbmZhVStha1NFcTVkcnpiclBlbTJqOVdkY25neEdzOTBmMHNGNVV6dWdJTlVVM0NRRnd5WS9GRkt4Smw1czFTd1Bcbm9QeEc3STViOUFUUTk2ZWZteHFLWU40WG5nemJibldQb3R5eE1ZU3BieDl6SVk5NmEyNmt5a1dQSW5IZEhnQkZcbk93ZU1JM3pxWnBGZlBiNStYK1hSRVozcEsyalBEU29aQXJFZ2sxbmg2SFQvU2dwQlZtY3BQdUpaT2wyQUxKQkVcbitoWnlRWTdvbksweGozZCtJRHkybXFnN091dG1NM3Z4eXVvWGcyMkpRMnUvQlJtUHhMcXFmTEIxRVNQaUxxMk5cbllXU3RNN2lGSGVhY05zYWNLSFR3YTJEYmV3RkRzbnhZNTVmNm1QQkxDUUtCZ1FEZUtMK0hMRUVDM3h3UGtvSENcbms2RktzR3Y2L2k3Z3lpbW5JT3VuTW5RTS82bms0am9FaFV2QnVsU0Y4TVhLdWFYb09oVDRzYldWVGVEMEFDY3NcbmZQSk9DV0kybXErVld4djZmUjNDK2xzelNMV1FMRTRqOUZyanZKZzgrWUMvUnozcUdDbFg0ZE0xN2UvT25EMm1cblBoVU5ZWng4bVBSRHM1dk5RSmFGMnkxT0l3S0JnUURTWkZLcjZEZCtnU2hHZitrT0NjM2kybTBCd1BjTXBjTVVcbmFJSVdFM2ttMXZ5UGI2Zjl0Nmxyb1Q1bEhNUXFMWVRzUEtqSzNWM2wzeTB1QnZLUmlQSzAwV0JPc0F3OWVNWklcbjAyUXk0ckhNY0lqOGRiRVlOWDdvVmE5ckZVeU9oMmtqRExudGJHV3g5aHE0U2FHc1J4ZUUwUndMUkU4d25PeXhcbndveC9lTldwZFFLQmdCeVcvZDcxY1FCZm1ncmUvZGYraTdsQzd3S0VCNkJpSSs0Z0xITjk2TFZyaVgrdEpXNURcbmdUWlRObUZ1Vk9YNzhqL3FpWnhmc2xDZWp4Nlhqbk1KT1YyVms1QVhaQlZDZmwxRUVMcHc0Wis5OGErMkkvQTRcbm1DSEt1WVRQVHlST2xNYzFpTXlJZ1ZmbFlRRWoxa000cGhqc3dQazQ3ZVp3ak5KalIzdStjeHdsQW9HQUpXNkxcbjl1SGQzYmdFL21ZTGhOL2hyWmJIQmlUYXozaytlQWNQL2ZXQS9KUUxZMG11VGNtN2J0YkZUeUFMRnFYNm5EMCtcbm1yay8xNElaZTdMb3ZWUHNPcGQxMXdvalkxeDFpc2R4Y0V3ODdlNm5zS01QMndySmhYU1pQU2dROHRyTXJkdTVcbnlMQWNkOGtkZitRNXkzanFpa3JaL25jc3o2MWJ2MVNwd3BReEQzRUNnWUFqcVpwWG93ZEtHSmFaY0FEZzkrZTRcblVieEEwODc5Qkd6RTlDKzVhd0ZNQXd3NjBuK3JtejFKdTJvbG9qZ0F5RURXWncvOHBtMW16RE90aDF5dTVYRkxcbjRxdldTUlI0MWdpcUVuSEs1Y0pFaFRpM3JwbjV2TTA5WWUxSHJTRjM5eUdrU0U0QjgvMndPYmtrWHRPQVJhRlhcbjdlcmcxNXFvMFhzbzM3UkZEdDlnNGc9PVxuLS0tLS1FTkQgUFJJVkFURSBLRVktLS0tLVxuIiwKICAiY2xpZW50X2VtYWlsIjogImJvdC00MjZAbGVhZHN0b3JlLTQ4OTEyMC5pYW0uZ3NlcnZpY2VhY2NvdW50LmNvbSIsCiAgImNsaWVudF9pZCI6ICIxMTIwMzUyNjczODc0MzExNzUyNTciLAogICJhdXRoX3VyaSI6ICJodHRwczovL2FjY291bnRzLmdvb2dsZS5jb20vby9vYXV0aDIvYXV0aCIsCiAgInRva2VuX3VyaSI6ICJodHRwczovL29hdXRoMi5nb29nbGVhcGlzLmNvbS90b2tlbiIsCiAgImF1dGhfcHJvdmlkZXJfeDUwOV9jZXJ0X3VybCI6ICJodHRwczovL3d3dy5nb29nbGVhcGlzLmNvbS9vYXV0aDIvdjEvY2VydHMiLAogICJjbGllbnRfeDUwOV9jZXJ0X3VybCI6ICJodHRwczovL3d3dy5nb29nbGVhcGlzLmNvbS9yb2JvdC92MS9tZXRhZGF0YS94NTA5L2JvdC00MjYlNDBsZWFkc3RvcmUtNDg5MTIwLmlhbS5nc2VydmljZWFjY291bnQuY29tIiwKICAidW5pdmVyc2VfZG9tYWluIjogImdvb2dsZWFwaXMuY29tIgp9Cg==";

    private const string Scope = "https://www.googleapis.com/auth/spreadsheets https://www.googleapis.com/auth/drive";

    private HttpClient _http;

    private string? _spreadsheetId;
    private string? _firstSheetTitle;
    private int? _firstSheetId;

    private string? _accessToken;
    private DateTime _accessTokenExpiresUtc;

    private string? _clientEmail;
    private BouncyCastleRsa? _rsa;

    public string? CurrentSpreadsheetId => _spreadsheetId;
    public string? CurrentSheetTitle    => _firstSheetTitle;

    public GoogleSheetsService()
    {
        _http = BuildClient();
    }

    /// <summary>
    /// Создаёт свежий HttpClient. Используется в ctor и для retry — иногда первый
    /// коннект к Google виснет навсегда, а свежий handler пробивается.
    /// На этой машине системный crypto stack нестабилен, и переиспользование
    /// keep-alive коннекта между запросами иногда ломает GET к sheets.googleapis.com.
    /// </summary>
    private static HttpClient BuildClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            Proxy = null,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromSeconds(30),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        handler.SslOptions.EnabledSslProtocols =
            System.Security.Authentication.SslProtocols.Tls12
          | System.Security.Authentication.SslProtocols.Tls13;
        // ALPN: явно говорим серверу что мы умеем ТОЛЬКО HTTP/1.1.
        // Без этого Google по ALPN навязывает HTTP/2 даже если мы просим 1.1.
        handler.SslOptions.ApplicationProtocols = new List<System.Net.Security.SslApplicationProtocol>
        {
            System.Net.Security.SslApplicationProtocol.Http11,
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(25),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LeadStoreAutoBot/2.0");
        client.DefaultRequestHeaders.ConnectionClose = true;
        return client;
    }

    /// <summary>Пересоздаёт _http — после фатального таймаута, чтобы сбросить весь пул.</summary>
    private void RecreateHttpClient()
    {
        try { _http.Dispose(); } catch { }
        _http = BuildClient();
    }

    /// <summary>Парсит URL Google Таблицы и достаёт ID документа.</summary>
    public static string? ExtractSpreadsheetId(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var m = Regex.Match(url, @"/spreadsheets/d/([a-zA-Z0-9-_]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    private void EnsureCredentialsLoaded()
    {
        if (_rsa != null && _clientEmail != null) return;

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(CredsBase64));
        var sa = JsonSerializer.Deserialize<ServiceAccountKey>(json)
            ?? throw new InvalidOperationException("Не удалось распарсить service-account JSON.");

        _clientEmail = sa.ClientEmail;
        _rsa = ImportRsaFromPem(sa.PrivateKey);
    }

    /// <summary>
    /// Получает access_token. Кэширует пока не истёк (с запасом 5 минут).
    /// </summary>
    private async Task<string> GetAccessTokenAsync(Action<string>? log, CancellationToken ct)
    {
        if (_accessToken != null && DateTime.UtcNow < _accessTokenExpiresUtc.AddMinutes(-5))
            return _accessToken;

        EnsureCredentialsLoaded();

        log?.Invoke("собираю JWT");
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var headerJson = "{\"alg\":\"RS256\",\"typ\":\"JWT\"}";
        var claimsJson = JsonSerializer.Serialize(new ClaimsBody
        {
            Iss   = _clientEmail!,
            Scope = Scope,
            Aud   = "https://oauth2.googleapis.com/token",
            Iat   = nowUnix,
            Exp   = nowUnix + 3600,
        });

        string headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        string claimsB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(claimsJson));
        string signingInput = headerB64 + "." + claimsB64;

        var signingBytes = Encoding.UTF8.GetBytes(signingInput);
        var sig = _rsa!.SignData(signingBytes, 0, signingBytes.Length,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        string sigB64 = Base64UrlEncode(sig);

        string jwt = signingInput + "." + sigB64;

        log?.Invoke("POST oauth2.googleapis.com/token");
        using var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
            new KeyValuePair<string, string>("assertion",   jwt),
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var resp = await _http.PostAsync("https://oauth2.googleapis.com/token", form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        sw.Stop();
        log?.Invoke($"ответ {(int)resp.StatusCode} за {sw.ElapsedMilliseconds} мс");

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"OAuth token error {(int)resp.StatusCode}: {Truncate(body, 300)}");

        var tokenResp = JsonSerializer.Deserialize<TokenResponse>(body)
            ?? throw new InvalidOperationException("Не удалось распарсить ответ token endpoint.");

        if (string.IsNullOrEmpty(tokenResp.AccessToken))
            throw new InvalidOperationException($"Пустой access_token в ответе: {Truncate(body, 300)}");

        _accessToken = tokenResp.AccessToken;
        _accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(tokenResp.ExpiresIn > 0 ? tokenResp.ExpiresIn : 3600);
        return _accessToken;
    }

    /// <summary>
    /// Загружает строки из первого листа Google Таблицы.
    /// Колонки: A=site, B=op (основной), C=mg (менеджер).
    /// </summary>
    public async Task<List<PhoneRecord>> LoadPhonesAsync(
        string sheetUrl,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        void Log(string s) => onProgress?.Invoke(s);

        Log("шаг 1: парсинг ссылки");
        var id = ExtractSpreadsheetId(sheetUrl)
            ?? throw new ArgumentException("Не удалось распознать ссылку на Google Таблицу. Ожидается ссылка вида https://docs.google.com/spreadsheets/d/.../edit");
        Log($"шаг 1 ок: id={id}");

        // Диагностический TCP-пробинг — сразу видно если сеть лежит
        await ProbeHostAsync("oauth2.googleapis.com", 443, Log, ct);
        await ProbeHostAsync("sheets.googleapis.com", 443, Log, ct);

        Log("шаг 2: получение access_token");
        var token = await GetAccessTokenAsync(s => Log("    " + s), ct);
        Log("шаг 2 ок: access_token получен");

        Log("шаг 3: метаданные таблицы");
        var metaUrl = $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(id)}?fields=sheets.properties";
        var metaJson = await ApiGetAsync(metaUrl, token, ct);
        Log($"шаг 3 ок: получено {metaJson.Length} байт");

        using var metaDoc = JsonDocument.Parse(metaJson);
        if (!metaDoc.RootElement.TryGetProperty("sheets", out var sheetsArr) ||
            sheetsArr.ValueKind != JsonValueKind.Array || sheetsArr.GetArrayLength() == 0)
            throw new InvalidOperationException("В таблице нет листов или нет доступа к ней.");

        var firstProps = sheetsArr[0].GetProperty("properties");
        var firstTitle = firstProps.TryGetProperty("title", out var t) ? t.GetString() ?? "Лист1" : "Лист1";
        var firstSheetId = firstProps.TryGetProperty("sheetId", out var sid) ? sid.GetInt32() : 0;

        var range = $"{firstTitle}!A:C";
        Log($"шаг 4: чтение значений {range}");
        var valuesUrl = $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(id)}/values/{Uri.EscapeDataString(range)}";
        var valuesJson = await ApiGetAsync(valuesUrl, token, ct);

        using var valuesDoc = JsonDocument.Parse(valuesJson);
        int rowCount = 0;
        var hasValues = valuesDoc.RootElement.TryGetProperty("values", out var valuesEl)
                        && valuesEl.ValueKind == JsonValueKind.Array;
        if (hasValues) rowCount = valuesEl.GetArrayLength();
        Log($"шаг 4 ок: получено {rowCount} строк");

        _spreadsheetId   = id;
        _firstSheetTitle = firstTitle;
        _firstSheetId    = firstSheetId;

        var result = new List<PhoneRecord>();
        if (!hasValues) return result;

        int rowNum = 0;
        foreach (var row in valuesEl.EnumerateArray())
        {
            rowNum++;
            int len = row.GetArrayLength();
            string site = len > 0 ? (row[0].GetString() ?? "").Trim() : "";
            string op   = len > 1 ? (row[1].GetString() ?? "").Trim() : "";
            string mg   = len > 2 ? (row[2].GetString() ?? "").Trim() : "";

            bool hasPhone = op.Any(char.IsDigit) || mg.Any(char.IsDigit);

            if (string.IsNullOrEmpty(site))
            {
                if (hasPhone)
                {
                    result.Add(new PhoneRecord
                    {
                        Site = "", Op = op, Mg = mg,
                        RowNum = rowNum,
                        Status = "no_site",
                        StatusMessage = "нет источника",
                    });
                }
                continue;
            }

            if (!hasPhone) continue;

            result.Add(new PhoneRecord { Site = site, Op = op, Mg = mg, RowNum = rowNum });
        }
        return result;
    }

    /// <summary>
    /// Закрашивает фон ячейки в таблице. Колонка: 1=A(site), 2=B(op), 3=C(mg).
    /// Цвета: yellow=ok, red=dup, pink=mg==op, blue=no_site.
    /// </summary>
    public async Task ColorCellAsync(int rowNum, int columnIdx, string colorKind)
    {
        if (string.IsNullOrEmpty(_spreadsheetId) || _firstSheetId == null) return;
        if (rowNum < 1 || columnIdx < 1) return;

        try
        {
            var token = await GetAccessTokenAsync(null, CancellationToken.None);

            (double r, double g, double b) = colorKind switch
            {
                "yellow" => (1.0,  0.95, 0.6),
                "red"    => (0.97, 0.6,  0.6),
                "pink"   => (1.0,  0.78, 0.86),
                "blue"   => (0.6,  0.78, 1.0),
                _        => (1.0,  1.0,  1.0),
            };

            var payload = new
            {
                requests = new object[]
                {
                    new
                    {
                        repeatCell = new
                        {
                            range = new
                            {
                                sheetId          = _firstSheetId,
                                startRowIndex    = rowNum - 1,
                                endRowIndex      = rowNum,
                                startColumnIndex = columnIdx - 1,
                                endColumnIndex   = columnIdx,
                            },
                            cell = new
                            {
                                userEnteredFormat = new
                                {
                                    backgroundColor = new { red = r, green = g, blue = b }
                                }
                            },
                            fields = "userEnteredFormat.backgroundColor"
                        }
                    }
                }
            };

            var url = $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(_spreadsheetId!)}:batchUpdate";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(payload);

            using var resp = await _http.SendAsync(req);
            // не критично если упало — раскраска fail-safe
        }
        catch
        {
            // игнорируем
        }
    }

    private async Task<string> ApiGetAsync(string url, string accessToken, CancellationToken ct)
    {
        // На этой машине первый GET к sheets.googleapis.com иногда виснет до таймаута.
        // Делаем 3 попытки с пересозданием HttpClient и новым per-attempt таймаутом 20с,
        // чтобы не ждать дефолтные 25 на каждой неудаче.
        Exception? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                req.Version = HttpVersion.Version11;
                req.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
                req.Headers.ConnectionClose = true;

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, attemptCts.Token);
                var body = await resp.Content.ReadAsStringAsync(attemptCts.Token);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"Sheets API error {(int)resp.StatusCode} на {url}: {Truncate(body, 400)}");
                return body;
            }
            catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
            {
                last = oce;
                if (attempt < 3) RecreateHttpClient();
            }
            catch (HttpRequestException hre)
            {
                last = hre;
                if (attempt < 3) RecreateHttpClient();
            }
        }
        throw new InvalidOperationException(
            $"Sheets API GET виснет на {url} (3 попытки по 20с каждая). " +
            $"Возможно антивирус с TLS-инспекцией блокирует HTTPS-keep-alive. " +
            $"Последняя ошибка: {last?.GetType().Name}: {last?.Message}", last);
    }

    /// <summary>
    /// Парсит PKCS#8 RSA из PEM через BouncyCastle и оборачивает в BouncyCastleRsa.
    /// Никогда не зовёт системный Windows crypto — обходит ошибки 0xc1000001 / 0x8007001F.
    /// </summary>
    private static BouncyCastleRsa ImportRsaFromPem(string pem)
    {
        var body = pem
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();
        var der = Convert.FromBase64String(body);

        var keyParams = PrivateKeyFactory.CreateKey(der);
        if (keyParams is not RsaPrivateCrtKeyParameters rsaKey)
            throw new InvalidOperationException(
                $"Ключ из PEM не является RSA private key (получили {keyParams.GetType().Name}).");

        return new BouncyCastleRsa(rsaKey);
    }

    /// <summary>
    /// Быстрая проверка что TCP-соединение до хоста устанавливается. Логирует время.
    /// Если вернулось за &lt;1с — сеть здорова. Если повисло — фаервол/DNS/прокси.
    /// </summary>
    private static async Task ProbeHostAsync(string host, int port, Action<string> log, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            using var probeCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(TimeSpan.FromSeconds(5));
            await tcp.ConnectAsync(host, port, probeCts.Token);
            sw.Stop();
            log($"net-probe ok: {host}:{port} → {sw.ElapsedMilliseconds} мс");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            log($"net-probe TIMEOUT: {host}:{port} (>{sw.ElapsedMilliseconds} мс) — сеть/фаервол блокирует");
        }
        catch (Exception ex)
        {
            sw.Stop();
            log($"net-probe FAIL: {host}:{port} — {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max) + "...";

    private class ServiceAccountKey
    {
        [JsonPropertyName("client_email")] public string ClientEmail { get; set; } = "";
        [JsonPropertyName("private_key")]  public string PrivateKey  { get; set; } = "";
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("expires_in")]   public int    ExpiresIn   { get; set; }
        [JsonPropertyName("token_type")]   public string TokenType   { get; set; } = "";
    }

    private class ClaimsBody
    {
        [JsonPropertyName("iss")]   public string Iss   { get; set; } = "";
        [JsonPropertyName("scope")] public string Scope { get; set; } = "";
        [JsonPropertyName("aud")]   public string Aud   { get; set; } = "";
        [JsonPropertyName("iat")]   public long   Iat   { get; set; }
        [JsonPropertyName("exp")]   public long   Exp   { get; set; }
    }
}
