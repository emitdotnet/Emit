namespace Emit.Kafka.Tests.Consumer;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Kafka;
using global::Emit.Kafka.Consumer;
using global::Emit.Kafka.DependencyInjection;
using global::Emit.Kafka.Metrics;
using global::Emit.Kafka.Observability;
using global::Emit.Metrics;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class ConsumerGroupWorkerTests
{
    private static KafkaConsumerObserverInvoker CreateObserverInvoker()
    {
        return new KafkaConsumerObserverInvoker([], NullLogger<KafkaConsumerObserverInvoker>.Instance);
    }

    private static KafkaMetrics CreateKafkaMetrics() => new(null, new EmitMetricsEnrichment());

    private static KafkaBrokerMetrics CreateKafkaBrokerMetrics() => new(null, new EmitMetricsEnrichment());

    private static EmitMetrics CreateEmitMetrics() => new(null, new EmitMetricsEnrichment());

    [Fact]
    public void GivenConstructor_WhenCreated_ThenCreatesLoggerForCorrectType()
    {
        // Arrange
        var registration = CreateRegistration();
        var mockScopeFactory = Mock.Of<IServiceScopeFactory>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

        // Act
        var flowControl = new KafkaConsumerFlowControl(NullLogger<KafkaConsumerFlowControl>.Instance);
        var worker = new ConsumerGroupWorker<string, string>(
            registration,
            flowControl,
            circuitBreakerObserver: null,
            mockScopeFactory,
            mockLoggerFactory.Object,
            CreateObserverInvoker(),
            CreateKafkaMetrics(),
            CreateKafkaBrokerMetrics(),
            CreateEmitMetrics(),
            null);

        // Assert
        mockLoggerFactory.Verify(f => f.CreateLogger(It.Is<string>(s => s.Contains("ConsumerGroupWorker"))), Times.Once);
    }

    private static ConsumerGroupRegistration<string, string> CreateRegistration()
    {
        return new ConsumerGroupRegistration<string, string>
        {
            TopicName = "test-topic",
            GroupId = "test-group",
            DestinationAddress = new Uri("kafka://broker:9092/kafka/test-topic"),
            BuildConsumerPipelines = () => [new ConsumerPipelineEntry<string> { Identifier = "TestConsumer", Kind = ConsumerKind.Direct, ConsumerType = typeof(TestConsumer), Pipeline = new HandlerInvoker<string>(typeof(TestConsumer)) }],
            WorkerCount = 1,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = 32,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = TimeSpan.FromSeconds(30),
            ApplyClientConfig = _ => { },
            ApplyConsumerConfigOverrides = _ => { },
        };
    }

    private sealed class TestConsumer : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
