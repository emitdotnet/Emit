namespace Emit.Kafka.Consumer.Tests;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Consumer;
using global::Emit.Kafka.DependencyInjection;
using global::Emit.Kafka.Metrics;
using global::Emit.Kafka.Observability;
using global::Emit.Metrics;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Builds worker pools running real workers, for the tests of what stops, replaces and starts them.
/// </summary>
internal static class WorkerPoolTestKit
{
    /// <summary>The topic of the messages created.</summary>
    public const string Topic = "test-topic";

    /// <summary>The worker stop timeout of the supervisors created, unless given.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Creates a supervisor whose pools run one real worker handling every message with <paramref name="consumer"/>.
    /// </summary>
    public static WorkerPoolSupervisor<string, string> CreateSupervisor(
        BlockingConsumer consumer,
        Action<int>? onUnhandled = null,
        TimeSpan? workerStopTimeout = null,
        List<Task>? workerTasks = null)
    {
        var registration = new ConsumerGroupRegistration<string, string>
        {
            Name = "test-declaration",
            TopicName = Topic,
            GroupId = "test-group",
            DestinationAddress = new Uri($"kafka://broker:9092/{Topic}"),
            KeyDeserializer = ConfluentKafka.Deserializers.Utf8,
            ValueDeserializer = ConfluentKafka.Deserializers.Utf8,
            BuildConsumerPipelines = () => [new ConsumerPipelineEntry<string> { Identifier = nameof(BlockingConsumer), Kind = ConsumerKind.Direct, ConsumerType = typeof(BlockingConsumer), Pipeline = new HandlerInvoker<string>(typeof(BlockingConsumer)) }],
            WorkerCount = 1,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = 32,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = workerStopTimeout ?? StopTimeout,
            DeserializationRetryBackoff = Backoff.None,
            ApplyClientConfig = _ => { },
            ApplyConsumerConfigOverrides = _ => { },
        };

        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(BlockingConsumer))).Returns(consumer);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var observerInvoker = new KafkaConsumerObserverInvoker([], NullLogger<KafkaConsumerObserverInvoker>.Instance);
        var kafkaMetrics = new KafkaMetrics(null, new EmitMetricsEnrichment());

        return new WorkerPoolSupervisor<string, string>(
            registration,
            (offsetManager, cancellationToken) =>
            {
                var worker = new ConsumerWorker<string, string>(
                    "Worker[0]", registration, offsetManager, scopeFactory.Object, "test-group",
                    registration.DestinationAddress, observerInvoker, kafkaMetrics,
                    new EmitMetrics(null, new EmitMetricsEnrichment()), null, NullLogger.Instance);
                var running = worker.RunAsync(cancellationToken);
                workerTasks?.Add(running);
                return new WorkerPool<string, string>([worker], [running]);
            },
            kafkaMetrics,
            observerInvoker,
            NullLogger.Instance,
            onUnhandled);
    }

    /// <summary>Creates a committer committing through <paramref name="consumer"/>, or a mock; the caller disposes it.</summary>
    public static OffsetCommitter CreateCommitter(ConfluentKafka.IConsumer<byte[], byte[]>? consumer = null) => new(
        consumer ?? Mock.Of<ConfluentKafka.IConsumer<byte[], byte[]>>(),
        TimeSpan.FromMinutes(1),
        "test-group",
        new KafkaConsumerObserverInvoker([], NullLogger<KafkaConsumerObserverInvoker>.Instance),
        new KafkaMetrics(null, new EmitMetricsEnrichment()),
        NullLogger.Instance);

    /// <summary>Creates a message of the test topic.</summary>
    public static ConfluentKafka.ConsumeResult<byte[], byte[]> CreateConsumeResult(long offset) => new()
    {
        Topic = Topic,
        Partition = new ConfluentKafka.Partition(0),
        Offset = new ConfluentKafka.Offset(offset),
        Message = new ConfluentKafka.Message<byte[], byte[]>
        {
            Key = System.Text.Encoding.UTF8.GetBytes("k1"),
            Value = System.Text.Encoding.UTF8.GetBytes($"m{offset}"),
            Timestamp = new ConfluentKafka.Timestamp(DateTimeOffset.UtcNow),
        },
    };
}

/// <summary>
/// Consumer whose handlings block: until cancelled, or until released when it ignores cancellation. A callback can be
/// run on cancellation.
/// </summary>
internal sealed class BlockingConsumer(bool observesCancellation, Action? onCancelled = null) : IConsumer<string>
{
    public Task Started => started.Task;

    public int Handlings => Volatile.Read(ref handlings);

    public bool Cancelled { get; private set; }

    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int handlings;

    public void Release() => released.TrySetResult();

    public async Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref handlings);
        if (onCancelled is not null)
        {
            // Not disposed with the handling: the callbacks run last registered first, so the handling ends before it
            cancellationToken.Register(onCancelled);
        }

        started.TrySetResult();
        try
        {
            await released.Task.WaitAsync(observesCancellation ? cancellationToken : CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            Cancelled = true;
            throw;
        }
    }
}
