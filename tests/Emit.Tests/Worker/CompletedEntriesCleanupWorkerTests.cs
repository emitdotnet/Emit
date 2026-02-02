namespace Emit.Tests.Worker;

using Emit.Abstractions;
using Emit.Configuration;
using Emit.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public class CompletedEntriesCleanupWorkerTests
{
    private readonly Mock<IOutboxRepository> mockRepository;
    private readonly ILogger<CompletedEntriesCleanupWorker> logger;

    public CompletedEntriesCleanupWorkerTests()
    {
        mockRepository = new Mock<IOutboxRepository>();
        logger = NullLogger<CompletedEntriesCleanupWorker>.Instance;
    }

    private CompletedEntriesCleanupWorker CreateWorker(CleanupOptions options)
    {
        return new CompletedEntriesCleanupWorker(
            mockRepository.Object,
            Options.Create(options),
            logger);
    }

    [Fact]
    public void GivenNullRepository_WhenConstruct_ThenThrowsArgumentNullException()
    {
        // Arrange
        var options = Options.Create(new CleanupOptions());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CompletedEntriesCleanupWorker(
            null!,
            options,
            logger));
    }

    [Fact]
    public void GivenNullOptions_WhenConstruct_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CompletedEntriesCleanupWorker(
            mockRepository.Object,
            null!,
            logger));
    }

    [Fact]
    public void GivenNullLogger_WhenConstruct_ThenThrowsArgumentNullException()
    {
        // Arrange
        var options = Options.Create(new CleanupOptions());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CompletedEntriesCleanupWorker(
            mockRepository.Object,
            options,
            null!));
    }

    [Fact]
    public async Task GivenWorker_WhenStartedAndCancelled_ThenCallsDeleteCompletedEntriesAsync()
    {
        // Arrange
        var options = new CleanupOptions
        {
            RetentionPeriod = TimeSpan.FromDays(7),
            CleanupInterval = TimeSpan.FromHours(1),
            BatchSize = 100
        };

        mockRepository
            .Setup(r => r.DeleteCompletedEntriesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act - start worker and immediately cancel
        var task = worker.StartAsync(cts.Token);

        // Give it a moment to run the initial cleanup
        await Task.Delay(100);
        await cts.CancelAsync();

        await task;
        await worker.StopAsync(CancellationToken.None);

        // Assert - should have called DeleteCompletedEntriesAsync at least once (initial run)
        mockRepository.Verify(
            r => r.DeleteCompletedEntriesAsync(
                It.IsAny<DateTime>(),
                options.BatchSize,
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce());
    }

    [Fact]
    public async Task GivenEntriesExist_WhenCleanupRuns_ThenDeletesInBatches()
    {
        // Arrange
        var options = new CleanupOptions
        {
            RetentionPeriod = TimeSpan.FromDays(7),
            CleanupInterval = TimeSpan.FromHours(1),
            BatchSize = 100
        };

        var callCount = 0;
        mockRepository
            .Setup(r => r.DeleteCompletedEntriesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // First call: return batch size (indicating more to delete)
                // Second call: return less than batch size (indicating done)
                return callCount == 1 ? 100 : 50;
            });

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        var task = worker.StartAsync(cts.Token);
        await Task.Delay(200); // Give time for batch loop to complete
        await cts.CancelAsync();
        await task;
        await worker.StopAsync(CancellationToken.None);

        // Assert - should have called twice (first returns full batch, second returns partial)
        mockRepository.Verify(
            r => r.DeleteCompletedEntriesAsync(
                It.IsAny<DateTime>(),
                options.BatchSize,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GivenCorrectCutoffDate_WhenCleanupRuns_ThenUsesRetentionPeriod()
    {
        // Arrange
        var retentionPeriod = TimeSpan.FromDays(3);
        var options = new CleanupOptions
        {
            RetentionPeriod = retentionPeriod,
            CleanupInterval = TimeSpan.FromHours(1),
            BatchSize = 100
        };

        DateTime? capturedCutoffDate = null;
        mockRepository
            .Setup(r => r.DeleteCompletedEntriesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<DateTime, int, CancellationToken>((cutoff, _, _) => capturedCutoffDate = cutoff)
            .ReturnsAsync(0);

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        var beforeStart = DateTime.UtcNow;
        var task = worker.StartAsync(cts.Token);
        await Task.Delay(100);
        var afterStart = DateTime.UtcNow;
        await cts.CancelAsync();
        await task;
        await worker.StopAsync(CancellationToken.None);

        // Assert - cutoff should be approximately (now - retentionPeriod)
        Assert.NotNull(capturedCutoffDate);
        var expectedMinCutoff = beforeStart - retentionPeriod - TimeSpan.FromSeconds(1);
        var expectedMaxCutoff = afterStart - retentionPeriod + TimeSpan.FromSeconds(1);
        Assert.InRange(capturedCutoffDate.Value, expectedMinCutoff, expectedMaxCutoff);
    }

    [Fact]
    public async Task GivenRepositoryThrows_WhenCleanupRuns_ThenContinuesRunning()
    {
        // Arrange
        var options = new CleanupOptions
        {
            RetentionPeriod = TimeSpan.FromDays(7),
            CleanupInterval = TimeSpan.FromMilliseconds(50),
            BatchSize = 100
        };

        var callCount = 0;
        mockRepository
            .Setup(r => r.DeleteCompletedEntriesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new InvalidOperationException("Database error");
                }
                return 0;
            });

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        var task = worker.StartAsync(cts.Token);
        await Task.Delay(200); // Wait for retry
        await cts.CancelAsync();
        await task;
        await worker.StopAsync(CancellationToken.None);

        // Assert - should have continued running after exception
        Assert.True(callCount >= 2, $"Expected at least 2 calls but got {callCount}");
    }
}
