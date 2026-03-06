namespace Emit.Kafka.Consumer.Tests;

using Xunit;

public class WorkerPoolSupervisorTests
{
    [Fact]
    public async Task GivenRunningWorkers_WhenExecutionTokenCancelledBeforeWorkerCompletes_ThenReturnsNull()
    {
        // Arrange
        var tcs = new TaskCompletionSource();
        var executionCts = new CancellationTokenSource();
        var stopCts = CancellationTokenSource.CreateLinkedTokenSource(executionCts.Token);

        // Act — cancel first (simulates Ctrl+C), then worker exits due to cancellation
        executionCts.Cancel();
        tcs.SetResult();

        var result = await WorkerPoolSupervisor<string, string>.WaitForWorkerFaultAsync(
            [tcs.Task], executionCts.Token, stopCts.Token);

        // Assert — no fault, this was an intentional shutdown
        Assert.Null(result);
    }

    [Fact]
    public async Task GivenRunningWorkers_WhenWorkerCompletesAndExecutionTokenAlreadyCancelled_ThenReturnsNull()
    {
        // Arrange
        var tcs = new TaskCompletionSource();
        var executionCts = new CancellationTokenSource();
        var stopCts = CancellationTokenSource.CreateLinkedTokenSource(executionCts.Token);

        // Act — worker completes first, then cancellation fires (both happen "simultaneously" in real scenario)
        tcs.SetResult();
        executionCts.Cancel();

        var result = await WorkerPoolSupervisor<string, string>.WaitForWorkerFaultAsync(
            [tcs.Task], executionCts.Token, stopCts.Token);

        // Assert — no fault, execution token was cancelled
        Assert.Null(result);
    }

    [Fact]
    public async Task GivenRunningWorkers_WhenStopTokenCancelledByRebalance_ThenReturnsNull()
    {
        // Arrange — stop token is independent of execution token (simulates rebalance cancel)
        var tcs = new TaskCompletionSource();
        var executionCts = new CancellationTokenSource();
        var stopCts = new CancellationTokenSource();

        // Act — cancel stop token only (rebalance), execution token stays active
        stopCts.Cancel();

        var result = await WorkerPoolSupervisor<string, string>.WaitForWorkerFaultAsync(
            [tcs.Task], executionCts.Token, stopCts.Token);

        // Assert — no fault, monitor was intentionally stopped
        Assert.Null(result);
    }

    [Fact]
    public async Task GivenRunningWorkers_WhenWorkerFaults_ThenReturnsFaultedTask()
    {
        // Arrange
        var tcs = new TaskCompletionSource();
        var executionCts = new CancellationTokenSource();
        var stopCts = CancellationTokenSource.CreateLinkedTokenSource(executionCts.Token);

        // Act — worker throws exception (no cancellation)
        tcs.SetException(new InvalidOperationException("boom"));

        var result = await WorkerPoolSupervisor<string, string>.WaitForWorkerFaultAsync(
            [tcs.Task], executionCts.Token, stopCts.Token);

        // Assert — fault detected
        Assert.NotNull(result);
        Assert.True(result.Value.CompletedTask.IsFaulted);
        Assert.Equal(0, result.Value.WorkerIndex);
    }

    [Fact]
    public async Task GivenRunningWorkers_WhenWorkerCompletesUnexpectedly_ThenReturnsCompletedTask()
    {
        // Arrange
        var tcs = new TaskCompletionSource();
        var executionCts = new CancellationTokenSource();
        var stopCts = CancellationTokenSource.CreateLinkedTokenSource(executionCts.Token);

        // Act — worker exits normally without cancellation (unexpected)
        tcs.SetResult();

        var result = await WorkerPoolSupervisor<string, string>.WaitForWorkerFaultAsync(
            [tcs.Task], executionCts.Token, stopCts.Token);

        // Assert — unexpected completion detected
        Assert.NotNull(result);
        Assert.False(result.Value.CompletedTask.IsFaulted);
        Assert.Equal(0, result.Value.WorkerIndex);
    }

    [Fact]
    public async Task GivenMultipleWorkers_WhenSecondWorkerFaults_ThenReturnsCorrectIndex()
    {
        // Arrange
        var tcs1 = new TaskCompletionSource();
        var tcs2 = new TaskCompletionSource();
        var executionCts = new CancellationTokenSource();
        var stopCts = CancellationTokenSource.CreateLinkedTokenSource(executionCts.Token);

        // Act — second worker faults
        tcs2.SetException(new InvalidOperationException("worker 2 failed"));

        var result = await WorkerPoolSupervisor<string, string>.WaitForWorkerFaultAsync(
            [tcs1.Task, tcs2.Task], executionCts.Token, stopCts.Token);

        // Assert — correct worker index reported
        Assert.NotNull(result);
        Assert.Equal(1, result.Value.WorkerIndex);
    }
}
