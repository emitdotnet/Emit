namespace Emit.UnitTests.Daemon;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Configuration;
using global::Emit.Daemon;
using global::Emit.Metrics;
using global::Emit.Models;
using global::Emit.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public class OutboxDaemonTests
{
    private readonly Mock<IOutboxRepository> mockRepository = new();
    private readonly Mock<IOutboxProvider> mockProvider = new();
    private readonly OutboxOptions options = new()
    {
        PollingInterval = TimeSpan.FromMilliseconds(50),
        BatchSize = 100
    };

    [Fact]
    public void GivenDaemonId_ThenReturnsCorrectId()
    {
        // Arrange
        var daemon = CreateDaemon();

        // Assert
        Assert.Equal("emit:outbox", daemon.DaemonId);
    }

    [Fact]
    public async Task GivenStarted_WhenAssignmentTokenCancelled_ThenStopsProcessing()
    {
        // Arrange
        mockRepository
            .Setup(r => r.GetBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var daemon = CreateDaemon();
        using var cts = new CancellationTokenSource();

        // Act
        await daemon.StartAsync(cts.Token);
        await cts.CancelAsync();
        await Task.Delay(200);

        // Assert — daemon should stop processing after the assignment token is cancelled
        var countBefore = mockRepository.Invocations.Count;
        await Task.Delay(150);
        var countAfter = mockRepository.Invocations.Count;

        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public async Task GivenStarted_WhenStopCalled_ThenStopsGracefully()
    {
        // Arrange
        mockRepository
            .Setup(r => r.GetBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var daemon = CreateDaemon();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await daemon.StartAsync(cts.Token);
        await Task.Delay(100);
        await daemon.StopAsync(CancellationToken.None);

        var countAfterStop = mockRepository.Invocations.Count;
        await Task.Delay(150);

        // Assert — no further calls after StopAsync
        Assert.Equal(countAfterStop, mockRepository.Invocations.Count);
    }

    [Fact]
    public async Task GivenEntriesAvailable_WhenStarted_ThenProcessesThem()
    {
        // Arrange
        var entry = CreateEntry("entry-1", "test-provider", "group-1", sequence: 1);

        mockProvider.Setup(p => p.SystemId).Returns("test-provider");
        mockProvider
            .Setup(p => p.ProcessAsync(entry, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockRepository
            .Setup(r => r.GetBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry]);

        mockRepository
            .Setup(r => r.DeleteAsync(entry.Id!, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var daemon = CreateDaemon([mockProvider.Object]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await daemon.StartAsync(cts.Token);
        await Task.Delay(300);
        await daemon.StopAsync(CancellationToken.None);

        // Assert
        mockProvider.Verify(p => p.ProcessAsync(entry, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mockRepository.Verify(r => r.DeleteAsync(entry.Id!, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task GivenProcessingFails_WhenEntry_ThenGroupProcessingStops()
    {
        // Arrange
        var entry1 = CreateEntry("entry-1", "test-provider", "group-1", sequence: 1);
        var entry2 = CreateEntry("entry-2", "test-provider", "group-1", sequence: 2);

        mockProvider.Setup(p => p.SystemId).Returns("test-provider");
        mockProvider
            .Setup(p => p.ProcessAsync(entry1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delivery failed"));

        mockRepository
            .Setup(r => r.GetBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry1, entry2]);

        var daemon = CreateDaemon([mockProvider.Object]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await daemon.StartAsync(cts.Token);
        await Task.Delay(300);
        await daemon.StopAsync(CancellationToken.None);

        // Assert — only the first entry is attempted; second is not processed after failure
        mockProvider.Verify(p => p.ProcessAsync(entry1, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mockProvider.Verify(p => p.ProcessAsync(entry2, It.IsAny<CancellationToken>()), Times.Never);
        mockRepository.Verify(r => r.DeleteAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GivenEntriesFromMultipleGroups_WhenDispatched_ThenBothGroupsProcessed()
    {
        // Arrange
        var entry1 = CreateEntry("entry-1", "test-provider", "group-1", sequence: 1);
        var entry2 = CreateEntry("entry-2", "test-provider", "group-2", sequence: 2);

        mockProvider.Setup(p => p.SystemId).Returns("test-provider");
        mockProvider
            .Setup(p => p.ProcessAsync(It.IsAny<OutboxEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockRepository
            .Setup(r => r.GetBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry1, entry2]);

        mockRepository
            .Setup(r => r.DeleteAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var daemon = CreateDaemon([mockProvider.Object]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await daemon.StartAsync(cts.Token);
        await Task.Delay(300);
        await daemon.StopAsync(CancellationToken.None);

        // Assert — both entries from different groups are processed
        mockProvider.Verify(p => p.ProcessAsync(entry1, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mockProvider.Verify(p => p.ProcessAsync(entry2, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private static OutboxEntry CreateEntry(
        string id,
        string systemId,
        string groupKey,
        long sequence) =>
        new()
        {
            Id = id,
            SystemId = systemId,
            Destination = "kafka://localhost:9092/test-topic",
            GroupKey = groupKey,
            Sequence = sequence,
            EnqueuedAt = DateTime.UtcNow,
            Body = [1, 2, 3],
            Properties = new Dictionary<string, string>()
        };

    private OutboxDaemon CreateDaemon(IEnumerable<IOutboxProvider>? providers = null)
    {
        var metrics = new OutboxMetrics(null, new EmitMetricsEnrichment());
        var observerInvoker = new OutboxObserverInvoker(
            [],
            metrics,
            NullLogger<OutboxObserverInvoker>.Instance);

        return new OutboxDaemon(
            mockRepository.Object,
            providers ?? [],
            observerInvoker,
            metrics,
            Options.Create(options),
            NullLogger<OutboxDaemon>.Instance);
    }
}
