namespace Emit.Mediator.Tests;

using System.Runtime.CompilerServices;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.DependencyInjection;
using global::Emit.Mediator;
using global::Emit.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Unit-of-work callbacks registered by mediator handlers, dispatched through the real pipeline.
/// </summary>
public sealed class MediatorUnitOfWorkCallbackTests
{
    [Fact]
    public async Task GivenHandlerRegisteringCallbacks_WhenItSucceeds_ThenSucceededRunsOnceBeforeSendReturns()
    {
        // Arrange
        var (mediator, log) = CreateMediator(m => m.AddHandler<RecordingHandler>());

        // Act
        await mediator.SendAsync(new RecordingRequest(Fail: false));
        log.Add("send returned");

        // Assert
        Assert.Equal(["handler", "succeeded", "send returned"], log.Entries);
    }

    [Fact]
    public async Task GivenHandlerRegisteringCallbacks_WhenItThrows_ThenFailedRunsWithTheException()
    {
        // Arrange
        var (mediator, log) = CreateMediator(m => m.AddHandler<RecordingHandler>());

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.SendAsync(new RecordingRequest(Fail: true)));

        // Assert
        Assert.Equal(["handler", "failed: InvalidOperationException"], log.Entries);
    }

    [Fact]
    public async Task GivenHandlerMiddlewareThatThrowsAfterTheHandler_WhenDispatched_ThenFailedRunsInsteadOfSucceeded()
    {
        // Arrange: the handler's own middleware belongs to its unit of work
        var (mediator, log) = CreateMediator(m =>
            m.AddHandler<RecordingHandler, RecordingRequest>(h => h.Use<ThrowAfterHandlerMiddleware>()));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.SendAsync(new RecordingRequest(Fail: false)));

        // Assert
        Assert.Equal(["handler", "failed: InvalidOperationException"], log.Entries);
    }

    [Fact]
    public async Task GivenCancelledRequest_WhenHandlerObservesTheCancellation_ThenFailedRunsWithTheCancellation()
    {
        // Arrange
        var (mediator, log) = CreateMediator(m => m.AddHandler<CancellationObservingHandler>());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mediator.SendAsync(new CancellableRequest(), cts.Token));

        // Assert
        Assert.Equal(["failed: OperationCanceledException"], log.Entries);
    }

    [Fact]
    public async Task GivenStreamHandlerRegisteringCallbacks_WhenFullyEnumerated_ThenSucceededRunsOnceAfterTheLastItem()
    {
        // Arrange
        var (mediator, log) = CreateMediator(m => m.AddHandler<RecordingStreamHandler>());

        // Act
        await foreach (var item in mediator.CreateStreamAsync(new RecordingStreamRequest()))
        {
            log.Add($"item {item}");
        }

        log.Add("enumeration ended");

        // Assert
        Assert.Equal(["item 1", "item 2", "succeeded", "enumeration ended"], log.Entries);
    }

    [Fact]
    public async Task GivenConcurrentRequestsInOneScope_WhenOneFails_ThenEachRunsOnlyItsOwnCallbacks()
    {
        // Arrange
        var (mediator, log) = CreateMediator(m => m.AddHandler<RecordingHandler>());

        // Act
        var succeeding = mediator.SendAsync(new RecordingRequest(Fail: false, Delay: true));
        var failing = mediator.SendAsync(new RecordingRequest(Fail: true, Delay: true));
        await succeeding;
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing);

        // Assert
        Assert.Equal(1, log.Entries.Count(e => e == "succeeded"));
        Assert.Equal(1, log.Entries.Count(e => e == "failed: InvalidOperationException"));
    }

    // ── Harness ──

    private static (IMediator Mediator, CallbackLog Log) CreateMediator(Action<MediatorBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddScoped<CallbackLog>();
        services.AddEmit(emit => emit.AddMediator(configure));

        var scope = services.BuildServiceProvider().CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMediator>(),
                scope.ServiceProvider.GetRequiredService<CallbackLog>());
    }

    internal sealed class CallbackLog
    {
        private readonly List<string> entries = [];

        public IReadOnlyList<string> Entries
        {
            get { lock (entries) { return [.. entries]; } }
        }

        public void Add(string entry)
        {
            lock (entries) { entries.Add(entry); }
        }
    }

    internal sealed record RecordingRequest(bool Fail, bool Delay = false) : IRequest;

    internal sealed class RecordingHandler(IEmitContext emitContext, CallbackLog log) : IRequestHandler<RecordingRequest>
    {
        public async Task HandleAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
            emitContext.OnSucceeded(_ =>
            {
                log.Add("succeeded");
                return Task.CompletedTask;
            });
            emitContext.OnFailed((ex, _) =>
            {
                log.Add($"failed: {ex.GetType().Name}");
                return Task.CompletedTask;
            });

            if (request.Delay)
            {
                await Task.Yield();
            }

            log.Add("handler");
            if (request.Fail)
            {
                throw new InvalidOperationException("handler failed");
            }
        }
    }

    internal sealed record CancellableRequest : IRequest;

    internal sealed class CancellationObservingHandler(IEmitContext emitContext, CallbackLog log) : IRequestHandler<CancellableRequest>
    {
        public Task HandleAsync(CancellableRequest request, CancellationToken cancellationToken = default)
        {
            emitContext.OnSucceeded(_ =>
            {
                log.Add("succeeded");
                return Task.CompletedTask;
            });
            emitContext.OnFailed((ex, _) =>
            {
                log.Add($"failed: {ex.GetType().Name}");
                return Task.CompletedTask;
            });

            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    internal sealed record RecordingStreamRequest : IStreamRequest<int>;

    internal sealed class RecordingStreamHandler(IEmitContext emitContext, CallbackLog log) : IStreamRequestHandler<RecordingStreamRequest, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            RecordingStreamRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            emitContext.OnSucceeded(_ =>
            {
                log.Add("succeeded");
                return Task.CompletedTask;
            });

            yield return 1;
            await Task.Yield();
            yield return 2;
        }
    }

    internal sealed class ThrowAfterHandlerMiddleware : IMiddleware<MediatorContext<RecordingRequest>>
    {
        public async Task InvokeAsync(MediatorContext<RecordingRequest> context, Func<Task> next)
        {
            await next();
            throw new InvalidOperationException("failed after the handler");
        }
    }
}
