namespace Emit.Mediator.Tests;

using System.Runtime.CompilerServices;
using global::Emit.Abstractions.Pipeline;
using global::Emit.DependencyInjection;
using global::Emit.Mediator;
using global::Emit.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Verifies that a stream handler runs inside the middleware pipeline for the whole life of
/// the stream, rather than the pipeline completing as soon as the sequence is handed back.
/// </summary>
public sealed class MediatorStreamTests
{
    // ── Requests and handlers ──

    private sealed record CountRequest(int Count) : IStreamRequest<int>;

    private sealed record FailingRequest(int BeforeFailure) : IStreamRequest<int>;

    private sealed class CountHandler : IStreamRequestHandler<CountRequest, int>
    {
        public static readonly List<string> Trace = [];

        public async IAsyncEnumerable<int> HandleAsync(
            CountRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                for (var i = 0; i < request.Count; i++)
                {
                    Trace.Add($"yield:{i}");
                    yield return i;
                    await Task.Yield();
                }
            }
            finally
            {
                Trace.Add("handler:finally");
            }
        }
    }

    private sealed class FailingHandler : IStreamRequestHandler<FailingRequest, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            FailingRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < request.BeforeFailure; i++)
            {
                yield return i;
                await Task.Yield();
            }

            throw new InvalidOperationException("Simulated handler failure");
        }
    }

    // ── Middleware that records when it enters and leaves ──

    private sealed class TracingMiddleware<T> : IMiddleware<MediatorContext<T>>
    {
        public static List<string> Trace { get; set; } = [];

        public async Task InvokeAsync(MediatorContext<T> context, IMiddlewarePipeline<MediatorContext<T>> next)
        {
            Trace.Add("middleware:enter");
            try
            {
                await next.InvokeAsync(context);
            }
            finally
            {
                Trace.Add("middleware:exit");
            }
        }
    }

    // ── Helper ──

    private static IMediator CreateMediator(
        Action<MediatorBuilder> configure,
        bool trace = false,
        bool failAfterHandler = false)
    {
        var services = new ServiceCollection();
        services.AddEmit(emit =>
        {
            if (trace)
            {
                emit.InboundPipeline.Use(typeof(TracingMiddleware<>));
            }

            if (failAfterHandler)
            {
                emit.InboundPipeline.Use(typeof(FailAfterHandlerMiddleware<>));
            }

            emit.AddMediator(configure);
        });

        var scope = services.BuildServiceProvider().CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMediator>();
    }

    // ── Tests ──

    [Fact]
    public async Task GivenStreamHandler_WhenEnumerated_ThenAllItemsAreProduced()
    {
        // Arrange
        CountHandler.Trace.Clear();
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());

        // Act
        var received = new List<int>();
        await foreach (var item in mediator.CreateStreamAsync(new CountRequest(3)))
        {
            received.Add(item);
        }

        // Assert
        Assert.Equal([0, 1, 2], received);
    }

    [Fact]
    public async Task GivenStreamHandler_WhenNotEnumerated_ThenHandlerNeverRuns()
    {
        // Arrange
        CountHandler.Trace.Clear();
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());

        // Act: building the stream must not start any work.
        _ = mediator.CreateStreamAsync(new CountRequest(3));
        await Task.Delay(50);

        // Assert
        Assert.Empty(CountHandler.Trace);
    }

    [Fact]
    public async Task GivenStreamHandler_WhenEnumerated_ThenMiddlewareWrapsTheWholeStream()
    {
        // Arrange
        TracingMiddleware<CountRequest>.Trace = [];
        var trace = TracingMiddleware<CountRequest>.Trace;
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>(), trace: true);

        // Act
        await foreach (var item in mediator.CreateStreamAsync(new CountRequest(2)))
        {
            trace.Add($"consumed:{item}");
        }

        // Assert: the middleware must still be on the stack while items are produced. If the
        // pipeline completed when the sequence was handed back, the exit would come first.
        var enter = trace.IndexOf("middleware:enter");
        var exit = trace.IndexOf("middleware:exit");
        var lastConsumed = trace.LastIndexOf("consumed:1");

        Assert.True(enter >= 0 && exit >= 0 && lastConsumed >= 0);
        Assert.True(enter < lastConsumed, "middleware entered after items were consumed");
        Assert.True(lastConsumed < exit, "middleware exited before the stream finished");
    }

    [Fact]
    public async Task GivenStreamHandler_WhenConsumerStopsEarly_ThenHandlerStopsProducing()
    {
        // Arrange
        CountHandler.Trace.Clear();
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());

        // Act: take two items from a longer stream and stop.
        var received = new List<int>();
        await foreach (var item in mediator.CreateStreamAsync(new CountRequest(1000)))
        {
            received.Add(item);
            if (received.Count == 2)
            {
                break;
            }
        }

        // Assert: the handler unwound rather than running to completion.
        Assert.Equal([0, 1], received);
        Assert.Contains("handler:finally", CountHandler.Trace);
        Assert.DoesNotContain("yield:999", CountHandler.Trace);
    }

    [Fact]
    public async Task GivenStreamHandlerThatThrows_WhenEnumerated_ThenItemsArriveThenExceptionSurfaces()
    {
        // Arrange
        var mediator = CreateMediator(m => m.AddHandler<FailingHandler>());

        // Act
        var received = new List<int>();
        var act = async () =>
        {
            await foreach (var item in mediator.CreateStreamAsync(new FailingRequest(2)))
            {
                received.Add(item);
            }
        };

        // Assert: the consumer sees the items produced before the failure, then the failure
        // itself at the point in the sequence where it happened.
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal([0, 1], received);
    }

    [Fact]
    public async Task GivenStreamHandler_WhenCancelled_ThenEnumerationIsCancelled()
    {
        // Arrange
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());
        using var cts = new CancellationTokenSource();

        // Act
        var act = async () =>
        {
            await foreach (var item in mediator.CreateStreamAsync(new CountRequest(1000), cts.Token))
            {
                if (item == 1)
                {
                    await cts.CancelAsync();
                }
            }
        };

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    [Fact]
    public void GivenNoStreamHandler_WhenCreatingStream_ThenThrows()
    {
        // Arrange
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());

        // Act & Assert: a bad call fails at the call site, not on first enumeration.
        Assert.Throws<InvalidOperationException>(
            () => mediator.CreateStreamAsync(new UnregisteredRequest()));
    }

    [Fact]
    public void GivenNullRequest_WhenCreatingStream_ThenThrows()
    {
        // Arrange
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => mediator.CreateStreamAsync<int>(null!));
    }

    [Fact]
    public async Task GivenHandlerYieldingNothing_WhenEnumerated_ThenSequenceIsEmpty()
    {
        // Arrange
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());

        // Act
        var received = new List<int>();
        await foreach (var item in mediator.CreateStreamAsync(new CountRequest(0)))
        {
            received.Add(item);
        }

        // Assert
        Assert.Empty(received);
    }

    [Fact]
    public async Task GivenHandlerThatFailsBeforeAnyItem_WhenEnumerated_ThenExceptionSurfaces()
    {
        // Arrange
        var mediator = CreateMediator(m => m.AddHandler<FailingHandler>());

        // Act
        var received = new List<int>();
        var act = async () =>
        {
            await foreach (var item in mediator.CreateStreamAsync(new FailingRequest(0)))
            {
                received.Add(item);
            }
        };

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Empty(received);
    }

    [Fact]
    public async Task GivenStreamHandler_WhenEnumeratedTwice_ThenEachEnumerationRunsIndependently()
    {
        // Arrange
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());
        var stream = mediator.CreateStreamAsync(new CountRequest(3));

        // Act: the returned sequence is a recipe, not a running operation, so it can be
        // enumerated more than once.
        var first = new List<int>();
        await foreach (var item in stream)
        {
            first.Add(item);
        }

        var second = new List<int>();
        await foreach (var item in stream)
        {
            second.Add(item);
        }

        // Assert
        Assert.Equal([0, 1, 2], first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GivenSlowConsumer_WhenEnumerated_ThenHandlerDoesNotRunAhead()
    {
        // Arrange
        CountHandler.Trace.Clear();
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());

        // Act: read one item and pause. Back-pressure must stop the handler from racing to
        // the end and buffering everything in memory.
        var enumerator = mediator.CreateStreamAsync(new CountRequest(100)).GetAsyncEnumerator();
        try
        {
            Assert.True(await enumerator.MoveNextAsync());
            await Task.Delay(100);

            // Assert: demand-driven lockstep means the handler produced exactly the item the
            // consumer asked for, and is parked at its yield rather than racing ahead.
            var produced = CountHandler.Trace.Count(e => e.StartsWith("yield:", StringComparison.Ordinal));
            Assert.Equal(1, produced);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task GivenStreamHandler_WhenEnumeratorDisposedEarly_ThenPipelineIsNotLeftRunning()
    {
        // Arrange
        CountHandler.Trace.Clear();
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());

        // Act
        var enumerator = mediator.CreateStreamAsync(new CountRequest(100)).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        await enumerator.DisposeAsync();

        // Assert: disposal waits for the handler to unwind, so nothing is still producing
        // once it returns.
        var producedAtDisposal = CountHandler.Trace.Count(e => e.StartsWith("yield:", StringComparison.Ordinal));
        Assert.Contains("handler:finally", CountHandler.Trace);

        await Task.Delay(100);

        Assert.Equal(
            producedAtDisposal,
            CountHandler.Trace.Count(e => e.StartsWith("yield:", StringComparison.Ordinal)));
    }

    [Fact]
    public void GivenRequestDeclaringBothShapes_WhenRegistered_ThenRejected()
    {
        // Arrange & Act & Assert: a request produces either one response or a sequence.
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(emit => emit.AddMediator(m => m.AddHandler<AmbiguousHandler>())));

        Assert.Contains(nameof(IStreamRequest<object>), exception.Message, StringComparison.Ordinal);
    }

    private sealed record AmbiguousRequest : IRequest<int>, IStreamRequest<int>;

    private sealed class AmbiguousHandler : IStreamRequestHandler<AmbiguousRequest, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            AmbiguousRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return 0;
        }
    }

    [Fact]
    public async Task GivenHandlerAwaitingBetweenItems_WhenConsumerStopsEarly_ThenDisposalIsPrompt()
    {
        // Arrange: the handler blocks forever between its first and second item.
        var mediator = CreateMediator(m => m.AddHandler<BlockingHandler>());

        // Act
        var stop = async () =>
        {
            await foreach (var _ in mediator.CreateStreamAsync(new BlockingRequest()))
            {
                break;
            }
        };

        // Assert: lockstep means the handler is parked at its yield rather than inside the
        // block, so ending the enumeration does not wait for work that never finishes.
        await stop().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GivenFailureAfterHandler_WhenConsumerStopsEarly_ThenFailureStillSurfaces()
    {
        // Arrange: middleware that fails after the handler returns, standing in for a commit
        // that throws once the stream has ended.
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>(), failAfterHandler: true);

        // Act
        var act = async () =>
        {
            await foreach (var _ in mediator.CreateStreamAsync(new CountRequest(10)))
            {
                break;
            }
        };

        // Assert: a consumer that stopped reading is no longer watching the sequence, so this
        // has to reach it through the disposal instead of being swallowed.
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task GivenConsumerBodyThrows_WhenEnumerating_ThenHandlerUnwinds()
    {
        // Arrange
        CountHandler.Trace.Clear();
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());

        // Act
        var act = async () =>
        {
            await foreach (var _ in mediator.CreateStreamAsync(new CountRequest(10)))
            {
                throw new NotSupportedException("consumer failed");
            }
        };

        // Assert
        await Assert.ThrowsAsync<NotSupportedException>(act);
        Assert.Contains("handler:finally", CountHandler.Trace);
    }

    [Fact]
    public async Task GivenSameStream_WhenEnumeratedConcurrently_ThenEnumerationsAreIndependent()
    {
        // Arrange
        var mediator = CreateMediator(m => m.AddHandler<CountHandler>());
        var stream = mediator.CreateStreamAsync(new CountRequest(5));

        // Act: two enumerations interleaved rather than one after the other.
        var first = new List<int>();
        var second = new List<int>();

        var a = stream.GetAsyncEnumerator();
        var b = stream.GetAsyncEnumerator();

        try
        {
            while (await a.MoveNextAsync() && await b.MoveNextAsync())
            {
                first.Add(a.Current);
                second.Add(b.Current);
            }
        }
        finally
        {
            await a.DisposeAsync();
            await b.DisposeAsync();
        }

        // Assert: each enumeration ran its own pipeline and saw the whole sequence.
        Assert.Equal([0, 1, 2, 3, 4], first);
        Assert.Equal([0, 1, 2, 3, 4], second);
    }

    [Fact]
    public async Task GivenPerHandlerMiddleware_WhenEnumerated_ThenItWrapsTheStream()
    {
        // Arrange: the two-parameter AddHandler overload resolves the response type through a
        // different branch than the one-parameter form, so it needs its own coverage.
        var services = new ServiceCollection();
        services.AddEmit(emit => emit.AddMediator(m =>
            m.AddHandler<CountHandler, CountRequest>(h => h.Use<CountingMiddleware<CountRequest>>())));

        using var scope = services.BuildServiceProvider().CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        CountingMiddleware<CountRequest>.Invocations = 0;

        // Act
        await foreach (var _ in mediator.CreateStreamAsync(new CountRequest(3)))
        {
            // drain
        }

        // Assert
        Assert.Equal(1, CountingMiddleware<CountRequest>.Invocations);
    }

    private sealed record BlockingRequest : IStreamRequest<int>;

    private sealed class BlockingHandler : IStreamRequestHandler<BlockingRequest, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            BlockingRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return 0;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield return 1;
        }
    }

    private sealed class CountingMiddleware<T> : IMiddleware<MediatorContext<T>>
    {
        public static int Invocations;

        public async Task InvokeAsync(MediatorContext<T> context, IMiddlewarePipeline<MediatorContext<T>> next)
        {
            Interlocked.Increment(ref Invocations);
            await next.InvokeAsync(context);
        }
    }

    private sealed class FailAfterHandlerMiddleware<T> : IMiddleware<MediatorContext<T>>
    {
        public async Task InvokeAsync(MediatorContext<T> context, IMiddlewarePipeline<MediatorContext<T>> next)
        {
            await next.InvokeAsync(context);
            throw new InvalidOperationException("Simulated failure after the handler completed");
        }
    }

    private sealed record UnregisteredRequest : IStreamRequest<int>;
}
