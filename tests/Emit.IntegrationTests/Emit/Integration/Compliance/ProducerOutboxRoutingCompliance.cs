namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Compliance tests for the producer's outbox-versus-direct routing decision. A producer with
/// no <c>UseDirect()</c> opt-out must enqueue through the transactional outbox, so a message
/// produced inside a transaction that is rolled back is never delivered.
/// </summary>
/// <remarks>
/// Rollback is the discriminator these tests are built on. Asserting only that a committed
/// message eventually arrives cannot distinguish outbox routing from direct delivery, because
/// both deliver. Only rollback separates them: an outbox-routed message disappears with the
/// transaction, while a direct-routed one has already reached the broker. That difference is
/// the atomicity guarantee the outbox exists to provide, so it is asserted directly rather
/// than inferred.
/// </remarks>
[Trait("Category", "Integration")]
public abstract class ProducerOutboxRoutingCompliance : IAsyncLifetime
{
    /// <summary>
    /// Gets the Kafka bootstrap servers address for producing and consuming messages.
    /// </summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Configures the persistence provider (MongoDB or EF Core) with the outbox enabled.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="pollingInterval">The outbox daemon polling interval.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit, TimeSpan pollingInterval);

    /// <summary>
    /// Hook for EF Core implementations to flush tracked changes before committing.
    /// MongoDB needs no equivalent.
    /// </summary>
    /// <param name="scopedServices">The scoped service provider for the active transaction.</param>
    protected virtual Task FlushBeforeCommitAsync(IServiceProvider scopedServices)
        => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    // ── Routing is outbox-backed, regardless of registration order ──

    [Fact]
    public Task GivenPersistenceRegisteredFirst_WhenTransactionRollsBack_ThenMessageNotDelivered()
        => AssertRolledBackMessageIsNotDeliveredAsync(EmitRegistrationOrder.PersistenceFirst);

    [Fact]
    public Task GivenTransportRegisteredFirst_WhenTransactionRollsBack_ThenMessageNotDelivered()
        => AssertRolledBackMessageIsNotDeliveredAsync(EmitRegistrationOrder.PersistenceLast);

    [Fact]
    public Task GivenPersistenceRegisteredFirst_WhenTransactionCommits_ThenMessageDelivered()
        => AssertCommittedMessageIsDeliveredAsync(EmitRegistrationOrder.PersistenceFirst);

    [Fact]
    public Task GivenTransportRegisteredFirst_WhenTransactionCommits_ThenMessageDelivered()
        => AssertCommittedMessageIsDeliveredAsync(EmitRegistrationOrder.PersistenceLast);

    // ── UseDirect() must keep opting out, in either order ──

    [Fact]
    public Task GivenPersistenceRegisteredFirst_WhenDirectProducerRollsBack_ThenMessageStillDelivered()
        => AssertDirectProducerIgnoresTransactionAsync(EmitRegistrationOrder.PersistenceFirst);

    [Fact]
    public Task GivenTransportRegisteredFirst_WhenDirectProducerRollsBack_ThenMessageStillDelivered()
        => AssertDirectProducerIgnoresTransactionAsync(EmitRegistrationOrder.PersistenceLast);

    // ── Shared bodies ──

    private async Task AssertRolledBackMessageIsNotDeliveredAsync(EmitRegistrationOrder order)
    {
        // Arrange
        var topic = $"test-route-rollback-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        var host = BuildHost(sink, topic, groupId, pollingInterval, order);
        await host.StartAsync();

        try
        {
            // Act — produce inside a transaction, then abandon it without committing.
            using (var scope = host.Services.CreateScope())
            {
                var sp = scope.ServiceProvider;
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var producer = sp.GetRequiredService<IEventProducer<string, string>>();

                await using var transaction = await unitOfWork.BeginAsync();
                await producer.ProduceAsync(new EventMessage<string, string>("k", "rolled-back"));
                await transaction.RollbackAsync();
            }

            // Act — then commit a second message on the same topic. Without this, an empty sink
            // would also be the result of a broken producer, consumer or daemon, and the test
            // would pass for the wrong reason.
            using (var scope = host.Services.CreateScope())
            {
                var sp = scope.ServiceProvider;
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var producer = sp.GetRequiredService<IEventProducer<string, string>>();

                await using var transaction = await unitOfWork.BeginAsync();
                await producer.ProduceAsync(new EventMessage<string, string>("k", "committed"));
                await FlushBeforeCommitAsync(sp);
                await transaction.CommitAsync();
            }

            // Assert — the committed message arrives, proving delivery works, and the rolled-back
            // one never does. It was produced first, so had it escaped it would have arrived first.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("committed", ctx.Message);

            await Task.Delay(pollingInterval * 3);
            Assert.Single(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private async Task AssertCommittedMessageIsDeliveredAsync(EmitRegistrationOrder order)
    {
        // Arrange
        var topic = $"test-route-commit-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        var host = BuildHost(sink, topic, groupId, pollingInterval, order);
        await host.StartAsync();

        try
        {
            // Act
            using (var scope = host.Services.CreateScope())
            {
                var sp = scope.ServiceProvider;
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var producer = sp.GetRequiredService<IEventProducer<string, string>>();

                await using var transaction = await unitOfWork.BeginAsync();
                await producer.ProduceAsync(new EventMessage<string, string>("k", "committed"));
                await FlushBeforeCommitAsync(sp);
                await transaction.CommitAsync();
            }

            // Assert — the outbox daemon delivers it.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("committed", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// A producer that opted out with <c>UseDirect()</c> must keep bypassing the outbox, so its
    /// message survives a rolled-back transaction. This guards the opt-out half of the routing
    /// decision, which would otherwise be untested once routing is resolved at request time.
    /// </summary>
    private async Task AssertDirectProducerIgnoresTransactionAsync(EmitRegistrationOrder order)
    {
        // Arrange
        var topic = $"test-route-direct-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(10); // Long, so any delivery proves it was direct.
        var directSink = new MessageSink<string>();

        var host = BuildHost(new MessageSink<string>(), topic, groupId, pollingInterval, order, directSink);
        await host.StartAsync();

        try
        {
            // Act — produce through the direct producer inside a transaction, then roll back.
            using (var scope = host.Services.CreateScope())
            {
                var sp = scope.ServiceProvider;
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var producer = sp.GetRequiredService<IEventProducer<string, string>>();

                await using var transaction = await unitOfWork.BeginAsync();
                await producer.ProduceAsync(new EventMessage<string, string>("k", "direct-msg"));
                await transaction.RollbackAsync();
            }

            // Assert — delivered anyway, well inside the polling interval, so it cannot have
            // travelled through the outbox daemon.
            var ctx = await directSink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("direct-msg", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private IHost BuildHost(
        MessageSink<string> sink,
        string topic,
        string groupId,
        TimeSpan pollingInterval,
        EmitRegistrationOrder order,
        MessageSink<string>? directSink = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                if (directSink is not null)
                {
                    services.AddSingleton(new DirectTopicSink(directSink));
                }

                services.AddEmit(emit =>
                {
                    // Registration order is the variable under test; both orders must behave
                    // identically, since an application is free to call these in either order.
                    if (order == EmitRegistrationOrder.PersistenceFirst)
                    {
                        ConfigurePersistence(emit, pollingInterval);
                        ConfigureTransport(emit, topic, groupId, directSink is not null);
                    }
                    else
                    {
                        ConfigureTransport(emit, topic, groupId, directSink is not null);
                        ConfigurePersistence(emit, pollingInterval);
                    }
                });
            })
            .Build();
    }

    private void ConfigureTransport(EmitBuilder emit, string topic, string groupId, bool useDirect)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config => config.BootstrapServers = BootstrapServers);
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.UseUtf8Serialization();

                // Without UseDirect() this producer must route through the outbox; with it, the
                // producer must keep bypassing the outbox entirely.
                if (useDirect)
                {
                    t.Producer(p => p.UseDirect());
                }
                else
                {
                    t.Producer();
                }

                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;

                    // Each branch consumes into whichever sink the test actually registered.
                    if (useDirect)
                    {
                        group.AddConsumer<DirectTopicSinkConsumer>();
                    }
                    else
                    {
                        group.AddConsumer<SinkConsumer<string>>();
                    }
                });
            });
        });
    }
}
