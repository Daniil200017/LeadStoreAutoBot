using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LeadStoreAutoBot.Models.Api;
using LeadStoreAutoBot.Resources;

namespace LeadStoreAutoBot.Services;

/// <summary>
/// Клиент prostats.info API. Эндпоинт: <see cref="Constants.ApiEndpoint"/>.
/// Все запросы — POST JSON, токен в теле.
/// </summary>
public class ProstatsApi
{
    private readonly HttpClient _http;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(40);

    public ProstatsApi()
    {
        var handler = new HttpClientHandler
        {
            // Python отключал SSL верификацию (verify=False) — повторяем
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        _http = new HttpClient(handler) { Timeout = _timeout };
    }

    /// <summary>Базовый вызов API. Возвращает (десериализованный result, текст ошибки или null).</summary>
    private async Task<(JsonElement? Result, string? Error, bool Timeout)> CallAsync(string token, object payload, CancellationToken ct = default)
    {
        try
        {
            var dict = new Dictionary<string, object?> { ["token"] = token };
            // Сливаем payload в общий dict
            var elem = JsonSerializer.SerializeToElement(payload);
            foreach (var prop in elem.EnumerateObject())
                dict[prop.Name] = prop.Value;

            using var resp = await _http.PostAsJsonAsync(Constants.ApiEndpoint, dict, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement.Clone();
                var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
                if (status == "success")
                {
                    var hasResult = root.TryGetProperty("result", out var res);
                    return (hasResult ? res : null, null, false);
                }
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                return (null, msg ?? $"API вернул status='{status}'", false);
            }
            catch (JsonException)
            {
                return (null, $"Невалидный JSON в ответе: {body[..Math.Min(200, body.Length)]}", false);
            }
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient таймаут (40 сек) — Python считает это успехом, повторим логику
            return (null, "Таймаут соединения с API", true);
        }
        catch (HttpRequestException ex)
        {
            return (null, $"Сеть: {ex.Message}", false);
        }
        catch (Exception ex)
        {
            return (null, ex.Message, false);
        }
    }

    /// <summary>
    /// Получить список всех проектов через gck_projects и собрать снэпшот для проверки дублей.
    /// </summary>
    public async Task<ExistingProjectsSnapshot> GetExistingProjectsAsync(string token, CancellationToken ct = default)
    {
        var (result, err, _) = await CallAsync(token, new { command = "gck_projects" }, ct);
        if (err != null) throw new InvalidOperationException(err);
        if (result == null) return new ExistingProjectsSnapshot();

        var snapshot = new ExistingProjectsSnapshot();
        if (result.Value.ValueKind != JsonValueKind.Array) return snapshot;

        var prefix = new Regex(@"^B\d+_");
        foreach (var p in result.Value.EnumerateArray())
        {
            string name    = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string src     = p.TryGetProperty("src",  out var s) ? s.GetString() ?? "" : "";
            string content = p.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

            if (!string.IsNullOrEmpty(name))
            {
                snapshot.FullNames.Add(name);
                snapshot.BaseNames.Add(prefix.Replace(name, ""));
                snapshot.NameSrc.Add((name, src));
            }
            if (!string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(src))
                snapshot.ContentSrc.Add((content.Trim(), src));
        }
        return snapshot;
    }

    /// <summary>
    /// Создаёт ОДИН проект для конкретного оператора.
    /// </summary>
    /// <returns>(success, isTimeout, errorMessage). isTimeout=true считается успехом (как в Python).</returns>
    public async Task<(bool Success, bool TimedOut, string? Error)> CreateProjectAsync(
        string token,
        ProjectCreateRequest request,
        CancellationToken ct = default)
    {
        var (_, err, timedOut) = await CallAsync(token, request, ct);
        if (timedOut) return (true, true, null);
        if (err != null) return (false, false, err);
        return (true, false, null);
    }
}
