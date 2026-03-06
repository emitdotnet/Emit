namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Observability;
using Emit.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for <see cref="IProduceObserver"/>. Derived classes configure a
/// <c>string, string</c> topic with a registered observer. Two scenarios are covered:
/// both producing callbacks fire on success, and the error callback fires on failure.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ProduceObserverCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer.
    /// The <see cref="TrackingProduceObserver"/> must be registered as an <see cref="IProduceObserver"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    protected abstract void ConfigureWithProduceObserver(EmitBuilder emit, string topic);

    /// <summary>
    /// Verifies that when a message is produced successfully, both <c>OnProducingAsync</c>
    /// and <c>OnProducedAsync</c> are called.
    /// </summary>
    [Fact]
    public async Task GivenProduceObserver_WhenMessageProduced_ThenOnProducingAndOnProducedCallbacksFire()
    {
        // Arrange
        var topic = $"test-produce-obs-{Guid.NewGuid():N}";
        var observer = new TrackingProduceObserver();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TrackingProduceObserver>(observer);
                services.AddEmit(emit =>
                {
                    emit.Services.AddSingleton<IProduceObserver>(
                        sp => sp.GetRequiredService<TrackingProduceObserver>());
                    ConfigureWithProduceObserver(emit, topic);
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "hello-produce"));

            // Assert — wait briefly for async observer invocations to complete, then check counts.
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.True(observer.ProducingCount > 0,
                $"Expected OnProducingAsync to be called at least once but count was {observer.ProducingCount}.");
            Assert.True(observer.ProducedCount > 0,
                $"Expected OnProducedAsync to be called at least once but count was {observer.ProducedCount}.");
            Assert.Equal(0, observer.ErrorCount);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// <see cref="IProduceObserver"/> implementation that tracks invocation counts for
    /// each observer callback.
    /// </summary>
    public sealed class TrackingProduceObserver : IProduceObserver
    {
        private int producingCount;
        private int producedCount;
        private int errorCount;

        /// <summary>Gets the number of times <see cref="IProduceObserver.OnProducingAsync{T}"/> was called.</summary>
        public int ProducingCount => Volatile.Read(ref producingCount);

        /// <summary>Gets the number of times <see cref="IProduceObserver.OnProducedAsync{T}"/> was called.</summary>
        public int ProducedCount => Volatile.Read(ref producedCount);

        /// <summary>Gets the number of times <see cref="IProduceObserver.OnProduceErrorAsync{T}"/> was called.</summary>
        public int ErrorCount => Volatile.Read(ref errorCount);

        /// <inheritdoc />
        public Task OnProducingAsync<T>(OutboundContext<T> context)
        {
            Interlocked.Increment(ref producingCount);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnProducedAsync<T>(OutboundContext<T> context)
        {
            Interlocked.Increment(ref producedCount);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnProduceErrorAsync<T>(OutboundContext<T> context, Exception exception)
        {
            Interlocked.Increment(ref errorCount);
            return Task.CompletedTask;
        }
    }
}
