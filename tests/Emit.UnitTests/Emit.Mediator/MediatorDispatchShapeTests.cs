namespace Emit.Mediator.Tests;

using System.Runtime.CompilerServices;
using global::Emit.Abstractions.Pipeline;
using global::Emit.DependencyInjection;
using global::Emit.Mediator;
using global::Emit.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Behavior every dispatch shape must share, asserted once and run against each shape.
/// </summary>
/// <remarks>
/// A stream handler is a third handler shape rather than a separate mechanism, so it has to
/// behave like the others for everything that is not about producing a sequence. Deriving the
/// same facts for the void and response shapes keeps this honest: if a fact passes for those
/// and fails for streams, the difference is real rather than an artifact of how it is asserted.
/// </remarks>
public abstract class MediatorDispatchShapeTests
{
    /// <summary>Registers the shape's handler, which records its progress on the tracker.</summary>
    protected abstract void RegisterHandler(MediatorBuilder builder);

    /// <summary>Registers a handler for this shape that throws.</summary>
    protected abstract void RegisterFailingHandler(MediatorBuilder builder);

    /// <summary>Dispatches the shape's request and drives it to completion.</summary>
    protected abstract Task DispatchAsync(IMediator mediator, CancellationToken cancellationToken = default);

    /// <summary>Dispatches the shape's failing request and drives it to completion.</summary>
    protected abstract Task DispatchFailingAsync(IMediator mediator, CancellationToken cancellationToken = default);

    /// <summary>Dispatches a request of this shape that has no registered handler.</summary>
    protected abstract Task DispatchUnregisteredAsync(IMediator mediator);

    // ── Shared behavior ──

    [Fact]
    public async Task GivenMiddleware_WhenDispatched_ThenItWrapsTheHandler()
    {
        // Arrange
        var (mediator, tracker) = CreateMediator(RegisterHandler);

        // Act
        await DispatchAsync(mediator);

        // Assert: layered outermost to innermost, and unwound in reverse. The handler also
        // records its scope, which is asserted separately, so only layer markers are compared.
        var layers = tracker.Entries.Where(e => !e.StartsWith("scope:", StringComparison.Ordinal));

        Assert.Equal(
            ["global:enter", "mediator:enter", "handler", "mediator:exit", "global:exit"],
            layers);
    }

    [Fact]
    public async Task GivenHandlerThrows_WhenDispatched_ThenExceptionPropagates()
    {
        // Arrange
        var (mediator, _) = CreateMediator(RegisterFailingHandler);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => DispatchFailingAsync(mediator));
    }

    [Fact]
    public async Task GivenHandlerThrows_WhenDispatched_ThenMiddlewareStillUnwinds()
    {
        // Arrange
        var (mediator, tracker) = CreateMediator(RegisterFailingHandler);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => DispatchFailingAsync(mediator));

        // Assert: a failure must not strand middleware mid-pipeline.
        Assert.Contains("mediator:exit", tracker.Entries);
        Assert.Contains("global:exit", tracker.Entries);
    }

    [Fact]
    public async Task GivenNoRegisteredHandler_WhenDispatched_ThenThrowsInvalidOperation()
    {
        // Arrange
        var (mediator, _) = CreateMediator(RegisterHandler);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => DispatchUnregisteredAsync(mediator));
    }

    [Fact]
    public async Task GivenScopedService_WhenDispatched_ThenHandlerSharesTheCallerScope()
    {
        // Arrange
        var (mediator, tracker) = CreateMediator(RegisterHandler, out var scope);
        var callerInstance = scope.ServiceProvider.GetRequiredService<ScopeProbe>().Id;

        // Act
        await DispatchAsync(mediator);

        // Assert: handlers run in the caller's scope, which is what lets a caller and its
        // handlers share one transaction.
        Assert.Contains($"scope:{callerInstance}", tracker.Entries);
    }

    [Fact]
    public async Task GivenCancelledToken_WhenDispatched_ThenCancellationSurfaces()
    {
        // Arrange
        var (mediator, _) = CreateMediator(RegisterHandler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DispatchAsync(mediator, cts.Token));
    }

    // ── Harness ──

    private static (IMediator Mediator, DispatchTracker Tracker) CreateMediator(Action<MediatorBuilder> configure)
        => CreateMediator(configure, out _);

    private static (IMediator Mediator, DispatchTracker Tracker) CreateMediator(
        Action<MediatorBuilder> configure,
        out IServiceScope scope)
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeProbe>();
        services.AddScoped<DispatchTracker>();

        services.AddEmit(emit =>
        {
            emit.InboundPipeline.Use(typeof(GlobalRecordingMiddleware<>));
            emit.AddMediator(mediator =>
            {
                mediator.InboundPipeline.Use(typeof(MediatorRecordingMiddleware<>));
                configure(mediator);
            });
        });

        scope = services.BuildServiceProvider().CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMediator>(),
                scope.ServiceProvider.GetRequiredService<DispatchTracker>());
    }
}

/// <summary>Records what each layer did, resolved from the dispatching scope.</summary>
internal sealed class DispatchTracker
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

/// <summary>Identifies a scope so a handler can prove which one it ran in.</summary>
internal sealed class ScopeProbe
{
    public Guid Id { get; } = Guid.NewGuid();
}

internal sealed class GlobalRecordingMiddleware<T> : IMiddleware<MediatorContext<T>>
{
    public async Task InvokeAsync(MediatorContext<T> context, IMiddlewarePipeline<MediatorContext<T>> next)
    {
        var tracker = context.Services.GetRequiredService<DispatchTracker>();
        tracker.Add("global:enter");
        try
        {
            await next.InvokeAsync(context);
        }
        finally
        {
            tracker.Add("global:exit");
        }
    }
}

internal sealed class MediatorRecordingMiddleware<T> : IMiddleware<MediatorContext<T>>
{
    public async Task InvokeAsync(MediatorContext<T> context, IMiddlewarePipeline<MediatorContext<T>> next)
    {
        var tracker = context.Services.GetRequiredService<DispatchTracker>();
        tracker.Add("mediator:enter");
        try
        {
            await next.InvokeAsync(context);
        }
        finally
        {
            tracker.Add("mediator:exit");
        }
    }
}

// ── Void shape ──

public sealed class VoidDispatchShapeTests : MediatorDispatchShapeTests
{
    internal sealed record VoidRequest : IRequest;

    internal sealed record FailingVoidRequest : IRequest;

    internal sealed record UnregisteredVoidRequest : IRequest;

    internal sealed class VoidHandler(DispatchTracker tracker, ScopeProbe probe) : IRequestHandler<VoidRequest>
    {
        public Task HandleAsync(VoidRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tracker.Add("handler");
            tracker.Add($"scope:{probe.Id}");
            return Task.CompletedTask;
        }
    }

    internal sealed class FailingVoidHandler : IRequestHandler<FailingVoidRequest>
    {
        public Task HandleAsync(FailingVoidRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated handler failure");
    }

    protected override void RegisterHandler(MediatorBuilder builder) => builder.AddHandler<VoidHandler>();

    protected override void RegisterFailingHandler(MediatorBuilder builder) => builder.AddHandler<FailingVoidHandler>();

    protected override Task DispatchAsync(IMediator mediator, CancellationToken cancellationToken = default)
        => mediator.SendAsync(new VoidRequest(), cancellationToken);

    protected override Task DispatchFailingAsync(IMediator mediator, CancellationToken cancellationToken = default)
        => mediator.SendAsync(new FailingVoidRequest(), cancellationToken);

    protected override Task DispatchUnregisteredAsync(IMediator mediator)
        => mediator.SendAsync(new UnregisteredVoidRequest());
}

// ── Response shape ──

public sealed class ResponseDispatchShapeTests : MediatorDispatchShapeTests
{
    internal sealed record ResponseRequest : IRequest<string>;

    internal sealed record FailingResponseRequest : IRequest<string>;

    internal sealed record UnregisteredResponseRequest : IRequest<string>;

    internal sealed class ResponseHandler(DispatchTracker tracker, ScopeProbe probe) : IRequestHandler<ResponseRequest, string>
    {
        public Task<string> HandleAsync(ResponseRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tracker.Add("handler");
            tracker.Add($"scope:{probe.Id}");
            return Task.FromResult("done");
        }
    }

    internal sealed class FailingResponseHandler : IRequestHandler<FailingResponseRequest, string>
    {
        public Task<string> HandleAsync(FailingResponseRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated handler failure");
    }

    protected override void RegisterHandler(MediatorBuilder builder) => builder.AddHandler<ResponseHandler>();

    protected override void RegisterFailingHandler(MediatorBuilder builder) => builder.AddHandler<FailingResponseHandler>();

    protected override Task DispatchAsync(IMediator mediator, CancellationToken cancellationToken = default)
        => mediator.SendAsync(new ResponseRequest(), cancellationToken);

    protected override Task DispatchFailingAsync(IMediator mediator, CancellationToken cancellationToken = default)
        => mediator.SendAsync(new FailingResponseRequest(), cancellationToken);

    protected override Task DispatchUnregisteredAsync(IMediator mediator)
        => mediator.SendAsync(new UnregisteredResponseRequest());
}

// ── Stream shape ──

public sealed class StreamDispatchShapeTests : MediatorDispatchShapeTests
{
    internal sealed record StreamRequest : IStreamRequest<string>;

    internal sealed record FailingStreamRequest : IStreamRequest<string>;

    internal sealed record UnregisteredStreamRequest : IStreamRequest<string>;

    internal sealed class StreamHandler(DispatchTracker tracker, ScopeProbe probe)
        : IStreamRequestHandler<StreamRequest, string>
    {
        public async IAsyncEnumerable<string> HandleAsync(
            StreamRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tracker.Add("handler");
            tracker.Add($"scope:{probe.Id}");
            yield return "one";
            await Task.Yield();
        }
    }

    internal sealed class FailingStreamHandler : IStreamRequestHandler<FailingStreamRequest, string>
    {
        public async IAsyncEnumerable<string> HandleAsync(
            FailingStreamRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new InvalidOperationException("Simulated handler failure");
#pragma warning disable CS0162 // Unreachable: required for the method to be an iterator.
            yield break;
#pragma warning restore CS0162
        }
    }

    protected override void RegisterHandler(MediatorBuilder builder) => builder.AddHandler<StreamHandler>();

    protected override void RegisterFailingHandler(MediatorBuilder builder) => builder.AddHandler<FailingStreamHandler>();

    protected override Task DispatchAsync(IMediator mediator, CancellationToken cancellationToken = default)
        => DrainAsync(mediator.CreateStreamAsync(new StreamRequest(), cancellationToken), cancellationToken);

    protected override Task DispatchFailingAsync(IMediator mediator, CancellationToken cancellationToken = default)
        => DrainAsync(mediator.CreateStreamAsync(new FailingStreamRequest(), cancellationToken), cancellationToken);

    protected override Task DispatchUnregisteredAsync(IMediator mediator)
        => DrainAsync(mediator.CreateStreamAsync(new UnregisteredStreamRequest()), CancellationToken.None);

    private static async Task DrainAsync<T>(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        await foreach (var _ in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // Draining is the point; the items themselves do not matter here.
        }
    }
}
