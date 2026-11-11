using Syslog.Shared.Models;

namespace Syslog.Shared.Common;

/// <summary>与日志检索保持同一字段和 ASCII 全词语义；不把实时筛选偷偷扩展成子串或正则。</summary>
public static class LiveLogFilterValidation
{
    public const int MaximumSources = 10_000;
    public const long MinimumCacheBytes = 16L * 1024 * 1024;
    public const long MaximumCacheBytes = 2L * 1024 * 1024 * 1024;

    public static bool ValidSettings(LiveLogCacheSettings settings) =>
        settings is not null && settings.PerDeviceLimit is >= 100 and <= 50_000 && settings.MaxBytes is >= MinimumCacheBytes and <= MaximumCacheBytes;

    public static ApiResult<LiveLogFilter> Validate(LiveLogFilter filter, IReadOnlyCollection<string> allowedSourceIds)
    {
        if (filter is null || !ValidScope(allowedSourceIds) || filter.SourceId?.Length > 128
            || filter.From is { } from && from.Ticks % TimeSpan.TicksPerMillisecond != 0
            || filter.To is { } to && to.Ticks % TimeSpan.TicksPerMillisecond != 0
            || filter.From is { } start && filter.To is { } end && (start >= end || end - start > TimeSpan.FromDays(30))
            || filter.Severity is < 0 or > 7 || filter.Hostname?.Length > 255 || filter.AppName?.Length > 255
            || filter.Keywords?.Length > 512 || filter.Disposition is not ("all" or "normal" or "ignored"))
            return ApiResult<LiveLogFilter>.Fail("invalid_query", "实时日志筛选条件超出限制。");
        if (!string.IsNullOrWhiteSpace(filter.Keywords)
            && (filter.Keywords.Any(character => character > 127) || !filter.Keywords.Any(char.IsAsciiLetterOrDigit)))
            return ApiResult<LiveLogFilter>.Fail("unsupported_search_token", "当前关键词检索支持 ASCII 字母数字词；中文与纯符号检索尚未开放。");
        if (filter.SourceId is not null && !allowedSourceIds.Contains(filter.SourceId, StringComparer.Ordinal))
            return ApiResult<LiveLogFilter>.Fail("source_forbidden", "该日志来源不在当前授权范围内。");
        return ApiResult<LiveLogFilter>.Ok(filter);
    }

    public static bool ValidScope(IReadOnlyCollection<string> allowedSourceIds) =>
        allowedSourceIds is not null && allowedSourceIds.Count <= MaximumSources
        && allowedSourceIds.All(source => source is { Length: > 0 and <= 128 });

    /// <summary>ClickHouse text 索引使用 splitByNonAlpha：按非 ASCII 字母数字字符切词，保留大小写。</summary>
    public static string[] Tokens(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords)) return [];
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;
        for (var index = 0; index <= keywords.Length; index++)
        {
            if (index < keywords.Length && char.IsAsciiLetterOrDigit(keywords[index]))
            {
                if (start < 0) start = index;
            }
            else if (start >= 0)
            {
                tokens.Add(keywords[start..index]);
                start = -1;
            }
        }
        return tokens.ToArray();
    }

    public static bool MatchesKeywords(string message, IReadOnlyList<string> tokens)
    {
        // 只在词边界匹配，逐词查找避免为每条日志重新分配完整 token 集合。
        foreach (var token in tokens)
        {
            var matched = false;
            var start = 0;
            while (start <= message.Length - token.Length)
            {
                var index = message.IndexOf(token, start, StringComparison.Ordinal);
                if (index < 0) break;
                var end = index + token.Length;
                if ((index == 0 || !char.IsAsciiLetterOrDigit(message[index - 1]))
                    && (end == message.Length || !char.IsAsciiLetterOrDigit(message[end])))
                { matched = true; break; }
                start = index + 1;
            }
            if (!matched) return false;
        }
        return true;
    }
}
