namespace Emit.Kafka.Consumer.Tests;

using Xunit;
using static Emit.Kafka.Consumer.Tests.WorkerPoolTestKit;

public class WorkerPoolSupervisorTests
{
    [Fact]
    public async Task GivenHandlerOutlastingTheStopTimeout_WhenThePoolStops_ThenItIsCancelledAndTheMessagesAfterItAreNeverHandled()
    {
        // Arrange: the first message is handled until cancelled, the second waits behind it
        var consumer = new BlockingConsumer(observesCancellation: true);
        var unhandled = 0;
        var workerTasks = new List<Task>();
        var supervisor = CreateSupervisor(consumer, count => unhandled += count, workerTasks: workerTasks);
        await using var committer = CreateCommitter();
        supervisor.Start(new OffsetManager(committer), CancellationToken.None);
        var worker = supervisor.Workers![0];
        worker.Writer.TryWrite(CreateConsumeResult(offset: 0));
        worker.Writer.TryWrite(CreateConsumeResult(offset: 1));
        await consumer.Started;

        // Act
        await supervisor.StopAsync();
        await workerTasks[0].WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(consumer.Cancelled);
        Assert.Equal(1, consumer.Handlings);
        Assert.Equal(1, unhandled);
    }

    [Fact]
    public async Task GivenPartitionsAlreadyOwnedByAnotherConsumer_WhenThePoolStopsAtOnce_ThenTheHandlerIsCancelledWithoutWaitingForTheStopTimeout()
    {
        // Arrange: a stop timeout far longer than the test
        var consumer = new BlockingConsumer(observesCancellation: true);
        var supervisor = CreateSupervisor(consumer, _ => { }, TimeSpan.FromMinutes(5));
        await using var committer = CreateCommitter();
        supervisor.Start(new OffsetManager(committer), CancellationToken.None);
        supervisor.Workers![0].Writer.TryWrite(CreateConsumeResult(offset: 0));
        await consumer.Started;

        // Act
        await supervisor.StopAsync(cancelAtOnce: true).WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(consumer.Cancelled);
    }

    [Fact]
    public async Task GivenHandlerNotStoppingWhenCancelled_WhenThePoolStops_ThenItReturnsAndTheWorkerTakesNoFurtherMessage()
    {
        // Arrange: the handler ignores cancellation; a second message waits behind it
        var consumer = new BlockingConsumer(observesCancellation: false);
        var unhandled = 0;
        var workerTasks = new List<Task>();
        var supervisor = CreateSupervisor(consumer, count => unhandled += count, workerTasks: workerTasks);
        await using var committer = CreateCommitter();
        supervisor.Start(new OffsetManager(committer), CancellationToken.None);
        var worker = supervisor.Workers![0];
        worker.Writer.TryWrite(CreateConsumeResult(offset: 0));
        worker.Writer.TryWrite(CreateConsumeResult(offset: 1));
        await consumer.Started;

        // Act: the stop does not wait for the handler forever; the handler returns afterwards, ending its worker
        await supervisor.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
        consumer.Release();
        await workerTasks[0].WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(1, unhandled);
        Assert.Equal(1, consumer.Handlings);
    }

    [Fact]
    public async Task GivenHandlerWhoseCancellationCallbackBlocks_WhenThePoolStops_ThenItReturnsWithoutWaitingForTheCallback()
    {
        // Arrange: the handler registers a callback on its token that blocks until released
        using var callbackReleased = new ManualResetEventSlim();
        var consumer = new BlockingConsumer(observesCancellation: true, onCancelled: callbackReleased.Wait);
        var supervisor = CreateSupervisor(consumer);
        await using var committer = CreateCommitter();
        supervisor.Start(new OffsetManager(committer), CancellationToken.None);
        supervisor.Workers![0].Writer.TryWrite(CreateConsumeResult(offset: 0));
        await consumer.Started;

        // Act
        var stopped = await supervisor.StopAsync().WaitAsync(TimeSpan.FromSeconds(30)).ContinueWith(t => t.IsCompletedSuccessfully);
        callbackReleased.Set();

        // Assert
        Assert.True(stopped);
    }

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
