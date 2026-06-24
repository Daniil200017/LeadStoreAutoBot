using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LeadStoreAutoBot.Services;

// Диагностика: какие операторы (B1..B4) реально есть на аккаунте.
// API gck_projects не возвращает src, поэтому группируем по префиксу имени.
public static class OperatorDetector
{
    private static readonly Regex PrefixRe = new(@"^(B\d+)_", RegexOptions.Compiled);

    public record OperatorGuess(string Prefix, int Count, string Example);

    public static async Task<List<OperatorGuess>> DetectAsync(ProstatsApi api, string token)
    {
        var snap = await api.GetExistingProjectsAsync(token);

        var map = new Dictionary<string, (int Count, string Example)>();

        foreach (var name in snap.FullNames)
        {
            var m = PrefixRe.Match(name);
            if (!m.Success) continue;
            var prefix = m.Groups[1].Value;
            if (map.TryGetValue(prefix, out var v))
                map[prefix] = (v.Count + 1, v.Example);
            else
                map[prefix] = (1, name);
        }

        return map
            .Select(kv => new OperatorGuess(kv.Key, kv.Value.Count, kv.Value.Example))
            .OrderBy(g => g.Prefix)
            .ToList();
    }
}
