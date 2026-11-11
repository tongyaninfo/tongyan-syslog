using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;
using Xunit;

namespace Syslog.Ingest.Tests;

/// <summary>仅使用测试输出目录中的合成文件；保留全部产物，不调用会回收文件的确认接口。</summary>
public sealed class SpoolTests
{
    [Fact]
    public async Task AppendAndReadPreserveRawBytesAndContext()
    {
        var directory = NewDirectory();
        var status = new IngestStatus();
        using var spool = Create(directory, status);
        await spool.OpenAsync(CancellationToken.None);
        var expected = Event(1, [0x3c, 0x31, 0x34, 0x3e, 0x00, 0xff, 0xfe, 0xe4, 0xb8, 0xad]);
        Assert.True(await spool.AppendAsync([expected], CancellationToken.None));
        var batch = await spool.ReadNextAsync(CancellationToken.None);
        Assert.NotNull(batch);
        Assert.Equal(expected.BatchId, batch.Id);
        Assert.False(batch.Sending);
        var actual = Assert.Single(batch.Events);
        Assert.Equal(expected.RawBytes, actual.RawBytes);
        Assert.Equal(expected.EventId, actual.EventId);
        Assert.Equal(expected.ReceivedAt, actual.ReceivedAt);
        Assert.Equal(expected.ExpiresAt, actual.ExpiresAt);
        Assert.Equal(expected.SourceId, actual.SourceId);
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(1, status.PendingBatches);
        Assert.True(status.SpoolBytes > expected.RawBytes.Length);
        Assert.Single(Directory.EnumerateFiles(directory, "*.batch"));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task NewSpoolInstanceFindsPreviouslyPersistedBatch()
    {
        var directory = NewDirectory();
        var expected = Event(2, "retained synthetic evidence"u8.ToArray());
        using (var first = Create(directory))
        {
            await first.OpenAsync(CancellationToken.None);
            Assert.True(await first.AppendAsync([expected], CancellationToken.None));
        }
        var restoredStatus = new IngestStatus();
        using var restarted = Create(directory, restoredStatus);
        await restarted.OpenAsync(CancellationToken.None);
        var restored = await restarted.ReadNextAsync(CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal(expected.BatchId, restored.Id);
        Assert.Equal(expected.RawBytes, Assert.Single(restored.Events).RawBytes);
        Assert.Equal(1, restoredStatus.PendingBatches);
    }

    [Fact]
    public async Task SendingMarkerAndStableBatchIdentitySurviveReopen()
    {
        var directory = NewDirectory();
        var expected = Event(3, "uncertain submission"u8.ToArray());
        using (var first = Create(directory))
        {
            await first.OpenAsync(CancellationToken.None);
            Assert.True(await first.AppendAsync([expected], CancellationToken.None));
            await first.MarkSendingAsync(expected.BatchId, CancellationToken.None);
        }
        using var restarted = Create(directory);
        await restarted.OpenAsync(CancellationToken.None);
        var pending = await restarted.ReadNextAsync(CancellationToken.None);
        Assert.NotNull(pending);
        Assert.True(pending.Sending);
        Assert.Equal(expected.BatchId, pending.Id);
        Assert.Equal(expected.EventId, Assert.Single(pending.Events).EventId);
        Assert.True(File.Exists(Path.Combine(directory, expected.BatchId + ".sending")));
        Assert.True(File.Exists(Path.Combine(directory, expected.BatchId + ".batch")));
    }

    [Fact]
    public async Task ChecksumTamperingIsRejectedWithoutRemovingEvidence()
    {
        var directory = NewDirectory();
        using var spool = Create(directory);
        await spool.OpenAsync(CancellationToken.None);
        var expected = Event(4, "checksum sample"u8.ToArray());
        Assert.True(await spool.AppendAsync([expected], CancellationToken.None));
        var path = Path.Combine(directory, expected.BatchId + ".batch");
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        envelope["Hash"] = new string('0', 64);
        await File.WriteAllTextAsync(path, envelope.ToJsonString());
        var error = await Assert.ThrowsAsync<IOException>(() => spool.ReadNextAsync(CancellationToken.None));
        Assert.Equal("spool_corrupt", error.Message);
        Assert.True(File.Exists(path));
        Assert.Equal(new string('0', 64), JsonNode.Parse(await File.ReadAllTextAsync(path))!["Hash"]!.GetValue<string>());
    }

    [Fact]
    public async Task OrphanTemporaryFileBlocksEveryOpenAttempt()
    {
        var directory = NewDirectory();
        var orphan = Path.Combine(directory, "synthetic-orphan.tmp");
        await File.WriteAllBytesAsync(orphan, "partial synthetic envelope"u8.ToArray());
        using (var spool = Create(directory))
        {
            var first = await Assert.ThrowsAsync<SpoolRecoveryException>(() => spool.OpenAsync(CancellationToken.None));
            var second = await Assert.ThrowsAsync<SpoolRecoveryException>(() => spool.OpenAsync(CancellationToken.None));
            Assert.Equal("spool_recovery_required", first.Message);
            Assert.Equal("spool_recovery_required", second.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => spool.ReadNextAsync(CancellationToken.None));
        }
        using var restarted = Create(directory);
        var third = await Assert.ThrowsAsync<SpoolRecoveryException>(() => restarted.OpenAsync(CancellationToken.None));
        Assert.Equal("spool_recovery_required", third.Message);
        Assert.Equal("partial synthetic envelope"u8.ToArray(), await File.ReadAllBytesAsync(orphan));
    }

    [Fact]
    public async Task ExclusiveOwnershipRejectsSecondOwnerUntilFirstIsDisposed()
    {
        var directory = NewDirectory();
        using var contender = Create(directory);
        using (var owner = Create(directory))
        {
            await owner.OpenAsync(CancellationToken.None);
            await Assert.ThrowsAnyAsync<IOException>(() => contender.OpenAsync(CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => contender.ReadNextAsync(CancellationToken.None));
        }
        await contender.OpenAsync(CancellationToken.None);
        Assert.Null(await contender.ReadNextAsync(CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(directory, "owner.lock")));
    }

    [Fact]
    public async Task CapacityRefusalPreservesAllExistingFiles()
    {
        var directory = NewDirectory();
        var status = new IngestStatus();
        using var spool = Create(directory, status, 1024 * 1024);
        await spool.OpenAsync(CancellationToken.None);
        var expected = Event(5, new byte[200_000]);
        Assert.True(await spool.AppendAsync([expected], CancellationToken.None));
        var before = Directory.EnumerateFiles(directory).ToDictionary(path => path, Fingerprint, StringComparer.Ordinal);
        var rejected = Event(6, new byte[500_000]);
        Assert.False(await spool.AppendAsync([rejected], CancellationToken.None));
        var after = Directory.EnumerateFiles(directory).ToDictionary(path => path, Fingerprint, StringComparer.Ordinal);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var entry in before) Assert.Equal(entry.Value, after[entry.Key]);
        Assert.Equal(expected.BatchId, (await spool.ReadNextAsync(CancellationToken.None))!.Id);
        Assert.Equal(1, status.PendingBatches);
        Assert.False(File.Exists(Path.Combine(directory, rejected.BatchId + ".batch")));
        Assert.False(File.Exists(Path.Combine(directory, rejected.BatchId + ".tmp")));
    }

    [Fact]
    public async Task AcknowledgedCleanupInterruptionRequiresRecoveryWithoutResending()
    {
        var directory = NewDirectory();
        var expected = Event(7, "already committed"u8.ToArray());
        using (var first = Create(directory))
        {
            await first.OpenAsync(default);
            Assert.True(await first.AppendAsync([expected], default));
        }
        var marker = Path.Combine(directory, expected.BatchId + ".acked");
        await File.WriteAllTextAsync(marker, "acked-v1");
        using var restored = Create(directory);
        var error = await Assert.ThrowsAsync<SpoolRecoveryException>(() => restored.OpenAsync(default));
        Assert.Equal("spool_ack_cleanup_required", error.Code);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restored.ReadNextAsync(default));
        Assert.True(File.Exists(marker));
        Assert.True(File.Exists(Path.Combine(directory, expected.BatchId + ".batch")));
    }

    [Fact]
    public async Task SingleBatchLargerThanTotalCapacityIsPermanentConfigurationError()
    {
        var directory = NewDirectory();
        using var spool = Create(directory, capacity: 1024 * 1024);
        await spool.OpenAsync(default);
        var first = Event(8, new byte[65_000]) with { Message = new string('\0', 65_000) };
        var second = first with { EventId = 9 };
        var error = await Assert.ThrowsAsync<SpoolRecoveryException>(() => spool.AppendAsync([first, second], default));
        Assert.Equal("spool_batch_exceeds_capacity", error.Code);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.batch"));
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "spool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static FileEventSpool Create(string directory, IngestStatus? status = null, long capacity = 4 * 1024 * 1024) =>
        new(Microsoft.Extensions.Options.Options.Create(new IngestOptions { SpoolDirectory = directory, SpoolBytes = capacity }), status ?? new());

    private static LogEvent Event(long id, byte[] raw) => new()
    {
        EventId = id,
        BatchId = $"{id:D20}-{id:x32}",
        SourceId = "synthetic-source",
        PeerAddress = "192.0.2.10",
        Transport = "tcp",
        ReceivedAt = DateTimeOffset.Parse("2026-11-09T00:00:00Z"),
        ExpiresAt = DateTimeOffset.Parse("2027-11-09T00:00:00Z"),
        RawBytes = raw,
        Message = "Synthetic evidence 中文",
        ParseStatus = "parsed"
    };

    private static string Fingerprint(string path)
    {
        // owner.lock 被独占打开，空锁文件只比较长度；数据文件逐字节哈希比对。
        return Path.GetFileName(path) == "owner.lock" ? new FileInfo(path).Length.ToString() : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
