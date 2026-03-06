namespace Emit.Kafka.Tests.Consumer;

using System.Threading.Channels;
using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
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

public sealed class ConsumerWorkerTests
{
    [Fact]
    public void GivenNewWorker_WhenCreated_ThenChannelHasCorrectCapacity()
    {
        // Arrange
        var registration = CreateRegistration(bufferSize: 2);
        var worker = CreateWorker(registration: registration);
        var result1 = CreateConsumeResult();
        var result2 = CreateConsumeResult();
        var result3 = CreateConsumeResult();

        // Act
        var write1 = worker.Writer.TryWrite(result1);
        var write2 = worker.Writer.TryWrite(result2);
        var write3 = worker.Writer.TryWrite(result3);

        // Assert
        Assert.True(write1);
        Assert.True(write2);
        Assert.False(write3);
    }

    [Fact]
    public void GivenNewWorker_WhenCreated_ThenWriterIsNotNull()
    {
        // Arrange
        var worker = CreateWorker();

        // Act & Assert
        Assert.NotNull(worker.Writer);
    }

    [Fact]
    public void GivenComplete_WhenCalled_ThenChannelWriterIsCompleted()
    {
        // Arrange
        var worker = CreateWorker();
        var result = CreateConsumeResult();

        // Act
        worker.Complete();
        var writeResult = worker.Writer.TryWrite(result);

        // Assert
        Assert.False(writeResult);
    }

    [Fact]
    public async Task GivenRunAsync_WhenChannelCompleted_ThenRunAsyncCompletes()
    {
        // Arrange
        var worker = CreateWorker();
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        var runTask = Task.Run(() => worker.RunAsync(cts.Token));
        var delayTask = Task.Delay(TimeSpan.FromSeconds(5));
        var completedTask = await Task.WhenAny(runTask, delayTask);

        // Assert
        Assert.Same(runTask, completedTask);
    }

    [Fact]
    public async Task GivenRunAsync_WhenCancellationRequested_ThenRunAsyncExits()
    {
        // Arrange
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        // Act
        var runTask = Task.Run(() => worker.RunAsync(cts.Token));
        cts.Cancel();
        var delayTask = Task.Delay(TimeSpan.FromSeconds(5));
        var completedTask = await Task.WhenAny(runTask, delayTask);

        // Assert
        Assert.Same(runTask, completedTask);
    }

    [Fact]
    public async Task GivenRunAsync_WhenMessageProcessedSuccessfully_ThenReportsOffsetToManager()
    {
        // Arrange
        var mockOffsetManager = new Mock<OffsetManager>(CreateCommitter());
        var scopeFactory = CreateScopeFactory(typeof(TestConsumer), new TestConsumer());
        var worker = CreateWorker(offsetManager: mockOffsetManager.Object, scopeFactory: scopeFactory);
        var result = CreateConsumeResult();
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        mockOffsetManager.Verify(m => m.MarkAsProcessed(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<long>()), Times.Once);
    }

    [Fact]
    public async Task GivenRunAsync_WhenConsumerThrows_ThenOffsetNotReported()
    {
        // Arrange
        var registration = CreateRegistration(consumerTypes: [typeof(ThrowingConsumer)]);
        var mockOffsetManager = new Mock<OffsetManager>(CreateCommitter());
        var scopeFactory = CreateScopeFactory(typeof(ThrowingConsumer), new ThrowingConsumer());
        var worker = CreateWorker(registration: registration, offsetManager: mockOffsetManager.Object, scopeFactory: scopeFactory);
        var result = CreateConsumeResult();
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        mockOffsetManager.Verify(m => m.MarkAsProcessed(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task GivenDeserializeAsync_WhenSyncDeserializersConfigured_ThenDeserializesCorrectly()
    {
        // Arrange
        var consumer = new CapturingConsumer();
        var registration = CreateRegistration(consumerTypes: [typeof(CapturingConsumer)]);
        var scopeFactory = CreateScopeFactory(typeof(CapturingConsumer), consumer);
        var keyBytes = System.Text.Encoding.UTF8.GetBytes("test-key");
        var valueBytes = System.Text.Encoding.UTF8.GetBytes("test-value");
        var worker = CreateWorker(registration: registration, scopeFactory: scopeFactory);
        var result = CreateConsumeResult(key: keyBytes, value: valueBytes);
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        Assert.NotNull(consumer.CapturedContext);
        Assert.Equal("test-key", consumer.CapturedContext.Features.Get<IKeyFeature<string>>()!.Key);
        Assert.Equal("test-value", consumer.CapturedContext.Message);
    }

    [Fact]
    public async Task GivenDeserializeAsync_WhenAsyncDeserializersConfigured_ThenDeserializesCorrectly()
    {
        // Arrange
        var consumer = new CapturingConsumer();
        var registration = CreateRegistration(consumerTypes: [typeof(CapturingConsumer)]);
        var scopeFactory = CreateScopeFactory(typeof(CapturingConsumer), consumer);
        var worker = CreateWorker(registration: registration, scopeFactory: scopeFactory);
        var result = CreateConsumeResult();
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        Assert.NotNull(consumer.CapturedContext);
    }

    [Fact]
    public async Task GivenFanOut_WhenMultipleConsumers_ThenAllReceiveMessage()
    {
        // Arrange
        var registration = CreateRegistration(consumerTypes: [typeof(TestConsumer), typeof(TestConsumer), typeof(TestConsumer)]);
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(() => CreateScope(typeof(TestConsumer), new TestConsumer()));
        var worker = CreateWorker(registration: registration, scopeFactory: mockScopeFactory.Object);
        var result = CreateConsumeResult();
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        mockScopeFactory.Verify(f => f.CreateScope(), Times.Exactly(3));
    }

    [Fact]
    public async Task GivenFanOut_WhenMultipleConsumers_ThenCalledInRegistrationOrder()
    {
        // Arrange
        var callOrder = new List<string>();
        var consumerA = new OrderTrackingConsumer(callOrder, "A");
        var consumerB = new OrderTrackingConsumer(callOrder, "B");
        var consumerC = new OrderTrackingConsumer(callOrder, "C");
        var registration = CreateRegistration(consumerTypes: [typeof(OrderTrackingConsumer), typeof(OrderTrackingConsumer), typeof(OrderTrackingConsumer)]);
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.SetupSequence(f => f.CreateScope())
            .Returns(CreateScope(typeof(OrderTrackingConsumer), consumerA))
            .Returns(CreateScope(typeof(OrderTrackingConsumer), consumerB))
            .Returns(CreateScope(typeof(OrderTrackingConsumer), consumerC));
        var worker = CreateWorker(registration: registration, scopeFactory: mockScopeFactory.Object);
        var result = CreateConsumeResult();
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        Assert.Equal(["A", "B", "C"], callOrder);
    }

    [Fact]
    public async Task GivenFanOut_WhenSecondConsumerThrows_ThenFirstConsumerAlreadyCompleted()
    {
        // Arrange
        var consumer = new CapturingConsumer();
        var registration = CreateRegistration(consumerTypes: [typeof(CapturingConsumer), typeof(ThrowingConsumer)]);
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.SetupSequence(f => f.CreateScope())
            .Returns(CreateScope(typeof(CapturingConsumer), consumer))
            .Returns(CreateScope(typeof(ThrowingConsumer), new ThrowingConsumer()));
        var worker = CreateWorker(registration: registration, scopeFactory: mockScopeFactory.Object);
        var result = CreateConsumeResult();
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        Assert.NotNull(consumer.CapturedContext);
    }

    [Fact]
    public async Task GivenFanOut_WhenOneConsumerThrows_ThenOffsetNotReported()
    {
        // Arrange
        var registration = CreateRegistration(consumerTypes: [typeof(TestConsumer), typeof(ThrowingConsumer), typeof(TestConsumer)]);
        var mockOffsetManager = new Mock<OffsetManager>(CreateCommitter());
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.SetupSequence(f => f.CreateScope())
            .Returns(CreateScope(typeof(TestConsumer), new TestConsumer()))
            .Returns(CreateScope(typeof(ThrowingConsumer), new ThrowingConsumer()))
            .Returns(CreateScope(typeof(TestConsumer), new TestConsumer()));
        var worker = CreateWorker(registration: registration, offsetManager: mockOffsetManager.Object, scopeFactory: mockScopeFactory.Object);
        var result = CreateConsumeResult();
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        mockOffsetManager.Verify(m => m.MarkAsProcessed(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task GivenFanOut_WhenEachConsumer_ThenGetsOwnServiceScope()
    {
        // Arrange
        var registration = CreateRegistration(consumerTypes: [typeof(TestConsumer), typeof(TestConsumer)]);
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(() => CreateScope(typeof(TestConsumer), new TestConsumer()));
        var worker = CreateWorker(registration: registration, scopeFactory: mockScopeFactory.Object);
        var result = CreateConsumeResult();
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        mockScopeFactory.Verify(f => f.CreateScope(), Times.Exactly(2));
    }

    [Fact]
    public async Task GivenDeserializeAsync_WhenConsumeResultHasHeaders_ThenContextHasHeaders()
    {
        // Arrange
        var headers = new ConfluentKafka.Headers
        {
            { "key1", System.Text.Encoding.UTF8.GetBytes("value1") },
            { "key2", System.Text.Encoding.UTF8.GetBytes("value2") }
        };
        var consumer = new CapturingConsumer();
        var registration = CreateRegistration(consumerTypes: [typeof(CapturingConsumer)]);
        var scopeFactory = CreateScopeFactory(typeof(CapturingConsumer), consumer);
        var worker = CreateWorker(registration: registration, scopeFactory: scopeFactory);
        var result = CreateConsumeResult(headers: headers);
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        Assert.NotNull(consumer.CapturedContext);
        Assert.Equal(2, consumer.CapturedContext.Features.Get<IHeadersFeature>()!.Headers.Count);
    }

    [Fact]
    public async Task GivenDeserializeAsync_WhenConsumeResultHasNoHeaders_ThenContextHeadersEmpty()
    {
        // Arrange
        var consumer = new CapturingConsumer();
        var registration = CreateRegistration(consumerTypes: [typeof(CapturingConsumer)]);
        var scopeFactory = CreateScopeFactory(typeof(CapturingConsumer), consumer);
        var worker = CreateWorker(registration: registration, scopeFactory: scopeFactory);
        var result = CreateConsumeResult(headers: null);
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        Assert.NotNull(consumer.CapturedContext);
        Assert.Empty(consumer.CapturedContext.Features.Get<IHeadersFeature>()!.Headers);
    }

    [Fact]
    public async Task GivenDeserializeAsync_WhenConsumeResultHasTimestamp_ThenContextTimestampSet()
    {
        // Arrange
        var timestamp = new ConfluentKafka.Timestamp(DateTimeOffset.UtcNow);
        var consumer = new CapturingConsumer();
        var registration = CreateRegistration(consumerTypes: [typeof(CapturingConsumer)]);
        var scopeFactory = CreateScopeFactory(typeof(CapturingConsumer), consumer);
        var worker = CreateWorker(registration: registration, scopeFactory: scopeFactory);
        var result = CreateConsumeResult(timestamp: timestamp);
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        Assert.NotNull(consumer.CapturedContext);
        Assert.Equal(timestamp.UtcDateTime, consumer.CapturedContext.Timestamp.UtcDateTime);
    }

    [Fact]
    public async Task GivenFanOut_WhenMessageProcessed_ThenRawBytesFeaturesSetOnContext()
    {
        // Arrange
        var keyBytes = System.Text.Encoding.UTF8.GetBytes("raw-key");
        var valueBytes = System.Text.Encoding.UTF8.GetBytes("raw-value");
        IRawBytesFeature? capturedFeature = null;
        var registration = new ConsumerGroupRegistration<string, string>
        {
            TopicName = "test-topic",
            GroupId = "test-group",
            KeyDeserializer = ConfluentKafka.Deserializers.Utf8,
            ValueDeserializer = ConfluentKafka.Deserializers.Utf8,
            BuildConsumerPipelines = () =>
            [
                new ConsumerPipelineEntry<string>
                {
                    Identifier = "TestConsumer",
                    Kind = ConsumerKind.Direct,
                    ConsumerType = typeof(TestConsumer),
                    Pipeline = ctx =>
                    {
                        capturedFeature = ctx.Features.Get<IRawBytesFeature>();
                        return Task.CompletedTask;
                    }
                }
            ],
            WorkerCount = 1,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = 32,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = TimeSpan.FromSeconds(30),
            ApplyClientConfig = _ => { },
            ApplyConsumerConfigOverrides = _ => { },
            DeadLetterTopicMap = DeadLetterTopicMap.Empty,
        };
        var scopeFactory = CreateScopeFactory(typeof(TestConsumer), new TestConsumer());
        var worker = CreateWorker(registration: registration, scopeFactory: scopeFactory);
        var result = CreateConsumeResult(key: keyBytes, value: valueBytes);
        worker.Writer.TryWrite(result);
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        Assert.NotNull(capturedFeature);
        Assert.Equal(keyBytes, capturedFeature.RawKey);
        Assert.Equal(valueBytes, capturedFeature.RawValue);
    }

    [Fact]
    public async Task GivenSequentialSameKeyMessages_WhenMiddleMessageFails_ThenSubsequentOffsetNotCommitted()
    {
        // Arrange
        var consumer = new ThrowOnNthCallConsumer(throwOnCall: 2);
        var registration = CreateRegistration(consumerTypes: [typeof(ThrowOnNthCallConsumer)]);
        var mockKafkaConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = new OffsetCommitter(mockKafkaConsumer.Object, TimeSpan.FromMinutes(1), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);
        var offsetManager = new OffsetManager(committer);
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope())
            .Returns(() => CreateScope(typeof(ThrowOnNthCallConsumer), consumer));
        var worker = new ConsumerWorker<string, string>("Worker[0]", registration, offsetManager, mockScopeFactory.Object, "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), CreateEmitMetrics(), null, NullLogger.Instance);
        var key = System.Text.Encoding.UTF8.GetBytes("same-key");

        // Simulate ConsumerGroupWorker enqueuing offsets before dispatching
        offsetManager.Enqueue("test-topic", 0, 5);
        offsetManager.Enqueue("test-topic", 0, 6);
        offsetManager.Enqueue("test-topic", 0, 7);

        worker.Writer.TryWrite(CreateConsumeResult(offset: 5, key: key));
        worker.Writer.TryWrite(CreateConsumeResult(offset: 6, key: key));
        worker.Writer.TryWrite(CreateConsumeResult(offset: 7, key: key));
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);
        committer.Flush();

        // Assert - watermark stuck at 5 because offset 6 failed; committed offset = 5+1 = 6
        mockKafkaConsumer.Verify(c => c.Commit(It.Is<IEnumerable<ConfluentKafka.TopicPartitionOffset>>(
            offsets => offsets.Single().Offset.Value == 6)), Times.Once);
    }

    [Fact]
    public async Task GivenRunAsync_WhenCancellationDuringHandleAsync_ThenOffsetNotReported()
    {
        // Arrange
        var blockingConsumer = new BlockingConsumer();
        var registration = CreateRegistration(consumerTypes: [typeof(BlockingConsumer)]);
        var scopeFactory = CreateScopeFactory(typeof(BlockingConsumer), blockingConsumer);
        var mockOffsetManager = new Mock<OffsetManager>(CreateCommitter());
        var worker = CreateWorker(registration: registration, offsetManager: mockOffsetManager.Object, scopeFactory: scopeFactory);
        var result = CreateConsumeResult();
        worker.Writer.TryWrite(result);
        using var cts = new CancellationTokenSource();

        // Act
        var runTask = Task.Run(() => worker.RunAsync(cts.Token));
        await blockingConsumer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        worker.Complete();
        await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));

        // Assert
        mockOffsetManager.Verify(m => m.MarkAsProcessed(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<long>()), Times.Never);
    }

    // ── Deserialization error handling ──

    [Fact]
    public async Task GivenDeserializationError_WhenDiscardConfigured_ThenOffsetReportedAndMessageSkipped()
    {
        // Arrange
        var registration = CreateRegistrationWithFailingDeserializer(
            deserializationErrorAction: ErrorAction.Discard());
        var mockOffsetManager = new Mock<OffsetManager>(CreateCommitter());
        var worker = CreateWorker(registration: registration, offsetManager: mockOffsetManager.Object);
        worker.Writer.TryWrite(CreateConsumeResult());
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert — offset is reported despite deserialization failure
        mockOffsetManager.Verify(m => m.MarkAsProcessed("test-topic", 0, 0), Times.Once);
    }

    [Fact]
    public async Task GivenDeserializationError_WhenNoActionConfigured_ThenOffsetReportedAsDiscard()
    {
        // Arrange — no DeserializationErrorAction set (defaults to discard behavior)
        var registration = CreateRegistrationWithFailingDeserializer(
            deserializationErrorAction: null);
        var mockOffsetManager = new Mock<OffsetManager>(CreateCommitter());
        var worker = CreateWorker(registration: registration, offsetManager: mockOffsetManager.Object);
        worker.Writer.TryWrite(CreateConsumeResult());
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert — message is discarded and offset advances
        mockOffsetManager.Verify(m => m.MarkAsProcessed("test-topic", 0, 0), Times.Once);
    }

    [Fact]
    public async Task GivenDeserializationError_WhenDeadLetterWithExplicitTopic_ThenProducesToDlq()
    {
        // Arrange
        var mockSink = new Mock<IDeadLetterSink>();
        var registration = CreateRegistrationWithFailingDeserializer(
            deserializationErrorAction: ErrorAction.DeadLetter("errors.dlt"));
        var mockOffsetManager = new Mock<OffsetManager>(CreateCommitter());
        var worker = CreateWorker(registration: registration, offsetManager: mockOffsetManager.Object, deadLetterSink: mockSink.Object);
        worker.Writer.TryWrite(CreateConsumeResult());
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        mockSink.Verify(s => s.ProduceAsync(
            It.IsAny<byte[]?>(), It.IsAny<byte[]?>(),
            It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
            "errors.dlt",
            It.IsAny<CancellationToken>()), Times.Once);
        mockOffsetManager.Verify(m => m.MarkAsProcessed("test-topic", 0, 0), Times.Once);
    }

    [Fact]
    public async Task GivenDeserializationError_WhenDeadLetterWithConvention_ThenUsesConventionTopic()
    {
        // Arrange
        var mockSink = new Mock<IDeadLetterSink>();
        var registration = CreateRegistrationWithFailingDeserializer(
            deserializationErrorAction: ErrorAction.DeadLetter(),
            resolveDeadLetterTopic: topic => $"{topic}.dlt");
        var worker = CreateWorker(registration: registration, deadLetterSink: mockSink.Object);
        worker.Writer.TryWrite(CreateConsumeResult());
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        mockSink.Verify(s => s.ProduceAsync(
            It.IsAny<byte[]?>(), It.IsAny<byte[]?>(),
            It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
            "test-topic.dlt",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenDeserializationError_WhenDeadLetterButNoSink_ThenDiscardsAndReportsOffset()
    {
        // Arrange — dead letter configured but no IDeadLetterSink provided
        var registration = CreateRegistrationWithFailingDeserializer(
            deserializationErrorAction: ErrorAction.DeadLetter("errors.dlt"));
        var mockOffsetManager = new Mock<OffsetManager>(CreateCommitter());
        var worker = CreateWorker(registration: registration, offsetManager: mockOffsetManager.Object, deadLetterSink: null);
        worker.Writer.TryWrite(CreateConsumeResult());
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert — falls back to discard, offset still advances
        mockOffsetManager.Verify(m => m.MarkAsProcessed("test-topic", 0, 0), Times.Once);
    }

    [Fact]
    public async Task GivenDeserializationError_WhenDeadLetterButNoTopicResolvable_ThenDiscardsAndReportsOffset()
    {
        // Arrange — dead letter with no explicit topic and no convention
        var registration = CreateRegistrationWithFailingDeserializer(
            deserializationErrorAction: ErrorAction.DeadLetter());
        var mockSink = new Mock<IDeadLetterSink>();
        var mockOffsetManager = new Mock<OffsetManager>(CreateCommitter());
        var worker = CreateWorker(registration: registration, offsetManager: mockOffsetManager.Object, deadLetterSink: mockSink.Object);
        worker.Writer.TryWrite(CreateConsumeResult());
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert — cannot resolve topic, falls back to discard
        mockSink.Verify(s => s.ProduceAsync(
            It.IsAny<byte[]?>(), It.IsAny<byte[]?>(),
            It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        mockOffsetManager.Verify(m => m.MarkAsProcessed("test-topic", 0, 0), Times.Once);
    }

    [Fact]
    public async Task GivenDeserializationError_WhenDeadLettered_ThenHeadersContainDiagnosticInfo()
    {
        // Arrange
        IReadOnlyList<KeyValuePair<string, string>>? capturedHeaders = null;
        var mockSink = new Mock<IDeadLetterSink>();
        mockSink.Setup(s => s.ProduceAsync(It.IsAny<byte[]?>(), It.IsAny<byte[]?>(),
            It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<byte[]?, byte[]?, IReadOnlyList<KeyValuePair<string, string>>, string, CancellationToken>(
                (_, _, headers, _, _) => capturedHeaders = headers);
        var registration = CreateRegistrationWithFailingDeserializer(
            deserializationErrorAction: ErrorAction.DeadLetter("errors.dlt"));
        var worker = CreateWorker(registration: registration, deadLetterSink: mockSink.Object);
        worker.Writer.TryWrite(CreateConsumeResult());
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert
        Assert.NotNull(capturedHeaders);
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-exception-type");
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-exception-message");
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-consumer-group" && h.Value == "test-group");
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-source-topic" && h.Value == "test-topic");
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-source-partition" && h.Value == "0");
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-source-offset" && h.Value == "0");
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-timestamp");
    }

    [Fact]
    public async Task GivenDeserializationError_WhenDeadLettered_ThenOriginalHeadersPreserved()
    {
        // Arrange
        IReadOnlyList<KeyValuePair<string, string>>? capturedHeaders = null;
        var mockSink = new Mock<IDeadLetterSink>();
        mockSink.Setup(s => s.ProduceAsync(It.IsAny<byte[]?>(), It.IsAny<byte[]?>(),
            It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<byte[]?, byte[]?, IReadOnlyList<KeyValuePair<string, string>>, string, CancellationToken>(
                (_, _, headers, _, _) => capturedHeaders = headers);
        var registration = CreateRegistrationWithFailingDeserializer(
            deserializationErrorAction: ErrorAction.DeadLetter("errors.dlt"));
        var worker = CreateWorker(registration: registration, deadLetterSink: mockSink.Object);
        var originalHeaders = new ConfluentKafka.Headers
        {
            { "correlation-id", System.Text.Encoding.UTF8.GetBytes("abc-123") },
        };
        worker.Writer.TryWrite(CreateConsumeResult(headers: originalHeaders));
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert — original headers are preserved alongside diagnostic headers
        Assert.NotNull(capturedHeaders);
        Assert.Contains(capturedHeaders, h => h.Key == "correlation-id" && h.Value == "abc-123");
    }

    [Fact]
    public async Task GivenDeserializationError_WhenDlqProduceFails_ThenOffsetStillReportedAndMessageDiscarded()
    {
        // Arrange — DLQ produce throws, message should still be discarded
        var mockSink = new Mock<IDeadLetterSink>();
        mockSink.Setup(s => s.ProduceAsync(It.IsAny<byte[]?>(), It.IsAny<byte[]?>(),
            It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DLQ produce failed"));
        var registration = CreateRegistrationWithFailingDeserializer(
            deserializationErrorAction: ErrorAction.DeadLetter("errors.dlt"));
        var mockOffsetManager = new Mock<OffsetManager>(CreateCommitter());
        var worker = CreateWorker(registration: registration, offsetManager: mockOffsetManager.Object, deadLetterSink: mockSink.Object);
        worker.Writer.TryWrite(CreateConsumeResult());
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert — offset still advances even if DLQ produce fails
        mockOffsetManager.Verify(m => m.MarkAsProcessed("test-topic", 0, 0), Times.Once);
    }

    [Fact]
    public async Task GivenDeserializationError_WhenSubsequentMessagesValid_ThenSubsequentMessagesProcessed()
    {
        // Arrange — first message fails deserialization, second succeeds
        var consumer = new CapturingConsumer();
        var failingKeyDeserializer = new FailOnFirstCallDeserializer();
        var registration = new ConsumerGroupRegistration<string, string>
        {
            TopicName = "test-topic",
            GroupId = "test-group",
            KeyDeserializer = failingKeyDeserializer,
            ValueDeserializer = ConfluentKafka.Deserializers.Utf8,
            BuildConsumerPipelines = () => [new ConsumerPipelineEntry<string> { Identifier = "CapturingConsumer", Kind = ConsumerKind.Direct, ConsumerType = typeof(CapturingConsumer), Pipeline = new HandlerInvoker<string>(typeof(CapturingConsumer)).InvokeAsync }],
            WorkerCount = 1,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = 32,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = TimeSpan.FromSeconds(30),
            ApplyClientConfig = _ => { },
            ApplyConsumerConfigOverrides = _ => { },
            DeserializationErrorAction = ErrorAction.Discard(),
            DeadLetterTopicMap = DeadLetterTopicMap.Empty,
        };
        var scopeFactory = CreateScopeFactory(typeof(CapturingConsumer), consumer);
        var mockOffsetManager = new Mock<OffsetManager>(CreateCommitter());
        var worker = CreateWorker(registration: registration, offsetManager: mockOffsetManager.Object, scopeFactory: scopeFactory);
        worker.Writer.TryWrite(CreateConsumeResult(offset: 0));
        worker.Writer.TryWrite(CreateConsumeResult(offset: 1));
        worker.Complete();
        using var cts = new CancellationTokenSource();

        // Act
        await worker.RunAsync(cts.Token);

        // Assert — second message was processed successfully
        Assert.NotNull(consumer.CapturedContext);
        mockOffsetManager.Verify(m => m.MarkAsProcessed("test-topic", 0, 0), Times.Once);
        mockOffsetManager.Verify(m => m.MarkAsProcessed("test-topic", 0, 1), Times.Once);
    }

    private static ConsumerGroupRegistration<string, string> CreateRegistrationWithFailingDeserializer(
        ErrorAction? deserializationErrorAction = null,
        Func<string, string?>? resolveDeadLetterTopic = null)
    {
        return new ConsumerGroupRegistration<string, string>
        {
            TopicName = "test-topic",
            GroupId = "test-group",
            KeyDeserializer = new AlwaysThrowingDeserializer(),
            ValueDeserializer = ConfluentKafka.Deserializers.Utf8,
            BuildConsumerPipelines = () => [new ConsumerPipelineEntry<string> { Identifier = "TestConsumer", Kind = ConsumerKind.Direct, ConsumerType = typeof(TestConsumer), Pipeline = new HandlerInvoker<string>(typeof(TestConsumer)).InvokeAsync }],
            WorkerCount = 1,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = 32,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = TimeSpan.FromSeconds(30),
            ApplyClientConfig = _ => { },
            ApplyConsumerConfigOverrides = _ => { },
            DeserializationErrorAction = deserializationErrorAction,
            ResolveDeadLetterTopic = resolveDeadLetterTopic,
            DeadLetterTopicMap = DeadLetterTopicMap.Empty,
        };
    }

    private static ConsumerGroupRegistration<string, string> CreateRegistration(
        int bufferSize = 32,
        Type[]? consumerTypes = null)
    {
        var types = consumerTypes ?? [typeof(TestConsumer)];
        return new ConsumerGroupRegistration<string, string>
        {
            TopicName = "test-topic",
            GroupId = "test-group",
            KeyDeserializer = ConfluentKafka.Deserializers.Utf8,
            ValueDeserializer = ConfluentKafka.Deserializers.Utf8,
            BuildConsumerPipelines = () => types.Select(t => new ConsumerPipelineEntry<string> { Identifier = t.Name, Kind = ConsumerKind.Direct, ConsumerType = t, Pipeline = new HandlerInvoker<string>(t).InvokeAsync }).ToArray(),
            WorkerCount = 1,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = bufferSize,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = TimeSpan.FromSeconds(30),
            ApplyClientConfig = _ => { },
            ApplyConsumerConfigOverrides = _ => { },
            DeadLetterTopicMap = DeadLetterTopicMap.Empty,
        };
    }

    private static KafkaConsumerObserverInvoker CreateObserverInvoker()
    {
        return new KafkaConsumerObserverInvoker([], NullLogger<KafkaConsumerObserverInvoker>.Instance);
    }

    private static KafkaMetrics CreateKafkaMetrics() => new(null, new EmitMetricsEnrichment());

    private static EmitMetrics CreateEmitMetrics() => new(null, new EmitMetricsEnrichment());

    private static OffsetCommitter CreateCommitter()
    {
        return new OffsetCommitter(
            Mock.Of<ConfluentKafka.IConsumer<byte[], byte[]>>(),
            TimeSpan.FromMinutes(1),
            "test-group",
            CreateObserverInvoker(),
            CreateKafkaMetrics(),
            NullLogger.Instance);
    }

    private static ConsumerWorker<string, string> CreateWorker(
        ConsumerGroupRegistration<string, string>? registration = null,
        OffsetManager? offsetManager = null,
        IServiceScopeFactory? scopeFactory = null,
        IDeadLetterSink? deadLetterSink = null)
    {
        registration ??= CreateRegistration();
        var committer = CreateCommitter();
        offsetManager ??= new OffsetManager(committer);
        scopeFactory ??= Mock.Of<IServiceScopeFactory>();
        return new ConsumerWorker<string, string>("Worker[0]", registration, offsetManager, scopeFactory, "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), CreateEmitMetrics(), deadLetterSink, NullLogger.Instance);
    }

    private static ConfluentKafka.ConsumeResult<byte[], byte[]> CreateConsumeResult(
        string topic = "test-topic",
        int partition = 0,
        long offset = 0,
        byte[]? key = null,
        byte[]? value = null,
        ConfluentKafka.Headers? headers = null,
        ConfluentKafka.Timestamp? timestamp = null)
    {
        return new ConfluentKafka.ConsumeResult<byte[], byte[]>
        {
            Topic = topic,
            Partition = new ConfluentKafka.Partition(partition),
            Offset = new ConfluentKafka.Offset(offset),
            Message = new ConfluentKafka.Message<byte[], byte[]>
            {
                Key = key ?? System.Text.Encoding.UTF8.GetBytes("test-key"),
                Value = value ?? System.Text.Encoding.UTF8.GetBytes("test-value"),
                Headers = headers,
                Timestamp = timestamp ?? new ConfluentKafka.Timestamp(DateTimeOffset.UtcNow),
            },
        };
    }

    private static IServiceScope CreateScope(Type serviceType, object instance)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockProvider = new Mock<IServiceProvider>();
        mockProvider.Setup(p => p.GetService(serviceType)).Returns(instance);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockProvider.Object);
        return mockScope.Object;
    }

    private static IServiceScopeFactory CreateScopeFactory(Type consumerType, object instance)
    {
        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope()).Returns(CreateScope(consumerType, instance));
        return mockFactory.Object;
    }

    private sealed class CapturingConsumer : IConsumer<string>
    {
        public InboundContext<string>? CapturedContext { get; private set; }

        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
        {
            CapturedContext = context;
            return Task.CompletedTask;
        }
    }

    private sealed class TestConsumer : IConsumer<string>
    {
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ThrowingConsumer : IConsumer<string>
    {
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Test failure");
    }

    private sealed class OrderTrackingConsumer : IConsumer<string>
    {
        private readonly List<string> callOrder;
        private readonly string name;

        public OrderTrackingConsumer(List<string> callOrder, string name)
        {
            this.callOrder = callOrder;
            this.name = name;
        }

        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
        {
            callOrder.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowOnNthCallConsumer(int throwOnCall) : IConsumer<string>
    {
        private int callCount;

        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref callCount) == throwOnCall)
            {
                throw new InvalidOperationException("Planned failure");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BlockingConsumer : IConsumer<string>
    {
        public TaskCompletionSource Started { get; } = new();

        public async Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class AlwaysThrowingDeserializer : ConfluentKafka.IDeserializer<string>
    {
        public string Deserialize(ReadOnlySpan<byte> data, bool isNull, ConfluentKafka.SerializationContext context)
            => throw new FormatException("Simulated deserialization failure");
    }

    private sealed class FailOnFirstCallDeserializer : ConfluentKafka.IDeserializer<string>
    {
        private int callCount;

        public string Deserialize(ReadOnlySpan<byte> data, bool isNull, ConfluentKafka.SerializationContext context)
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                throw new FormatException("Simulated deserialization failure on first call");
            }

            return System.Text.Encoding.UTF8.GetString(data);
        }
    }
}
