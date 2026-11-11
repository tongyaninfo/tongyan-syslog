using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Engine.Service.impl;

/// <summary>仅解释传入字节与来源快照；不读取当前时间，确保历史回放结果一致。</summary>
public sealed partial class SyslogParser : ISyslogParser
{
    public const int MaximumMessageLength = 65_536;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public LogEvent Parse(ReadOnlyMemory<byte> message, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = new LogEvent
        {
            SourceId = context.SourceId,
            PeerAddress = context.PeerAddress,
            Transport = context.Transport,
            ReceivedAt = context.ReceivedAt.ToUniversalTime(),
            ExpiresAt = context.ExpiresAt.ToUniversalTime(),
            RawBytes = message.ToArray()
        };
        if (message.IsEmpty) return Fail(result, "empty_message");
        if (message.Length > MaximumMessageLength) return Fail(result, "message_too_large");

        string text;
        try { text = StrictUtf8.GetString(message.Span); }
        catch (DecoderFallbackException)
        {
            // 非 UTF-8 原文仍是证据；替换字符仅用于显示，不覆盖保存的字节。
            return Fail(result with { Message = Encoding.UTF8.GetString(message.Span) }, "unsupported_message_encoding");
        }
        result = result with { Message = text };
        if (text[0] != '<') return result with { FailureCode = "unrecognized_format" };
        var priEnd = text.IndexOf('>');
        if (priEnd is < 2 or > 4 || !text.AsSpan(1, priEnd - 1).ContainsOnlyDigits()
            || !int.TryParse(text.AsSpan(1, priEnd - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var pri)
            || pri > 191 || (priEnd > 2 && text[1] == '0')) return Fail(result, "invalid_pri");
        result = result with { Facility = pri / 8, Severity = pri % 8 };
        var body = text[(priEnd + 1)..];
        if (body.Length == 0) return Fail(result, "missing_header");
        if (char.IsAsciiDigit(body[0])) return Parse5424(body, result);
        if (body.Length >= 3 && Array.IndexOf(Months, body[..3]) >= 0) return Parse3164(body, context, result);
        return result with { FailureCode = "unrecognized_format" };
    }

    private static LogEvent Parse5424(string body, LogEvent result)
    {
        var position = 0;
        var header = new string[6];
        for (var i = 0; i < header.Length; i++)
        {
            var end = body.IndexOf(' ', position);
            if (end <= position) return Fail(result, "invalid_rfc5424_header");
            header[i] = body[position..end];
            position = end + 1;
        }
        if (header[0] != "1") return Fail(result, "unsupported_version");
        if (!ValidHeader(header[2], 255) || !ValidHeader(header[3], 48)
            || !ValidHeader(header[4], 128) || !ValidHeader(header[5], 32)) return Fail(result, "invalid_header_field");
        // 非法时间戳也必须遵守结构化字段预算；完整畸形头仍在 RawBytes/Message，不能毒化整个写入批次。
        if (header[1].Length > 64) return Fail(result, "invalid_timestamp");

        result = result with
        {
            OriginalTimestamp = header[1], Hostname = Nil(header[2]), AppName = Nil(header[3]),
            ProcId = Nil(header[4]), MsgId = Nil(header[5])
        };
        if (header[1] != "-")
        {
            if (!TimestampPattern().IsMatch(header[1]) || !DateTimeOffset.TryParse(header[1], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var timestamp)) return Fail(result, "invalid_timestamp");
            result = result with { EventTime = timestamp.ToUniversalTime(), TimeConfidence = "explicit" };
        }
        if (!TryStructuredData(body, ref position, out var structuredData, out var failure)) return Fail(result, failure);
        result = result with { StructuredData = structuredData };
        if (position < body.Length && body[position] != ' ') return Fail(result, "invalid_message_separator");
        var content = position < body.Length ? body[(position + 1)..] : "";
        // UTF-8 BOM 属于编码标识，显示正文移除，RawBytes 保留完整原文。
        if (content.StartsWith('\uFEFF')) content = content[1..];
        return result with { Message = content, ParseStatus = "parsed", FailureCode = "" };
    }

    private static bool TryStructuredData(string body, ref int position, out string structuredData, out string failure)
    {
        structuredData = "";
        failure = "invalid_structured_data";
        if (position >= body.Length) return false;
        if (body[position] == '-') { position++; return true; }
        if (body[position] != '[') return false;
        var start = position;
        var elementIds = new HashSet<string>(StringComparer.Ordinal);
        var parameters = 0;
        while (position < body.Length && body[position] == '[')
        {
            position++;
            var nameStart = position;
            while (position < body.Length && body[position] is not ' ' and not ']') position++;
            var name = body[nameStart..position];
            if (!ValidSdName(name) || !elementIds.Add(name)) return false;
            if (elementIds.Count > 32) { failure = "structured_data_limit"; return false; }
            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            while (position < body.Length && body[position] == ' ')
            {
                position++;
                nameStart = position;
                while (position < body.Length && body[position] != '=')
                {
                    if (body[position] is ' ' or ']' or '"') return false;
                    position++;
                }
                name = body[nameStart..position];
                if (!ValidSdName(name) || !parameterNames.Add(name) || position + 1 >= body.Length
                    || body[position + 1] != '"') return false;
                if (++parameters > 128) { failure = "structured_data_limit"; return false; }
                position += 2;
                var closed = false;
                while (position < body.Length)
                {
                    var c = body[position++];
                    if (c == '"') { closed = true; break; }
                    if (c == ']') return false;
                    // RFC 5424 的未知反斜线转义按字面保留；三个规定转义消耗下一个字符。
                    if (c == '\\' && position < body.Length && body[position] is '"' or ']' or '\\') position++;
                }
                if (!closed) return false;
            }
            if (position >= body.Length || body[position] != ']') return false;
            position++;
        }
        structuredData = body[start..position];
        return true;
    }

    private static LogEvent Parse3164(string body, ParseContext context, LogEvent result)
    {
        if (body.Length < 17 || body[15] != ' ') return Fail(result, "invalid_rfc3164_header");
        result = result with { OriginalTimestamp = body[..15] };
        var timeMatch = LegacyTimestampPattern().Match(body[..15]);
        if (!timeMatch.Success) return Fail(result, "invalid_timestamp");
        var month = Array.IndexOf(Months, timeMatch.Groups[1].Value) + 1;
        var day = int.Parse(timeMatch.Groups[2].Value.Trim(), CultureInfo.InvariantCulture);
        var hour = int.Parse(timeMatch.Groups[3].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(timeMatch.Groups[4].Value, CultureInfo.InvariantCulture);
        var second = int.Parse(timeMatch.Groups[5].Value, CultureInfo.InvariantCulture);
        var localReceived = TimeZoneInfo.ConvertTime(context.ReceivedAt, context.SourceTimeZone);
        var dates = new List<DateTime>();
        // 缺失年份仅以固定接收时刻前后一年推断；跨年回放不依赖执行当天。
        for (var year = Math.Max(1, localReceived.Year - 1); year <= Math.Min(9999, localReceived.Year + 1); year++)
        {
            if (day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59) continue;
            dates.Add(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified));
        }
        if (dates.Count == 0) return Fail(result, "invalid_timestamp");
        var local = dates.OrderBy(d => Math.Abs((d - localReceived.DateTime).Ticks)).ThenBy(d => d).First();
        // 夏令时缺口不能靠跳到相邻年份变成有效时间；歧义时固定选择最接近接收时间的时刻。
        if (context.SourceTimeZone.IsInvalidTime(local)) return Fail(result, "invalid_local_time");
        var ambiguous = context.SourceTimeZone.IsAmbiguousTime(local);
        var offsets = ambiguous ? context.SourceTimeZone.GetAmbiguousTimeOffsets(local) : [context.SourceTimeZone.GetUtcOffset(local)];
        var chosen = offsets.Select(offset => new DateTimeOffset(local, offset).ToUniversalTime())
            .OrderBy(t => Math.Abs((t - context.ReceivedAt).Ticks)).ThenBy(t => t).First();
        var confidence = ambiguous ? "inferred_ambiguous" : "inferred_year_timezone";
        if (Math.Abs((chosen - context.ReceivedAt).TotalDays) > 183) confidence = "inferred_distant";
        var hostnameEnd = body.IndexOf(' ', 16);
        if (hostnameEnd < 0) hostnameEnd = body.Length;
        var hostname = body[16..hostnameEnd];
        if (!ValidHeader(hostname, 255)) return Fail(result, "invalid_hostname");
        var content = hostnameEnd < body.Length ? body[(hostnameEnd + 1)..] : "";
        var appName = "";
        var processId = "";
        var tag = LegacyTagPattern().Match(content);
        if (tag.Success)
        {
            appName = tag.Groups[1].Value;
            processId = tag.Groups[2].Value;
            content = content[tag.Length..];
        }
        return result with
        {
            EventTime = chosen, TimeConfidence = confidence, Hostname = hostname,
            AppName = appName, ProcId = processId, Message = content, ParseStatus = "parsed", FailureCode = ""
        };
    }

    private static LogEvent Fail(LogEvent value, string code) => value with { ParseStatus = "failed", FailureCode = code };
    private static string Nil(string value) => value == "-" ? "" : value;
    private static bool ValidHeader(string value, int maximum) => value.Length is > 0 && value.Length <= maximum && value.All(c => c is >= '!' and <= '~');
    private static bool ValidSdName(string value) => ValidHeader(value, 32) && value.All(c => c is not '=' and not ']' and not '"');

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,6})?(?:Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();
    [GeneratedRegex(@"^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) ([ 0-9][0-9]) ([0-9]{2}):([0-9]{2}):([0-9]{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyTimestampPattern();
    [GeneratedRegex(@"^([A-Za-z0-9_./-]{1,48})(?:\[([0-9]{1,20})\])?: ?", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyTagPattern();
}

internal static class AsciiDigits
{
    internal static bool ContainsOnlyDigits(this ReadOnlySpan<char> value)
    {
        foreach (var c in value) if (!char.IsAsciiDigit(c)) return false;
        return !value.IsEmpty;
    }
}
