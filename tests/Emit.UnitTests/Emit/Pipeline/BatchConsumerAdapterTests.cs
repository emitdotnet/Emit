namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

public sealed class BatchConsumerAdapterTests
{
    private sealed class TestBatchConsumer : IBatchConsumer<string>
    {
        public ConsumeContext<MessageBatch<string>>? ReceivedContext { get; private set; }

        public Task ConsumeAsync(ConsumeContext<MessageBatch<string>> context, CancellationToken cancellationToken)
        {
            ReceivedContext = context;
            return Task.CompletedTask;
        }
    }

    private static ConsumeContext<MessageBatch<string>> CreateBatchContext(IServiceProvider services)
    {
        var batch = new MessageBatch<string>([]);
        return new ConsumeContext<MessageBatch<string>>
        {
            Message = batch,
            MessageId = "msg-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = services,
            TransportContext = TestTransportContext.Create(services),
        };
    }

    [Fact]
    public async Task Given_RegisteredBatchConsumer_When_ConsumeAsyncCalled_Then_ResolvesFromDIAndDelegates()
    {
        // Arrange
        var consumer = new TestBatchConsumer();
        var services = new ServiceCollection();
        services.AddSingleton<TestBatchConsumer>(consumer);
        var provider = services.BuildServiceProvider();

        var adapter = new BatchConsumerAdapter<string, TestBatchConsumer>();
        var context = CreateBatchContext(provider);

        // Act
        await adapter.ConsumeAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(consumer.ReceivedContext);
    }

    [Fact]
    public async Task Given_ConsumeContext_When_AdapterDelegates_Then_SameContextInstancePassedThrough()
    {
        // Arrange
        var consumer = new TestBatchConsumer();
        var services = new ServiceCollection();
        services.AddSingleton<TestBatchConsumer>(consumer);
        var provider = services.BuildServiceProvider();

        var adapter = new BatchConsumerAdapter<string, TestBatchConsumer>();
        var context = CreateBatchContext(provider);

        // Act
        await adapter.ConsumeAsync(context, CancellationToken.None);

        // Assert
        Assert.Same(context, consumer.ReceivedContext);
    }
}
