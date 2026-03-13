namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Models;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for the manual IUnitOfWork API (Tier 2). Verifies begin/commit/rollback
/// semantics when producing to the transactional outbox outside of the consumer pipeline.
/// </summary>
[Trait("Category", "Integration")]
public abstract class UnitOfWorkCompliance : IAsyncLifetime
{
    /// <summary>
    /// Configures Emit with a persistence provider (with outbox) and Kafka.
    /// A string,string topic with a producer and SinkConsumer consumer group must be registered.
    /// </summary>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan pollingInterval);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenUnitOfWork_WhenBeginAndCommit_ThenOutboxEntryDelivered()
    {
        // Arrange
        var topic = $"test-uow-commit-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var producer = sp.GetRequiredService<IEventProducer<string, string>>();

            await using var tx = await unitOfWork.BeginAsync();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "hello"), default)
                ;
            await FlushBeforeCommitAsync(sp);
            await tx.CommitAsync();

            // Assert
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("hello", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenUnitOfWork_WhenBeginAndRollback_ThenNoOutboxEntryDelivered()
    {
        // Arrange
        var topic = $"test-uow-rollback-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act — begin and rollback
            {
                using var scope = host.Services.CreateScope();
                var sp = scope.ServiceProvider;
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var producer = sp.GetRequiredService<IEventProducer<string, string>>();

                await using var tx = await unitOfWork.BeginAsync();
                await producer.ProduceAsync(new EventMessage<string, string>("k", "will-not-arrive"), default)
                    ;
                await tx.RollbackAsync();
            }

            // Wait for several polling cycles.
            await Task.Delay(pollingInterval * 3);

            // Produce a sentinel to confirm the consumer is alive.
            {
                using var scope = host.Services.CreateScope();
                var sp = scope.ServiceProvider;
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var producer = sp.GetRequiredService<IEventProducer<string, string>>();

                await using var tx = await unitOfWork.BeginAsync();
                await producer.ProduceAsync(new EventMessage<string, string>("k", "sentinel"), default)
                    ;
                await FlushBeforeCommitAsync(sp);
                await tx.CommitAsync();
            }

            // Assert — first message is the sentinel.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("sentinel", ctx.Message);
            await Task.Delay(pollingInterval * 2);
            Assert.Single(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenUnitOfWork_WhenBeginAndDispose_ThenAutoRollback()
    {
        // Arrange
        var topic = $"test-uow-dispose-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act — begin, produce, then dispose without commit or rollback.
            {
                using var scope = host.Services.CreateScope();
                var sp = scope.ServiceProvider;
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var producer = sp.GetRequiredService<IEventProducer<string, string>>();

                await using var tx = await unitOfWork.BeginAsync();
                await producer.ProduceAsync(new EventMessage<string, string>("k", "auto-rollback"), default)
                    ;
                // Dispose without commit → auto-rollback.
            }

            // Wait for several polling cycles.
            await Task.Delay(pollingInterval * 3);

            // Produce a sentinel.
            {
                using var scope = host.Services.CreateScope();
                var sp = scope.ServiceProvider;
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var producer = sp.GetRequiredService<IEventProducer<string, string>>();

                await using var tx = await unitOfWork.BeginAsync();
                await producer.ProduceAsync(new EventMessage<string, string>("k", "sentinel"), default)
                    ;
                await FlushBeforeCommitAsync(sp);
                await tx.CommitAsync();
            }

            // Assert
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("sentinel", ctx.Message);
            await Task.Delay(pollingInterval * 2);
            Assert.Single(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenUnitOfWork_WhenMultipleMessagesProducedAndCommitted_ThenAllDeliveredInOrder()
    {
        // Arrange
        const int messageCount = 5;
        var topic = $"test-uow-order-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act — produce each message in a separate transaction to get strictly
            // sequential outbox sequences (within a single SaveChangesAsync batch,
            // EF Core does not guarantee insertion order).
            for (var i = 0; i < messageCount; i++)
            {
                using var scope = host.Services.CreateScope();
                var sp = scope.ServiceProvider;
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var producer = sp.GetRequiredService<IEventProducer<string, string>>();

                await using var tx = await unitOfWork.BeginAsync();
                await producer.ProduceAsync(new EventMessage<string, string>("k", $"msg-{i}"), default);
                await FlushBeforeCommitAsync(sp);
                await tx.CommitAsync();
            }

            // Assert
            var received = new List<string>(messageCount);
            for (var i = 0; i < messageCount; i++)
            {
                var ctx = await sink.WaitForMessageAsync();
                received.Add(ctx.Message!);
            }

            for (var i = 0; i < messageCount; i++)
            {
                Assert.Equal($"msg-{i}", received[i]);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenUnitOfWork_WhenHandlerThrowsAfterProduce_ThenDisposedTransactionRollsBack()
    {
        // Arrange
        var topic = $"test-uow-throw-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act — begin, produce, throw, dispose via await using.
            try
            {
                using var scope = host.Services.CreateScope();
                var sp = scope.ServiceProvider;
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var producer = sp.GetRequiredService<IEventProducer<string, string>>();

                await using var tx = await unitOfWork.BeginAsync();
                await producer.ProduceAsync(new EventMessage<string, string>("k", "will-rollback"), default)
                    ;
                throw new InvalidOperationException("Simulated failure");
            }
            catch (InvalidOperationException)
            {
                // Expected.
            }

            // Wait for several polling cycles.
            await Task.Delay(pollingInterval * 3);

            // Produce a sentinel.
            {
                using var scope = host.Services.CreateScope();
                var sp = scope.ServiceProvider;
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var producer = sp.GetRequiredService<IEventProducer<string, string>>();

                await using var tx = await unitOfWork.BeginAsync();
                await producer.ProduceAsync(new EventMessage<string, string>("k", "sentinel"), default)
                    ;
                await FlushBeforeCommitAsync(sp);
                await tx.CommitAsync();
            }

            // Assert
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("sentinel", ctx.Message);
            await Task.Delay(pollingInterval * 2);
            Assert.Single(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Hook for EF Core implementations to call SaveChangesAsync before committing.
    /// MongoDB does not need this. Default implementation does nothing.
    /// </summary>
    protected virtual Task FlushBeforeCommitAsync(IServiceProvider scopedServices)
        => Task.CompletedTask;

    private IHost BuildHost(MessageSink<string> sink, string topic, string groupId, TimeSpan pollingInterval)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmit(emit, topic, groupId, pollingInterval));
            })
            .Build();
    }
}
