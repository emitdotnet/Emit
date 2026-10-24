namespace Emit.Kafka.Tests;

using System.Collections.Concurrent;
using System.Diagnostics;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Tracing;
using global::Emit.Kafka;
using global::Emit.Kafka.DependencyInjection;
using global::Emit.Kafka.Metrics;
using global::Emit.Models;
using global::Emit.Tracing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaOutboxProviderTests
{
    private static readonly TimeSpan TransactionTimeout = TimeSpan.FromSeconds(1);

    public enum Ending
    {
        Committed,
        NotCompleted,
        CommitStillPending,
    }

    private readonly OutboxDeliveryContext run = new(Guid.NewGuid(), Generation: 3);
    private readonly List<(string TransactionalId, Mock<ConfluentKafka.IProducer<byte[], byte[]>> Producer)> producers = [];

    [Fact]
    public async Task GivenRunsOfTheOutboxDaemon_WhenTheirDeliveriesBegin_ThenEachRunInitializesItsOwnProducerOnceWithTheDaemonsTransactionalId()
    {
        // Arrange: initializing a producer fences every earlier producer with the same transactional id
        using var provider = CreateProvider();
        var nextRun = run with { Generation = run.Generation + 1 };

        // Act
        await using (var delivery = await provider.BeginDeliveryAsync(run))
        {
            await delivery.CompleteAsync();
        }

        await using (var delivery = await provider.BeginDeliveryAsync(run))
        {
            await delivery.CompleteAsync();
        }

        await using (var delivery = await provider.BeginDeliveryAsync(nextRun))
        {
            await delivery.CompleteAsync();
        }

        // Assert
        Assert.Equal(2, producers.Count);
        Assert.All(producers, p => Assert.Equal(KafkaOutboxProvider.TransactionalIdOf(run.FencingKey), p.TransactionalId));
        producers[0].Producer.Verify(p => p.InitTransactions(TransactionTimeout), Times.Once);
        producers[0].Producer.Verify(p => p.BeginTransaction(), Times.Exactly(2));
        producers[0].Producer.Verify(p => p.Dispose(), Times.Once);
        producers[1].Producer.Verify(p => p.InitTransactions(TransactionTimeout), Times.Once);
    }

    [Theory]
    [InlineData(Ending.Committed)]
    [InlineData(Ending.NotCompleted)]
    [InlineData(Ending.CommitStillPending)]
    public async Task GivenDelivery_WhenDisposed_ThenNothingIsWaitedForAndOnlyTheProducerOfACommittedOneIsKept(Ending ending)
    {
        // Arrange: a commit or an abort can take up to the transaction timeout, which a stopped daemon cannot wait
        // for; the next producer's initialization aborts whatever an abandoned one left open
        using var commitReturns = new ManualResetEventSlim(ending != Ending.CommitStillPending);
        var disposed = new TaskCompletionSource();
        using var provider = CreateProvider(producer =>
        {
            producer.Setup(p => p.CommitTransaction(It.IsAny<TimeSpan>())).Callback(() => commitReturns.Wait());
            producer.Setup(p => p.Dispose()).Callback(disposed.SetResult);
        });
        using var drainTimeout = new CancellationTokenSource();

        async Task DeliverTwiceAsync()
        {
            await using (var delivery = await provider.BeginDeliveryAsync(run))
            {
                await delivery.SendAsync(CreateValidEntry());
                if (ending == Ending.Committed)
                {
                    await delivery.CompleteAsync();
                }
                else if (ending == Ending.CommitStillPending)
                {
                    var completing = delivery.CompleteAsync(drainTimeout.Token);
                    await drainTimeout.CancelAsync();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completing);
                }
            }

            await using var next = await provider.BeginDeliveryAsync(run);
            await next.CompleteAsync();
        }

        // Act
        await DeliverTwiceAsync().WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        var first = producers[0].Producer;
        first.Verify(p => p.AbortTransaction(It.IsAny<TimeSpan>()), Times.Never);
        Assert.Equal(ending == Ending.Committed ? 1 : 2, producers.Count);
        Assert.Equal(ending == Ending.NotCompleted, disposed.Task.IsCompleted);
        commitReturns.Set();
        if (ending != Ending.Committed)
        {
            await disposed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task GivenProducerFencedByALaterRun_WhenCompleting_ThenTheDeliveryFailsAndTheNextOneInitializesANewProducer()
    {
        // Arrange: the fenced producer cannot be used again; whether this node may still deliver is the outbox
        // daemon's to decide, from its node's lease, before it begins the next delivery
        using var provider = CreateProvider(producer => producer
            .Setup(p => p.CommitTransaction(It.IsAny<TimeSpan>()))
            .Throws(new ConfluentKafka.KafkaException(new ConfluentKafka.Error(ConfluentKafka.ErrorCode.Local_Fenced, "Fenced", isFatal: true))));

        // Act
        await using (var delivery = await provider.BeginDeliveryAsync(run))
        {
            await Assert.ThrowsAsync<ConfluentKafka.KafkaException>(() => delivery.CompleteAsync());
        }

        await using (await provider.BeginDeliveryAsync(run))
        {
        }

        // Assert
        Assert.Equal(2, producers.Count);
        producers[0].Producer.Verify(p => p.AbortTransaction(It.IsAny<TimeSpan>()), Times.Never);
        producers[0].Producer.Verify(p => p.Dispose(), Times.Once);
        producers[1].Producer.Verify(p => p.InitTransactions(TransactionTimeout), Times.Once);
    }

    [Theory]
    [InlineData(ConfluentKafka.ErrorCode.GroupLoadInProgress, true)]
    [InlineData(ConfluentKafka.ErrorCode.GroupCoordinatorNotAvailable, true)]
    [InlineData(ConfluentKafka.ErrorCode.Local_TimedOut, false)]
    public async Task GivenProducerWhoseInitializationFailsOnce_WhenADeliveryBegins_ThenItIsInitializedAgainOnlyIfTheBrokerAskedForThat(
        ConfluentKafka.ErrorCode error,
        bool initializedAgain)
    {
        // Arrange: a broker whose transaction coordinator is still loading, as after it restarts, asks the client to
        // try again; a node that cannot reach the broker in time fails its run, so another node can deliver
        var attempts = 0;
        using var provider = CreateProvider(producer => producer
            .Setup(p => p.InitTransactions(TransactionTimeout))
            .Callback(() =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new ConfluentKafka.KafkaRetriableException(new ConfluentKafka.Error(error));
                }
            }));

        // Act
        var exception = await Record.ExceptionAsync(async () => await (await provider.BeginDeliveryAsync(run)).DisposeAsync());

        // Assert
        Assert.Equal(initializedAgain, exception is null);
        Assert.Equal(initializedAgain ? 2 : 1, attempts);
    }

    [Fact]
    public async Task GivenEntriesSent_WhenTheirMessagesAreInFlight_ThenEachWasHandedToTheProducerInCallOrderAndOnlyTheCommitWaitsForThem()
    {
        // Arrange: the outbox hands a group's entries over one after the other, and the producer keeps their order;
        // committing the transaction waits for every message in it
        List<byte[]> produced = [];
        using var provider = CreateProvider(producer => producer
            .Setup(p => p.Produce(It.IsAny<string>(), It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(), It.IsAny<Action<ConfluentKafka.DeliveryReport<byte[], byte[]>>>()))
            .Callback((string _, ConfluentKafka.Message<byte[], byte[]> message, Action<ConfluentKafka.DeliveryReport<byte[], byte[]>> _) =>
                produced.Add(message.Value!)));
        await using var delivery = await provider.BeginDeliveryAsync(run);
        var first = CreateValidEntry();
        var second = CreateValidEntry();
        second.Body = [7, 8, 9];

        // Act
        await delivery.SendAsync(first).WaitAsync(TimeSpan.FromSeconds(30));
        await delivery.SendAsync(second).WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal([first.Body!, second.Body!], produced);
        Assert.Single(producers).Producer.Verify(p => p.CommitTransaction(It.IsAny<TimeSpan>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenEntryThatCannotBeSent_WhenSending_ThenNothingIsProduced(bool daemonStopped)
    {
        // Arrange: a stopped daemon starts no new send, and an entry without a topic cannot be sent
        using var provider = CreateProvider();
        await using var delivery = await provider.BeginDeliveryAsync(run);
        var entry = CreateValidEntry();
        if (!daemonStopped)
        {
            entry.Destination = "kafka://localhost:9092";
        }

        // Act
        var exception = await Record.ExceptionAsync(() => delivery.SendAsync(entry, new CancellationToken(canceled: daemonStopped)));

        // Assert
        Assert.IsType(daemonStopped ? typeof(OperationCanceledException) : typeof(InvalidOperationException), exception);
        Assert.Single(producers).Producer.Verify(
            p => p.Produce(It.IsAny<string>(), It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(), It.IsAny<Action<ConfluentKafka.DeliveryReport<byte[], byte[]>>>()),
            Times.Never);
    }

    [Theory]
    [InlineData(ConfluentKafka.ErrorCode.Local_MsgTimedOut, true)]
    [InlineData(ConfluentKafka.ErrorCode.Local_Transport, true)]
    [InlineData(ConfluentKafka.ErrorCode.Local_AllBrokersDown, true)]
    [InlineData(ConfluentKafka.ErrorCode.MsgSizeTooLarge, false)]
    [InlineData(ConfluentKafka.ErrorCode.TopicAuthorizationFailed, false)]
    public async Task GivenMessageThatCouldNotBeSent_WhenTheDeliveryCompletes_ThenItIsNotCommittedItsSendSpanEndsInErrorAndOnlyAFailureToReachTheBrokerIsReportedAsSuch(
        ConfluentKafka.ErrorCode error,
        bool brokerUnreachable)
    {
        // Arrange: a message that times out was retried by the client until the broker stayed out of reach, which
        // another node may not be; a broker that refuses the entry refuses it from every node
        var topic = $"test-topic-{Guid.NewGuid():N}";
        var sends = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EmitActivitySourceNames.Producer,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if ((string?)activity.GetTagItem("messaging.destination.name") == topic)
                {
                    sends.Enqueue(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var provider = CreateProvider(producer => producer
            .Setup(p => p.Produce(It.IsAny<string>(), It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(), It.IsAny<Action<ConfluentKafka.DeliveryReport<byte[], byte[]>>>()))
            .Callback((string _, ConfluentKafka.Message<byte[], byte[]> _, Action<ConfluentKafka.DeliveryReport<byte[], byte[]>> report) =>
                report(new ConfluentKafka.DeliveryReport<byte[], byte[]> { Error = new ConfluentKafka.Error(error) })));
        await using var delivery = await provider.BeginDeliveryAsync(run);
        await delivery.SendAsync(KafkaBuilder.CreateOutboxEntry(
            $"kafka://localhost:9092/{topic}", topic, typeof(byte[]), typeof(byte[]), [1, 2, 3], [4, 5, 6], headers: null));

        // Act
        var exception = await Record.ExceptionAsync(() => delivery.CompleteAsync());

        // Assert
        Assert.Equal(brokerUnreachable, exception is MessageBrokerUnreachableException);
        Assert.IsType<ConfluentKafka.KafkaException>(brokerUnreachable ? exception!.InnerException : exception);
        Assert.Single(producers).Producer.Verify(p => p.CommitTransaction(It.IsAny<TimeSpan>()), Times.Never);
        var send = Assert.Single(sends);
        Assert.Equal(ActivityStatusCode.Error, send.Status);
        Assert.Equal(error.ToString(), send.GetTagItem("error.type"));
    }

    private KafkaOutboxProvider CreateProvider(Action<Mock<ConfluentKafka.IProducer<byte[], byte[]>>>? setup = null) =>
        new(
            transactionalId =>
            {
                var producer = new Mock<ConfluentKafka.IProducer<byte[], byte[]>>();
                producer
                    .Setup(p => p.Produce(It.IsAny<string>(), It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(), It.IsAny<Action<ConfluentKafka.DeliveryReport<byte[], byte[]>>>()))
                    .Callback((string _, ConfluentKafka.Message<byte[], byte[]> _, Action<ConfluentKafka.DeliveryReport<byte[], byte[]>> report) =>
                        report(new ConfluentKafka.DeliveryReport<byte[], byte[]> { Error = new ConfluentKafka.Error(ConfluentKafka.ErrorCode.NoError) }));
                if (producers.Count == 0)
                {
                    setup?.Invoke(producer);
                }

                producers.Add((transactionalId, producer));
                return producer.Object;
            },
            TransactionTimeout,
            new KafkaMetrics(null, new EmitMetricsEnrichment()),
            Mock.Of<INodeIdentity>(n => n.NodeId == Guid.NewGuid()),
            new EmitTracingOptions(),
            NullLogger<KafkaOutboxProvider>.Instance);

    private static OutboxEntry CreateValidEntry() =>
        KafkaBuilder.CreateOutboxEntry(
            "kafka://localhost:9092/test-topic", "test-topic", typeof(byte[]), typeof(byte[]), [1, 2, 3], [4, 5, 6], headers: null);
}
