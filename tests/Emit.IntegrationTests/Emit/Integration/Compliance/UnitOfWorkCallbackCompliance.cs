namespace Emit.IntegrationTests.Integration.Compliance;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Mediator;
using Emit.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static Emit.IntegrationTests.Integration.TestHelpers;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Compliance tests for the callbacks registered through <see cref="IEmitContext.OnSucceeded"/> and
/// <see cref="IEmitContext.OnFailed"/>. Verifies, against a real database and broker, that they run
/// once at the end of the unit of work: success callbacks after the transaction commits, failure
/// callbacks once no retry follows, for consumers, routed consumers, batch consumers and mediator
/// handlers, with and without the outbox.
/// </summary>
/// <remarks>
/// Each callback records whether the transaction its handler ran in had committed by the time the
/// callback ran. A callback that ran before the commit records <see langword="false"/>, so these
/// facts detect a callback fired too early on every provider without reading provider-specific state.
/// </remarks>
[Trait("Category", "Integration")]
public abstract class UnitOfWorkCallbackCompliance : IAsyncLifetime
{
    /// <summary>Gets the Kafka bootstrap servers address.</summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Configures the persistence provider (MongoDB or EF Core).
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="outbox">Whether to enable the outbox.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit, bool outbox);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    // ── Consumers ──

    [Fact]
    public async Task GivenTransactionalConsumer_WhenItSucceeds_ThenSucceededRunsOnceAfterTheCommit()
    {
        // Arrange
        var recorder = new CallbackRecorder();
        var value = $"ok-{Guid.NewGuid():N}";
        await using var run = await StartAsync(recorder, group => group.AddConsumer<CallbackTransactionalConsumer>());

        // Act
        await run.ProduceAsync(value);

        // Assert
        var record = await recorder.WaitForAsync(value, CallbackEvent.Succeeded);
        Assert.True(record.Committed);
        Assert.Single(recorder.For(value));
    }

    [Fact]
    public async Task GivenTransactionalConsumerWithoutOutbox_WhenItSucceeds_ThenSucceededRunsOnceAfterTheCommit()
    {
        // Arrange: transactions come from the persistence provider, not from the outbox
        var recorder = new CallbackRecorder();
        var value = $"no-outbox-{Guid.NewGuid():N}";
        await using var run = await StartAsync(
            recorder,
            group => group.AddConsumer<CallbackTransactionalConsumer>(),
            outbox: false);

        // Act
        await run.ProduceAsync(value);

        // Assert
        var record = await recorder.WaitForAsync(value, CallbackEvent.Succeeded);
        Assert.True(record.Committed);
        Assert.Single(recorder.For(value));
    }

    [Fact]
    public async Task GivenConsumerFailingOnce_WhenTheRetrySucceeds_ThenSucceededRunsOnceAndFailedNever()
    {
        // Arrange
        var recorder = new CallbackRecorder();
        var value = $"retry-{Guid.NewGuid():N}";
        await using var run = await StartAsync(recorder, group =>
        {
            group.OnError(error => error.Default(a => a.Retry(2, Backoff.None).Discard()));
            group.AddConsumer<CallbackFailOnceConsumer>();
        });

        // Act
        await run.ProduceAsync(value);

        // Assert: the failed first attempt registered callbacks too, and they must not run
        var record = await recorder.WaitForAsync(value, CallbackEvent.Succeeded);
        Assert.True(record.Committed);
        Assert.Single(recorder.For(value));
    }

    [Fact]
    public async Task GivenConsumerThatAlwaysFails_WhenRetriesAreUsedUp_ThenFailedRunsOnceWithTheException()
    {
        // Arrange
        var recorder = new CallbackRecorder();
        var value = $"fail-{Guid.NewGuid():N}";
        await using var run = await StartAsync(recorder, group =>
        {
            group.OnError(error => error.Default(a => a.Retry(2, Backoff.None).Discard()));
            group.AddConsumer<CallbackAlwaysFailingConsumer>();
        });

        // Act
        await run.ProduceAsync(value);

        // Assert: three attempts ran, and only the last one's failure callback ran
        var record = await recorder.WaitForAsync(value, CallbackEvent.Failed);
        Assert.Equal(nameof(InvalidOperationException), record.Exception);
        Assert.False(record.Committed);
        Assert.Single(recorder.For(value));
        Assert.Equal(3, recorder.Attempts(value));
    }

    [Fact]
    public async Task GivenTransactionalRoutedConsumer_WhenItSucceeds_ThenSucceededRunsOnceAfterTheCommit()
    {
        // Arrange
        var recorder = new CallbackRecorder();
        var value = $"routed-{Guid.NewGuid():N}";
        await using var run = await StartAsync(recorder, group =>
            group.AddRouter<string>(
                "callback-router",
                _ => "route",
                routes => routes.Route<CallbackTransactionalConsumer>("route")));

        // Act
        await run.ProduceAsync(value);

        // Assert
        var record = await recorder.WaitForAsync(value, CallbackEvent.Succeeded);
        Assert.True(record.Committed);
        Assert.Single(recorder.For(value));
    }

    [Fact]
    public async Task GivenTransactionalBatchConsumer_WhenItSucceeds_ThenSucceededRunsOnceAfterTheCommit()
    {
        // Arrange
        var recorder = new CallbackRecorder();
        var value = $"batch-{Guid.NewGuid():N}";
        await using var run = await StartAsync(recorder, group =>
            group.AddBatchConsumer<CallbackTransactionalBatchConsumer>(options =>
            {
                options.MaxSize = 10;
                options.Timeout = TimeSpan.FromMilliseconds(200);
            }));

        // Act
        await run.ProduceAsync(value);

        // Assert
        var record = await recorder.WaitForAsync(value, CallbackEvent.Succeeded);
        Assert.True(record.Committed);
        Assert.Single(recorder.For(value));
    }

    // ── Mediator ──

    [Fact]
    public async Task GivenMediatorCalledFromTransactionalConsumer_WhenBothSucceed_ThenHandlerCallbackRunsAfterTheConsumerCommit()
    {
        // Arrange: the handler is not transactional, so it writes into the consumer's transaction
        var recorder = new CallbackRecorder();
        var value = $"nested-{Guid.NewGuid():N}";
        await using var run = await StartAsync(
            recorder,
            group => group.AddConsumer<CallbackMediatorCallingConsumer>(),
            configureMediator: m => m.AddHandler<CallbackNestedHandler>());

        // Act
        await run.ProduceAsync(value);

        // Assert
        var record = await recorder.WaitForAsync(value, CallbackEvent.Succeeded);
        Assert.True(record.Committed);
        Assert.Single(recorder.For(value));
    }

    [Fact]
    public async Task GivenTransactionalStreamHandler_WhenFullyEnumerated_ThenSucceededRunsOnceAfterTheCommit()
    {
        // Arrange
        var recorder = new CallbackRecorder();
        var value = $"stream-{Guid.NewGuid():N}";
        await using var run = await StartAsync(
            recorder,
            group => group.AddConsumer<CallbackTransactionalConsumer>(),
            configureMediator: m => m.AddHandler<CallbackTransactionalStreamHandler>());

        // Act
        using (var scope = run.Host.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await foreach (var _ in mediator.CreateStreamAsync(new CallbackStreamRequest(value)))
            {
                // Draining is the point
            }
        }

        // Assert
        var record = await recorder.WaitForAsync(value, CallbackEvent.Succeeded);
        Assert.True(record.Committed);
        Assert.Single(recorder.For(value));
    }

    // ── Helpers ──

    private async Task<CallbackRun> StartAsync(
        CallbackRecorder recorder,
        Action<KafkaConsumerGroupBuilder<string, string>> configureGroup,
        bool outbox = true,
        Action<MediatorBuilder>? configureMediator = null)
    {
        var topic = $"test-uow-callback-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(recorder);
                services.AddEmit(emit =>
                {
                    ConfigureFastCoordination(emit, NewServiceName());
                    ConfigurePersistence(emit, outbox);
                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config => config.BootstrapServers = BootstrapServers);
                        kafka.AutoProvision();
                        kafka.Topic<string, string>(topic, t =>
                        {
                            t.SetUtf8KeyDeserializer();
                            t.SetUtf8ValueDeserializer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                configureGroup(group);
                            });
                        });
                    });

                    if (configureMediator is not null)
                    {
                        emit.AddMediator(configureMediator);
                    }
                });
            })
            .Build();

        await host.StartAsync();
        return new CallbackRun(host, topic, BootstrapServers);
    }

    private sealed class CallbackRun(IHost host, string topic, string bootstrapServers) : IAsyncDisposable
    {
        public IHost Host => host;

        public async Task ProduceAsync(string value)
        {
            using var producer = new ConfluentKafka.ProducerBuilder<string, string>(
                new ConfluentKafka.ProducerConfig { BootstrapServers = bootstrapServers })
                .Build();

            await producer.ProduceAsync(topic, new ConfluentKafka.Message<string, string> { Key = "k", Value = value });
        }

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}

/// <summary>Which callback ran.</summary>
public enum CallbackEvent
{
    /// <summary>An <see cref="IEmitContext.OnSucceeded"/> callback.</summary>
    Succeeded,

    /// <summary>An <see cref="IEmitContext.OnFailed"/> callback.</summary>
    Failed,
}

/// <summary>A callback that ran, with the state it observed.</summary>
/// <param name="Value">The message or request the callback belongs to.</param>
/// <param name="Event">Which callback ran.</param>
/// <param name="Committed">Whether the handler's transaction had committed when the callback ran.</param>
/// <param name="Exception">The exception type name passed to a failure callback.</param>
public sealed record CallbackRecord(string Value, CallbackEvent Event, bool Committed, string? Exception);

/// <summary>Collects the callbacks that ran, and the attempts made, across a test's host.</summary>
public sealed class CallbackRecorder
{
    private readonly ConcurrentQueue<CallbackRecord> records = new();
    private readonly ConcurrentDictionary<string, int> attempts = new();

    /// <summary>Returns the callbacks recorded for <paramref name="value"/>.</summary>
    public IReadOnlyList<CallbackRecord> For(string value) => [.. records.Where(r => r.Value == value)];

    /// <summary>Returns how many attempts were made for <paramref name="value"/>.</summary>
    public int Attempts(string value) => attempts.GetValueOrDefault(value);

    /// <summary>Counts an attempt for <paramref name="value"/> and returns its number, starting at 1.</summary>
    public int StartAttempt(string value) => attempts.AddOrUpdate(value, 1, (_, count) => count + 1);

    /// <summary>
    /// Registers both callbacks on <paramref name="emitContext"/> for <paramref name="value"/>, each
    /// recording whether <paramref name="transaction"/> had committed when it ran.
    /// </summary>
    public void Register(IEmitContext emitContext, string value, ITransactionContext? transaction)
    {
        emitContext.OnSucceeded(_ =>
        {
            records.Enqueue(new CallbackRecord(value, CallbackEvent.Succeeded, transaction?.IsCommitted ?? false, null));
            return Task.CompletedTask;
        });
        emitContext.OnFailed((exception, _) =>
        {
            records.Enqueue(new CallbackRecord(value, CallbackEvent.Failed, transaction?.IsCommitted ?? false, exception.GetType().Name));
            return Task.CompletedTask;
        });
    }

    /// <summary>Waits until a callback of <paramref name="callbackEvent"/> ran for <paramref name="value"/>.</summary>
    public async Task<CallbackRecord> WaitForAsync(string value, CallbackEvent callbackEvent)
    {
        await WaitUntilAsync(
            () => records.Any(r => r.Value == value && r.Event == callbackEvent),
            $"No {callbackEvent} callback ran for {value}");
        return records.First(r => r.Value == value && r.Event == callbackEvent);
    }
}

/// <summary>A [Transactional] consumer registering both callbacks.</summary>
[Transactional]
public sealed class CallbackTransactionalConsumer(IEmitContext emitContext, CallbackRecorder recorder) : IConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
    {
        recorder.StartAttempt(context.Message!);
        recorder.Register(emitContext, context.Message!, emitContext.Transaction);
        return Task.CompletedTask;
    }
}

/// <summary>A [Transactional] consumer registering both callbacks, then failing its first attempt.</summary>
[Transactional]
public sealed class CallbackFailOnceConsumer(IEmitContext emitContext, CallbackRecorder recorder) : IConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
    {
        var attempt = recorder.StartAttempt(context.Message!);
        recorder.Register(emitContext, context.Message!, emitContext.Transaction);
        return attempt == 1
            ? throw new InvalidOperationException("Simulated transient failure")
            : Task.CompletedTask;
    }
}

/// <summary>A [Transactional] consumer registering both callbacks, then always failing.</summary>
[Transactional]
public sealed class CallbackAlwaysFailingConsumer(IEmitContext emitContext, CallbackRecorder recorder) : IConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
    {
        recorder.StartAttempt(context.Message!);
        recorder.Register(emitContext, context.Message!, emitContext.Transaction);
        throw new InvalidOperationException("Simulated failure");
    }
}

/// <summary>A [Transactional] batch consumer registering both callbacks for each item.</summary>
[Transactional]
public sealed class CallbackTransactionalBatchConsumer(IEmitContext emitContext, CallbackRecorder recorder) : IBatchConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<MessageBatch<string>> context, CancellationToken cancellationToken)
    {
        foreach (var item in context.Message.Items)
        {
            recorder.StartAttempt(item.Message!);
            recorder.Register(emitContext, item.Message!, emitContext.Transaction);
        }

        return Task.CompletedTask;
    }
}

/// <summary>A request handled by <see cref="CallbackNestedHandler"/>.</summary>
public sealed record CallbackNestedRequest(string Value) : IRequest;

/// <summary>A [Transactional] consumer that sends a mediator request while its transaction is open.</summary>
[Transactional]
public sealed class CallbackMediatorCallingConsumer(IMediator mediator) : IConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) =>
        mediator.SendAsync(new CallbackNestedRequest(context.Message!), cancellationToken);
}

/// <summary>
/// An undecorated handler registering both callbacks. Called from a [Transactional] consumer, it
/// records the consumer's transaction, which its callbacks must wait for.
/// </summary>
public sealed class CallbackNestedHandler(IEmitContext emitContext, CallbackRecorder recorder) : IRequestHandler<CallbackNestedRequest>
{
    /// <inheritdoc />
    public Task HandleAsync(CallbackNestedRequest request, CancellationToken cancellationToken = default)
    {
        recorder.StartAttempt(request.Value);
        recorder.Register(emitContext, request.Value, emitContext.Transaction);
        return Task.CompletedTask;
    }
}

/// <summary>A request handled by <see cref="CallbackTransactionalStreamHandler"/>.</summary>
public sealed record CallbackStreamRequest(string Value) : IStreamRequest<int>;

/// <summary>A [Transactional] stream handler registering both callbacks before producing items.</summary>
[Transactional]
public sealed class CallbackTransactionalStreamHandler(IEmitContext emitContext, CallbackRecorder recorder)
    : IStreamRequestHandler<CallbackStreamRequest, int>
{
    /// <inheritdoc />
    public async IAsyncEnumerable<int> HandleAsync(
        CallbackStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        recorder.StartAttempt(request.Value);
        recorder.Register(emitContext, request.Value, emitContext.Transaction);

        for (var i = 0; i < 3; i++)
        {
            yield return i;
            await Task.Yield();
        }
    }
}
