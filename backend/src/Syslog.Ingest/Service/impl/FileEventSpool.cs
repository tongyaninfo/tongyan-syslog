using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service;
using Syslog.Shared.Models;

namespace Syslog.Ingest.Service.impl;

/// <summary>本机有界暂存：写入临时文件、Flush(true)、原子改名；不承诺设备掉电时的目录项持久性。</summary>
public sealed class FileEventSpool(IOptions<IngestOptions> options, IngestStatus status) : IEventSpool, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string directory = Path.GetFullPath(options.Value.SpoolDirectory);
    private FileStream? ownership;

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (ownership is not null) return;
            if (options.Value.SpoolBytes < 1024 * 1024) throw new ArgumentException("暂存容量配置无效");
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            // 独占所有权避免两个宿主同时发送同一批次。锁文件不含用户数据。
            ownership = new FileStream(Path.Combine(directory, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            RefreshCounters();
            // 未完成的临时文件不能悄悄忽略，否则会把崩溃恢复说成无丢失。
            if (Directory.EnumerateFiles(directory, "*.tmp").Any()) throw new SpoolRecoveryException("spool_recovery_required");
            // 确认后回收中断不会重发；首版要求显式恢复，避免跳过残留却把占用报告为健康。
            if (Directory.EnumerateFiles(directory, "*.acked").Any()) throw new SpoolRecoveryException("spool_ack_cleanup_required");
        }
        catch { ownership?.Dispose(); ownership = null; throw; }
        finally { gate.Release(); }
    }

    public async Task<bool> AppendAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0 || events.Count > 10000) throw new ArgumentException("批次无效");
        var id = events[0].BatchId;
        ValidateId(id);
        if (events.Any(e => e.BatchId != id || e.EventId <= 0)) throw new ArgumentException("批次标识不一致");
        var payload = JsonSerializer.SerializeToUtf8Bytes(events);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, id, Convert.ToHexString(SHA256.HashData(payload)), payload));
        if (bytes.Length > 16 * 1024 * 1024) throw new ArgumentException("暂存批次过大");
        if (bytes.Length + 4096 > options.Value.SpoolBytes) throw new SpoolRecoveryException("spool_batch_exceeds_capacity");
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            RefreshCounters();
            // 为发送和确认标记预留空间；磁盘还有其它使用者，I/O错误仍须显式处理。
            if (status.SpoolBytes + bytes.Length + 4096 > options.Value.SpoolBytes) return false;
            await WriteNewAsync(FilePath(id, ".tmp"), bytes, cancellationToken);
            File.Move(FilePath(id, ".tmp"), FilePath(id, ".batch"));
            RefreshCounters();
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task<PendingBatch?> ReadNextAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            // 名称以首事件ID补零排序，避免随机文件名打乱发送顺序。
            var file = Directory.EnumerateFiles(directory, "*.batch").Order(StringComparer.Ordinal)
                .FirstOrDefault(f => !File.Exists(Path.ChangeExtension(f, ".acked")));
            if (file is null) return null;
            if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new IOException("spool_corrupt");
            var data = JsonSerializer.Deserialize<Envelope>(await File.ReadAllBytesAsync(file, cancellationToken))
                ?? throw new IOException("spool_corrupt");
            ValidateId(data.Id);
            if (data.Version != 1 || Path.GetFileNameWithoutExtension(file) != data.Id
                || data.Hash != Convert.ToHexString(SHA256.HashData(data.Payload))) throw new IOException("spool_corrupt");
            var events = JsonSerializer.Deserialize<LogEvent[]>(data.Payload) ?? throw new IOException("spool_corrupt");
            if (events.Length is 0 or > 10000 || events.Any(e => e.BatchId != data.Id || e.EventId <= 0)) throw new IOException("spool_corrupt");
            return new PendingBatch(data.Id, events, File.Exists(FilePath(data.Id, ".sending")));
        }
        finally { gate.Release(); }
    }

    public async Task MarkSendingAsync(string batchId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            if (!File.Exists(FilePath(batchId, ".batch"))) throw new IOException("spool_batch_missing");
            await WriteNewAsync(FilePath(batchId, ".sending"), "sending-v1"u8.ToArray(), cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task AcknowledgeAsync(string batchId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            await WriteNewAsync(FilePath(batchId, ".acked"), "acked-v1"u8.ToArray(), cancellationToken);
            // 仅回收已确认的本应用暂存。先落确认标记，崩溃后不会重新发送已确认批次。
            File.Delete(FilePath(batchId, ".batch"));
            File.Delete(FilePath(batchId, ".sending"));
            File.Delete(FilePath(batchId, ".acked"));
            RefreshCounters();
        }
        finally { gate.Release(); }
    }

    private static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.WriteThrough };
        if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(path, fileOptions);
        await file.WriteAsync(bytes, cancellationToken);
        await file.FlushAsync(cancellationToken);
        file.Flush(flushToDisk: true);
    }

    private void RefreshCounters()
    {
        long bytes = 0, batches = 0;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            bytes += new FileInfo(file).Length;
            if (file.EndsWith(".batch", StringComparison.Ordinal) && !File.Exists(Path.ChangeExtension(file, ".acked"))) batches++;
        }
        Interlocked.Exchange(ref status.SpoolBytes, bytes);
        Interlocked.Exchange(ref status.PendingBatches, batches);
    }
    private string FilePath(string id, string suffix) { ValidateId(id); return Path.Combine(directory, id + suffix); }
    private static void ValidateId(string id)
    {
        if (!Regex.IsMatch(id, "\\A[0-9]{20}-[a-f0-9]{32}\\z", RegexOptions.CultureInvariant)) throw new ArgumentException("暂存批次标识无效");
    }
    private void EnsureOpen() { if (ownership is null) throw new InvalidOperationException("暂存尚未打开"); }
    public void Dispose() { ownership?.Dispose(); gate.Dispose(); }
    private sealed record Envelope(int Version, string Id, string Hash, byte[] Payload);
}

public sealed class SpoolRecoveryException(string code) : IOException(code)
{
    public string Code { get; } = code;
}
