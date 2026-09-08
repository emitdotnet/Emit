namespace Emit.IntegrationTests.Integration.Compliance;

using System.Runtime.CompilerServices;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Mediator;
using Emit.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for the [Transactional] attribute on mediator request handlers.
/// Verifies that a transaction is started and committed for decorated handlers, rolled back
/// when they fail, and that no transaction is started for undecorated handlers, across both
/// handler shapes: <see cref="IRequestHandler{TRequest}"/> and
/// <see cref="IRequestHandler{TRequest, TResponse}"/>.
/// </summary>
/// <remarks>
/// Assertions go through <see cref="IEmitContext.Transaction"/>, which every persistence
/// provider sets from its unit of work, so one set of facts covers all providers. Observing
/// the transaction from inside the handler (rather than inferring it from an outbox delivery)
/// is what makes these tests able to detect an attribute that is silently ignored, and removes
/// any dependency on a message broker or daemon polling.
/// </remarks>
[Trait("Category", "Integration")]
public abstract class MediatorTransactionalCompliance : IAsyncLifetime
{
    /// <summary>
    /// Gets the Kafka bootstrap servers address. The outbox requires a transport, so one is
    /// registered even though these tests assert transaction scope rather than delivery.
    /// </summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Configures the persistence provider (MongoDB or EF Core) with the outbox enabled,
    /// which is what registers <see cref="IUnitOfWork"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit);

    /// <summary>
    /// Configures the same persistence provider with the outbox left off, so no
    /// <see cref="IUnitOfWork"/> is registered.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    protected abstract void ConfigurePersistenceWithoutOutbox(EmitBuilder emit);

    /// <summary>
    /// Registers the provider's ambient session inspector. Providers that expose a session
    /// handle override this so provider-specific facts can assert on it.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    protected virtual void RegisterAmbientSessionInspector(IServiceCollection services)
        => services.AddScoped<IAmbientSessionInspector, NullAmbientSessionInspector>();

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    // ── A decorated handler runs inside a transaction, in both shapes ──

    [Fact]
    public async Task GivenTransactionalResponseHandler_WhenSent_ThenTransactionActiveInsideHandler()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalResponseHandler>());

        try
        {
            // Act
            await SendAsync(host, new TransactionalResponseRequest("hello"));

            // Assert
            var observation = Assert.Single(probe.Observations);
            Assert.NotNull(observation.Transaction);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    [Fact]
    public async Task GivenTransactionalVoidHandler_WhenSent_ThenTransactionActiveInsideHandler()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalVoidHandler>());

        try
        {
            // Act
            await SendAsync(host, new TransactionalVoidRequest());

            // Assert
            var observation = Assert.Single(probe.Observations);
            Assert.NotNull(observation.Transaction);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    // ── An undecorated handler must not get one, in both shapes ──

    [Fact]
    public async Task GivenPlainResponseHandler_WhenSent_ThenNoTransactionInsideHandler()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<PlainResponseHandler>());

        try
        {
            // Act
            await SendAsync(host, new PlainResponseRequest("hello"));

            // Assert
            var observation = Assert.Single(probe.Observations);
            Assert.Null(observation.Transaction);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    [Fact]
    public async Task GivenPlainVoidHandler_WhenSent_ThenNoTransactionInsideHandler()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<PlainVoidHandler>());

        try
        {
            // Act
            await SendAsync(host, new PlainVoidRequest());

            // Assert
            var observation = Assert.Single(probe.Observations);
            Assert.Null(observation.Transaction);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    // ── Success commits, in both shapes ──

    [Fact]
    public async Task GivenTransactionalResponseHandler_WhenHandlerSucceeds_ThenTransactionCommitted()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalResponseHandler>());

        try
        {
            // Act
            await SendAsync(host, new TransactionalResponseRequest("hello"));

            // Assert
            var transaction = Assert.Single(probe.Observations).Transaction;
            Assert.NotNull(transaction);
            Assert.True(transaction.IsCommitted);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    [Fact]
    public async Task GivenTransactionalVoidHandler_WhenHandlerSucceeds_ThenTransactionCommitted()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalVoidHandler>());

        try
        {
            // Act
            await SendAsync(host, new TransactionalVoidRequest());

            // Assert
            var transaction = Assert.Single(probe.Observations).Transaction;
            Assert.NotNull(transaction);
            Assert.True(transaction.IsCommitted);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    // ── Failure must not commit, in both shapes ──

    [Fact]
    public async Task GivenTransactionalResponseHandler_WhenHandlerThrows_ThenTransactionNotCommitted()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalResponseHandler>());

        try
        {
            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => SendAsync(host, new TransactionalResponseRequest("boom", ShouldThrow: true)));

            // Assert
            var transaction = Assert.Single(probe.Observations).Transaction;
            Assert.NotNull(transaction);
            Assert.False(transaction.IsCommitted);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    [Fact]
    public async Task GivenTransactionalVoidHandler_WhenHandlerThrows_ThenTransactionNotCommitted()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalVoidHandler>());

        try
        {
            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => SendAsync(host, new TransactionalVoidRequest(ShouldThrow: true)));

            // Assert
            var transaction = Assert.Single(probe.Observations).Transaction;
            Assert.NotNull(transaction);
            Assert.False(transaction.IsCommitted);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    // ── Registration order must not decide whether the attribute works ──

    [Fact]
    public async Task GivenPersistenceRegisteredAfterMediator_WhenResponseHandlerSent_ThenTransactionActive()
    {
        // Arrange
        var (host, probe) = BuildHost(
            m => m.AddHandler<TransactionalResponseHandler>(),
            EmitRegistrationOrder.PersistenceLast);

        try
        {
            // Act
            await SendAsync(host, new TransactionalResponseRequest("hello"));

            // Assert
            var observation = Assert.Single(probe.Observations);
            Assert.NotNull(observation.Transaction);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    [Fact]
    public async Task GivenPersistenceRegisteredAfterMediator_WhenVoidHandlerSent_ThenTransactionActive()
    {
        // Arrange
        var (host, probe) = BuildHost(
            m => m.AddHandler<TransactionalVoidHandler>(),
            EmitRegistrationOrder.PersistenceLast);

        try
        {
            // Act
            await SendAsync(host, new TransactionalVoidRequest());

            // Assert
            var observation = Assert.Single(probe.Observations);
            Assert.NotNull(observation.Transaction);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    // ── Consecutive requests must each get their own transaction ──

    [Fact]
    public async Task GivenTwoTransactionalRequests_WhenSentInTheSameScope_ThenEachGetsItsOwnTransaction()
    {
        // Arrange — the mediator deliberately runs handlers in the caller's scope, so an
        // endpoint or job that sends two commands shares one IEmitContext between them.
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalResponseHandler>());

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.SendAsync(new TransactionalResponseRequest("first"));
            await mediator.SendAsync(new TransactionalResponseRequest("second"));

            // Assert — a transaction left behind by the first request would either be reused by
            // the second or make it fail outright, so both must be distinct and committed.
            Assert.Equal(2, probe.Count);
            var transactions = probe.Observations.Select(o => o.Transaction).ToList();
            Assert.All(transactions, t => Assert.NotNull(t));
            Assert.All(transactions, t => Assert.True(t!.IsCommitted));
            Assert.NotSame(transactions[0], transactions[1]);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    // ── A transaction that cannot be honoured must not be ignored ──

    [Fact]
    public void GivenTransactionalHandlerAndNoOutbox_WhenHostBuilt_ThenConfigurationIsRejected()
    {
        // Arrange & Act — persistence is present but the outbox is off, so no IUnitOfWork
        // exists and the attribute cannot possibly be honoured.
        static void Build(Action<EmitBuilder> configurePersistence) =>
            Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(new TransactionProbe());
                    services.AddScoped<IAmbientSessionInspector, NullAmbientSessionInspector>();
                    services.AddEmit(emit =>
                    {
                        configurePersistence(emit);
                        emit.AddMediator(m => m.AddHandler<TransactionalResponseHandler>());
                    });
                })
                .Build()
                .Dispose();

        // Assert — silently dropping the transaction would let a handler believe its writes
        // are atomic when they are not, so this must fail loudly rather than be ignored.
        var exception = Assert.Throws<InvalidOperationException>(() => Build(ConfigurePersistenceWithoutOutbox));
        Assert.Contains(nameof(TransactionalResponseHandler), exception.Message, StringComparison.Ordinal);
    }

    // ── The transactional wrapper must not disturb the response ──

    [Fact]
    public async Task GivenTransactionalResponseHandler_WhenSent_ThenResponseIsReturnedUnchanged()
    {
        // Arrange
        var (host, _) = BuildHost(m => m.AddHandler<TransactionalResponseHandler>());

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var response = await mediator.SendAsync(new TransactionalResponseRequest("hello"));

            // Assert
            Assert.Equal("handled:hello", response);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    // ── Stream handlers get the same transaction treatment ──

    [Fact]
    public async Task GivenTransactionalStreamHandler_WhenEnumerated_ThenTransactionActiveInsideHandler()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalStreamHandler>());

        try
        {
            // Act
            await DrainAsync(host, new TransactionalStreamRequest(2));

            // Assert: the handler observes a transaction while producing, not merely while
            // being constructed.
            Assert.Equal(2, probe.Count);
            Assert.All(probe.Observations, o => Assert.NotNull(o.Transaction));
        }
        finally
        {
            await StopAsync(host);
        }
    }

    [Fact]
    public async Task GivenTransactionalStreamHandler_WhenEnumerationCompletes_ThenTransactionCommitted()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalStreamHandler>());

        try
        {
            // Act
            await DrainAsync(host, new TransactionalStreamRequest(2));

            // Assert: the commit happens after the last item, because the pipeline wraps the
            // whole enumeration rather than just its start.
            var transaction = probe.Last!.Transaction;
            Assert.NotNull(transaction);
            Assert.True(transaction.IsCommitted);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    [Fact]
    public async Task GivenTransactionalStreamHandler_WhenHandlerThrowsPartway_ThenTransactionNotCommitted()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalStreamHandler>());

        try
        {
            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DrainAsync(host, new TransactionalStreamRequest(2, ShouldThrow: true)));

            // Assert: items already delivered do not make the work durable.
            var transaction = probe.Last!.Transaction;
            Assert.NotNull(transaction);
            Assert.False(transaction.IsCommitted);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    [Fact]
    public async Task GivenPlainStreamHandler_WhenEnumerated_ThenNoTransactionInsideHandler()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<PlainStreamHandler>());

        try
        {
            // Act
            await DrainAsync(host, new PlainStreamRequest(2));

            // Assert
            Assert.Equal(2, probe.Count);
            Assert.All(probe.Observations, o => Assert.Null(o.Transaction));
        }
        finally
        {
            await StopAsync(host);
        }
    }

    [Fact]
    public async Task GivenTransactionalStreamHandler_WhenConsumerStopsEarly_ThenTransactionCommitted()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalStreamHandler>());

        try
        {
            // Act: take one item and stop.
            using (var scope = host.Services.CreateScope())
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                await foreach (var _ in mediator.CreateStreamAsync(new TransactionalStreamRequest(10)))
                {
                    break;
                }
            }

            // Assert: ending the enumeration is a normal end to the request, so the work the
            // handler already did is kept.
            var transaction = probe.Last!.Transaction;
            Assert.NotNull(transaction);
            Assert.True(transaction.IsCommitted);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    [Fact]
    public async Task GivenTransactionalStreamHandler_WhenCancelled_ThenTransactionNotCommitted()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalStreamHandler>());
        using var cts = new CancellationTokenSource();

        try
        {
            // Act
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                using var scope = host.Services.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                await foreach (var _ in mediator.CreateStreamAsync(
                    new TransactionalStreamRequest(10), cts.Token))
                {
                    await cts.CancelAsync();
                }
            });

            // Assert: cancelling aborts the request, unlike ending the enumeration.
            var transaction = probe.Last!.Transaction;
            Assert.NotNull(transaction);
            Assert.False(transaction.IsCommitted);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    /// <summary>Enumerates a stream request to completion through a fresh scope.</summary>
    protected static async Task DrainAsync<TResponse>(IHost host, IStreamRequest<TResponse> request)
    {
        using var scope = host.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await foreach (var _ in mediator.CreateStreamAsync(request))
        {
            // Draining is the point; the items themselves do not matter here.
        }
    }

    // ── Helpers ──

    /// <summary>
    /// Builds and starts a host with the persistence provider, the probe, and the supplied
    /// mediator handler registrations. Derived classes may use this to add provider-specific facts.
    /// </summary>
    /// <param name="configureMediator">Handler registrations for this test.</param>
    /// <param name="order">The order in which persistence and the mediator are registered.</param>
    /// <returns>The started host and the probe the handlers record into.</returns>
    protected (IHost Host, TransactionProbe Probe) BuildHost(
        Action<MediatorBuilder> configureMediator,
        EmitRegistrationOrder order = EmitRegistrationOrder.PersistenceFirst)
    {
        var probe = new TransactionProbe();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(probe);
                RegisterAmbientSessionInspector(services);

                services.AddEmit(emit =>
                {
                    // The outbox is only meaningful with a transport, so Kafka is registered
                    // through its real builder rather than shimmed. No topics are declared:
                    // these tests assert transaction scope, never delivery. Kafka always
                    // follows persistence, since that is the order a working application uses;
                    // the variable under test is where the mediator sits relative to them.
                    if (order == EmitRegistrationOrder.PersistenceFirst)
                    {
                        ConfigurePersistence(emit);
                        ConfigureTransport(emit);
                        emit.AddMediator(configureMediator);
                    }
                    else
                    {
                        emit.AddMediator(configureMediator);
                        ConfigurePersistence(emit);
                        ConfigureTransport(emit);
                    }
                });
            })
            .Build();

        host.StartAsync().GetAwaiter().GetResult();

        return (host, probe);
    }

    /// <summary>Sends a request that produces no response through a fresh scope.</summary>
    protected static async Task SendAsync(IHost host, IRequest request)
    {
        using var scope = host.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.SendAsync(request);
    }

    /// <summary>Sends a request that produces a response through a fresh scope.</summary>
    protected static async Task<TResponse> SendAsync<TResponse>(IHost host, IRequest<TResponse> request)
    {
        using var scope = host.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.SendAsync(request);
    }

    /// <summary>
    /// Registers a transport so the outbox has something that could drain it. No topics are
    /// declared: these tests assert transaction scope, never delivery.
    /// </summary>
    private void ConfigureTransport(EmitBuilder emit)
        => emit.AddKafka(kafka => kafka.ConfigureClient(config => config.BootstrapServers = BootstrapServers));

    private static async Task StopAsync(IHost host)
    {
        await host.StopAsync();
        host.Dispose();
    }
}

/// <summary>
/// Where the persistence provider is configured relative to the other Emit integrations
/// (the mediator, a transport) on the builder. Applications are free to call these in any
/// order, so every order must behave identically.
/// </summary>
public enum EmitRegistrationOrder
{
    /// <summary>Persistence is configured before the other integrations.</summary>
    PersistenceFirst,

    /// <summary>Persistence is configured after the other integrations.</summary>
    PersistenceLast,
}

// ── Requests ──

/// <summary>A request handled by a [Transactional] handler that returns a response.</summary>
public sealed record TransactionalResponseRequest(string Value, bool ShouldThrow = false) : IRequest<string>;

/// <summary>A request handled by a [Transactional] handler that returns no response.</summary>
public sealed record TransactionalVoidRequest(bool ShouldThrow = false) : IRequest;

/// <summary>A request handled by an undecorated handler that returns a response.</summary>
public sealed record PlainResponseRequest(string Value) : IRequest<string>;

/// <summary>A request handled by an undecorated handler that returns no response.</summary>
public sealed record PlainVoidRequest : IRequest;

// ── Handlers ──

/// <summary>
/// A [Transactional] handler returning a response. Records the ambient transaction it observes
/// so the test can assert the attribute took effect.
/// </summary>
[Transactional]
public sealed class TransactionalResponseHandler(
    IEmitContext emitContext,
    IAmbientSessionInspector sessionInspector,
    TransactionProbe probe) : IRequestHandler<TransactionalResponseRequest, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(
        TransactionalResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        probe.Record(emitContext.Transaction, sessionInspector.CurrentSession);

        return request.ShouldThrow
            ? throw new InvalidOperationException("Simulated handler failure")
            : Task.FromResult($"handled:{request.Value}");
    }
}

/// <summary>
/// A [Transactional] handler returning no response. Records the ambient transaction it observes.
/// </summary>
[Transactional]
public sealed class TransactionalVoidHandler(
    IEmitContext emitContext,
    IAmbientSessionInspector sessionInspector,
    TransactionProbe probe) : IRequestHandler<TransactionalVoidRequest>
{
    /// <inheritdoc />
    public Task HandleAsync(TransactionalVoidRequest request, CancellationToken cancellationToken = default)
    {
        probe.Record(emitContext.Transaction, sessionInspector.CurrentSession);

        return request.ShouldThrow
            ? throw new InvalidOperationException("Simulated handler failure")
            : Task.CompletedTask;
    }
}

/// <summary>
/// An undecorated handler returning a response. Records the ambient transaction so the test can
/// assert that none was started.
/// </summary>
public sealed class PlainResponseHandler(
    IEmitContext emitContext,
    IAmbientSessionInspector sessionInspector,
    TransactionProbe probe) : IRequestHandler<PlainResponseRequest, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(PlainResponseRequest request, CancellationToken cancellationToken = default)
    {
        probe.Record(emitContext.Transaction, sessionInspector.CurrentSession);
        return Task.FromResult($"handled:{request.Value}");
    }
}

/// <summary>
/// An undecorated handler returning no response. Records the ambient transaction so the test can
/// assert that none was started.
/// </summary>
public sealed class PlainVoidHandler(
    IEmitContext emitContext,
    IAmbientSessionInspector sessionInspector,
    TransactionProbe probe) : IRequestHandler<PlainVoidRequest>
{
    /// <inheritdoc />
    public Task HandleAsync(PlainVoidRequest request, CancellationToken cancellationToken = default)
    {
        probe.Record(emitContext.Transaction, sessionInspector.CurrentSession);
        return Task.CompletedTask;
    }
}

/// <summary>A request handled by a [Transactional] stream handler.</summary>
public sealed record TransactionalStreamRequest(int Count, bool ShouldThrow = false) : IStreamRequest<int>;

/// <summary>A request handled by an undecorated stream handler.</summary>
public sealed record PlainStreamRequest(int Count) : IStreamRequest<int>;

/// <summary>
/// A [Transactional] stream handler recording the ambient transaction it observes as it
/// produces each item, so the test can tell whether the transaction spans the enumeration.
/// </summary>
[Transactional]
public sealed class TransactionalStreamHandler(
    IEmitContext emitContext,
    IAmbientSessionInspector sessionInspector,
    TransactionProbe probe) : IStreamRequestHandler<TransactionalStreamRequest, int>
{
    /// <inheritdoc />
    public async IAsyncEnumerable<int> HandleAsync(
        TransactionalStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < request.Count; i++)
        {
            probe.Record(emitContext.Transaction, sessionInspector.CurrentSession);
            yield return i;
            await Task.Yield();
        }

        if (request.ShouldThrow)
        {
            throw new InvalidOperationException("Simulated handler failure");
        }
    }
}

/// <summary>An undecorated stream handler, which must not receive a transaction.</summary>
public sealed class PlainStreamHandler(
    IEmitContext emitContext,
    IAmbientSessionInspector sessionInspector,
    TransactionProbe probe) : IStreamRequestHandler<PlainStreamRequest, int>
{
    /// <inheritdoc />
    public async IAsyncEnumerable<int> HandleAsync(
        PlainStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < request.Count; i++)
        {
            probe.Record(emitContext.Transaction, sessionInspector.CurrentSession);
            yield return i;
            await Task.Yield();
        }
    }
}
