namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class BatchPerItemAdapterTests
{
    [Fact]
    public async Task GivenBatchAndAlwaysPassingInner_WhenInvoked_ThenAllItemsSurviveAndNextCalled()
    {
        // Arrange
        var inner = new AlwaysPassMiddleware();
        var adapter = new BatchPerItemAdapter<string>(_ => inner);
        var batch = CreateBatch(["a", "b", "c"]);
        var context = CreateBatchContext(batch);

        var outerNextInvoked = false;
        MessageBatch<string>? handlerBatch = null;
        Func<Task> outerNext = () =>
        {
            outerNextInvoked = true;
            handlerBatch = context.Message;
            return Task.CompletedTask;
        };

        // Act
        await adapter.InvokeAsync(context, outerNext);

        // Assert
        Assert.True(outerNextInvoked);
        Assert.Equal(3, handlerBatch!.Count);
        Assert.Equal(3, inner.InvocationCount);
    }

    [Fact]
    public async Task GivenBatchAndAlwaysShortCircuitingInner_WhenInvoked_ThenAllItemsDroppedAndNextNotCalled()
    {
        // Arrange
        var inner = new AlwaysShortCircuitMiddleware();
        var adapter = new BatchPerItemAdapter<string>(_ => inner);
        var batch = CreateBatch(["a", "b"]);
        var context = CreateBatchContext(batch);

        var outerNextInvoked = false;
        Func<Task> outerNext = () =>
        {
            outerNextInvoked = true;
            return Task.CompletedTask;
        };

        // Act
        await adapter.InvokeAsync(context, outerNext);

        // Assert
        Assert.False(outerNextInvoked);
        Assert.Equal(2, inner.InvocationCount);
    }

    [Fact]
    public async Task GivenBatchAndPredicateInner_WhenInvoked_ThenOnlyMatchingItemsSurvive()
    {
        // Arrange — keep items starting with "k"
        var inner = new PredicateMiddleware<string>(msg => msg.StartsWith('k'));
        var adapter = new BatchPerItemAdapter<string>(_ => inner);
        var batch = CreateBatch(["keep:1", "drop:1", "keep:2", "drop:2", "keep:3"]);
        var context = CreateBatchContext(batch);

        MessageBatch<string>? handlerBatch = null;
        Func<Task> outerNext = () =>
        {
            handlerBatch = context.Message;
            return Task.CompletedTask;
        };

        // Act
        await adapter.InvokeAsync(context, outerNext);

        // Assert
        Assert.NotNull(handlerBatch);
        Assert.Equal(3, handlerBatch.Count);
        Assert.All(handlerBatch, item => Assert.StartsWith("keep:", item.Message));
    }

    [Fact]
    public async Task GivenInnerThrows_WhenInvoked_ThenExceptionPropagatesAndOuterNextNotCalled()
    {
        // Arrange
        var inner = new ThrowingMiddleware(new TimeoutException("transient"));
        var adapter = new BatchPerItemAdapter<string>(_ => inner);
        var batch = CreateBatch(["a", "b"]);
        var context = CreateBatchContext(batch);

        var outerNextInvoked = false;
        Func<Task> outerNext = () =>
        {
            outerNextInvoked = true;
            return Task.CompletedTask;
        };

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() => adapter.InvokeAsync(context, outerNext));
        Assert.False(outerNextInvoked);
    }

    [Fact]
    public async Task GivenItemDroppedBeforeAnItemThatThrows_WhenInvoked_ThenTheBatchKeepsOnlyTheItemsNotDropped()
    {
        // Arrange: a retry runs the batch again, so an item already dropped (and dead-lettered) must not be
        // evaluated a second time
        var inner = new PredicateMiddleware<string>(msg => msg.StartsWith("throw:", StringComparison.Ordinal)
            ? throw new TimeoutException("transient")
            : msg.StartsWith("keep:", StringComparison.Ordinal));
        var adapter = new BatchPerItemAdapter<string>(_ => inner);
        var context = CreateBatchContext(CreateBatch(["keep:1", "drop:1", "throw:1", "drop:2", "keep:2"]));

        // Act
        var failure = await Record.ExceptionAsync(() =>
            adapter.InvokeAsync(context, () => Task.CompletedTask));

        // Assert
        Assert.IsType<TimeoutException>(failure);
        Assert.Equal(["keep:1", "throw:1", "drop:2", "keep:2"], context.Message.Select(item => item.Message));
    }

    [Fact]
    public async Task GivenInnerCalledForEachItem_WhenInvoked_ThenInnerSeesPerItemContext()
    {
        // Arrange — capture each per-item context the inner sees
        var inner = new CapturingPassMiddleware<string>();
        var adapter = new BatchPerItemAdapter<string>(_ => inner);
        var batch = CreateBatch(["x", "y", "z"]);
        var context = CreateBatchContext(batch);

        // Act
        await adapter.InvokeAsync(context, () => Task.CompletedTask);

        // Assert — each per-item context carries its own message
        Assert.Equal(["x", "y", "z"], inner.SeenMessages);
    }

    // ── Helpers ──

    private static MessageBatch<string> CreateBatch(IEnumerable<string> messages)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var items = messages.Select(m => new BatchItem<string>
        {
            Message = m,
            TransportContext = TestTransportContext.Create(services),
        }).ToList();
        return new MessageBatch<string>(items);
    }

    private static ConsumeContext<MessageBatch<string>> CreateBatchContext(MessageBatch<string> batch)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new ConsumeContext<MessageBatch<string>>
        {
            MessageId = "batch-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = services,
            Message = batch,
            TransportContext = TestTransportContext.Create(services),
        };
    }

    // ── Inner middleware fakes ──

    private sealed class AlwaysPassMiddleware : IMiddleware<ConsumeContext<string>>
    {
        public int InvocationCount { get; private set; }

        public Task InvokeAsync(ConsumeContext<string> context, Func<Task> next)
        {
            InvocationCount++;
            return next();
        }
    }

    private sealed class AlwaysShortCircuitMiddleware : IMiddleware<ConsumeContext<string>>
    {
        public int InvocationCount { get; private set; }

        public Task InvokeAsync(ConsumeContext<string> context, Func<Task> next)
        {
            InvocationCount++;
            // intentionally do not call next
            return Task.CompletedTask;
        }
    }

    private sealed class PredicateMiddleware<T>(Func<T, bool> predicate) : IMiddleware<ConsumeContext<T>>
    {
        public Task InvokeAsync(ConsumeContext<T> context, Func<Task> next)
        {
            if (predicate(context.Message))
                return next();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingMiddleware(Exception ex) : IMiddleware<ConsumeContext<string>>
    {
        public Task InvokeAsync(ConsumeContext<string> context, Func<Task> next)
        {
            throw ex;
        }
    }

    private sealed class CapturingPassMiddleware<T> : IMiddleware<ConsumeContext<T>>
    {
        public List<T> SeenMessages { get; } = [];

        public Task InvokeAsync(ConsumeContext<T> context, Func<Task> next)
        {
            SeenMessages.Add(context.Message);
            return next();
        }
    }
}
