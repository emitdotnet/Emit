namespace Emit.IntegrationTests.Integration.Compliance;

using System.Diagnostics;
using System.Text;
using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Tracing;
using Emit.Configuration;
using Emit.DependencyInjection;
using Emit.Kafka.Consumer;
using Emit.Kafka.DependencyInjection;
using Emit.Mediator;
using Emit.Mediator.DependencyInjection;
using Emit.Testing;
using Emit.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Compliance tests for tracing: the spans Emit starts for each path a message takes, how they form one trace, and
/// their attributes, following the OpenTelemetry messaging conventions. A message's <c>create</c> span is its
/// creation context: its <c>send</c> span and every <c>process</c> span are its children, a batch's process span links
/// to the creation context of each of its messages, and a dead letter's <c>send</c> span becomes the creation context
/// of the dead letter. Derived classes configure a persistence provider with the outbox enabled; Kafka is configured
/// by the base class.
/// </summary>
[Trait("Category", "Integration")]
public abstract class TracingCompliance : IAsyncLifetime
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a test waits before asserting that no span was started: every span of a message ends right after
    /// its handler returns.
    /// </summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the Kafka bootstrap servers address.
    /// </summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Configures the persistence provider with the outbox enabled.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="configureOutbox">Configures the outbox the provider enables.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit, Action<OutboxOptions> configureOutbox);

    /// <summary>
    /// Hook for providers that must save tracked changes before committing. Does nothing by default.
    /// </summary>
    protected virtual Task FlushBeforeCommitAsync(IServiceProvider scopedServices) => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task GivenMessageProducedWithinATrace_WhenProcessedByAConsumerAndARouter_ThenItsSpansFormThatTrace(
        bool throughOutbox,
        bool tracingEnabled)
    {
        // Arrange
        using var recorder = new ActivityRecorder();
        var topic = NewTopic();
        var sink = new MessageSink<string>();
        using var host = BuildHost(
            services => services.AddSingleton(sink),
            kafka => kafka.Topic<string, string>(topic, t =>
            {
                t.UseUtf8Serialization();
                ConfigureProducer(t, throughOutbox);
                t.ConsumerGroup(NewGroup(), group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                    group.AddRouter<string>("router", _ => "route", routes => routes.Route<RoutedSinkConsumer>("route"));
                });
            }),
            tracing => tracing.Enabled = tracingEnabled);
        await host.StartAsync();

        try
        {
            // Act
            var root = new Activity("test").Start();
            root.SetBaggage("tenant", "acme");
            await ProduceAsync(host, topic, new EventMessage<string, string>("order-1", "placed"), throughOutbox);
            root.Stop();
            var received = await ReceiveAsync(sink, 2);

            // Assert: both deliveries carry the message id the producer assigned
            Assert.All(received, r => Assert.Equal(r.Headers.GetFirstValue(WellKnownHeaders.MessageId), r.MessageId));
            if (!tracingEnabled)
            {
                await Task.Delay(SettleTime);
                Assert.Empty(recorder.OfDestination(topic));
                return;
            }

            var create = Assert.Single(await recorder.WaitForAsync($"create {topic}"));
            Assert.Equal(EmitActivitySourceNames.Producer, create.Source.Name);
            Assert.Equal(ActivityKind.Producer, create.Kind);
            Assert.Equal(root.SpanId, create.ParentSpanId);
            AssertMessagingTags(create, "create", topic);
            Assert.Equal(received[0].MessageId, create.GetTagItem("messaging.message.id"));
            Assert.Equal("order-1", create.GetTagItem("messaging.kafka.message.key"));

            var send = Assert.Single(await recorder.WaitForAsync($"send {topic}"));
            Assert.Equal(EmitActivitySourceNames.Producer, send.Source.Name);
            Assert.Equal(ActivityKind.Client, send.Kind);
            Assert.Equal(create.SpanId, send.ParentSpanId);
            AssertMessagingTags(send, "send", topic);
            Assert.Equal(received[0].MessageId, send.GetTagItem("messaging.message.id"));
            Assert.NotNull(send.GetTagItem("messaging.destination.partition.id"));
            Assert.NotNull(send.GetTagItem("messaging.kafka.offset"));

            var processes = await recorder.WaitForAsync($"process {topic}", 2);
            Assert.Equal(2, processes.Count);
            Assert.All(processes, process =>
            {
                Assert.Equal(EmitActivitySourceNames.Consumer, process.Source.Name);
                Assert.Equal(ActivityKind.Consumer, process.Kind);
                Assert.Equal(create.SpanId, process.ParentSpanId);
                Assert.Contains(process.Links, link => link.Context.SpanId == create.SpanId);
                AssertMessagingTags(process, "process", topic);
                Assert.Equal(received[0].MessageId, process.GetTagItem("messaging.message.id"));
                Assert.Equal("order-1", process.GetTagItem("messaging.kafka.message.key"));
                Assert.NotNull(process.GetTagItem("messaging.consumer.group.name"));
                Assert.Equal(send.GetTagItem("messaging.destination.partition.id"), process.GetTagItem("messaging.destination.partition.id"));
                Assert.Equal(send.GetTagItem("messaging.kafka.offset"), process.GetTagItem("messaging.kafka.offset"));
                Assert.Equal("acme", process.GetBaggageItem("tenant"));
                Assert.Equal(ActivityStatusCode.Unset, process.Status);
            });
            var routed = Assert.Single(processes, p => p.GetTagItem("emit.route.key") is not null);
            Assert.Equal("route", routed.GetTagItem("emit.route.key"));
            Assert.Equal(nameof(RoutedSinkConsumer), routed.GetTagItem("emit.consumer.type"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task GivenMessageProducedOutsideAnyTrace_WhenProcessed_ThenItStartsATraceOnlyWhenRootActivitiesAreAllowed(
        bool producedByEmit,
        bool createRootActivities)
    {
        // Arrange
        using var recorder = new ActivityRecorder();
        var topic = NewTopic();
        var sink = new MessageSink<string>();
        using var host = BuildHost(
            services => services.AddSingleton(sink),
            kafka => kafka.Topic<string, string>(topic, t =>
            {
                t.UseUtf8Serialization();
                t.Producer(p => p.UseDirect());
                t.ConsumerGroup(NewGroup(), group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            }),
            tracing => tracing.CreateRootActivities = createRootActivities);
        await host.StartAsync();

        try
        {
            // Act: no span is current, and a message produced outside Emit has no trace context
            if (producedByEmit)
            {
                await ProduceAsync(host, topic, new EventMessage<string, string>("order-1", "placed"), throughOutbox: false);
            }
            else
            {
                await ProduceRawAsync(topic, Encoding.UTF8.GetBytes("order-1"), Encoding.UTF8.GetBytes("placed"));
            }

            await ReceiveAsync(sink, 1);

            // Assert
            if (!createRootActivities)
            {
                await Task.Delay(SettleTime);
                Assert.Empty(recorder.OfDestination(topic));
                return;
            }

            var process = Assert.Single(await recorder.WaitForAsync($"process {topic}"));
            AssertMessagingTags(process, "process", topic);
            Assert.Equal("order-1", process.GetTagItem("messaging.kafka.message.key"));
            if (producedByEmit)
            {
                var create = Assert.Single(await recorder.WaitForAsync($"create {topic}"));
                Assert.Equal(default, create.ParentSpanId);
                Assert.Equal(create.SpanId, process.ParentSpanId);
            }
            else
            {
                Assert.Equal(default, process.ParentSpanId);
                Assert.Empty(process.Links);
            }
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task GivenBatchOfMessagesFromTwoProducers_WhenProcessed_ThenItsSpanLinksToEachCreateSpan(
        bool createRootActivities,
        bool producedWithinTraces)
    {
        // Arrange
        using var recorder = new ActivityRecorder();
        var topic = NewTopic();
        var sink = new BatchSinkConsumer<string>();
        using var host = BuildHost(
            services => services.AddSingleton(sink),
            kafka => kafka.Topic<string, string>(topic, t =>
            {
                t.UseUtf8Serialization();
                t.Producer(p => p.UseDirect());
                t.ConsumerGroup(NewGroup(), group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddBatchConsumer<BatchSinkConsumer<string>>(batch =>
                    {
                        batch.MaxSize = 2;
                        batch.Timeout = TimeSpan.FromSeconds(10);
                    });
                });
            }),
            tracing => tracing.CreateRootActivities = createRootActivities);
        await host.StartAsync();

        try
        {
            // Act: two messages, each from its own trace, or from none
            List<ActivityTraceId> traces = [];
            foreach (var value in new[] { "first", "second" })
            {
                if (producedWithinTraces)
                {
                    var root = new Activity("test").Start();
                    traces.Add(root.TraceId);
                    await ProduceAsync(host, topic, new EventMessage<string, string>("order-1", value), throughOutbox: false);
                    root.Stop();
                }
                else
                {
                    await ProduceRawAsync(topic, Encoding.UTF8.GetBytes("order-1"), Encoding.UTF8.GetBytes(value));
                }
            }

            await TestHelpers.WaitUntilAsync(() => sink.Messages.Count >= 2, "Expected the batch of 2 messages.", ReceiveTimeout);

            // Assert
            Assert.Equal(2, Assert.Single(sink.ReceivedBatches).Count);
            if (!producedWithinTraces)
            {
                await Task.Delay(SettleTime);
                Assert.Empty(recorder.Named($"process {topic}"));
                return;
            }

            var process = Assert.Single(await recorder.WaitForAsync($"process {topic}"));
            AssertMessagingTags(process, "process", topic);
            Assert.Equal(ActivityKind.Consumer, process.Kind);
            Assert.Equal(default, process.ParentSpanId);
            var creates = recorder.Named($"create {topic}");
            Assert.Equal(
                creates.Select(c => c.SpanId.ToHexString()).Order(),
                process.Links.Select(link => link.Context.SpanId.ToHexString()).Order());
            Assert.Equal(
                traces.Select(t => t.ToHexString()).Order(),
                process.Links.Select(link => link.Context.TraceId.ToHexString()).Order());
            Assert.Equal(2, process.GetTagItem("messaging.batch.message_count"));
            Assert.NotNull(process.GetTagItem("messaging.consumer.group.name"));
            Assert.Null(process.GetTagItem("messaging.message.id"));
            Assert.Null(process.GetTagItem("messaging.kafka.offset"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenHandlerThatFailsTwiceThenSucceeds_WhenRetried_ThenEachFailedAttemptIsAnErrorAndTheProcessSpanIsNot()
    {
        // Arrange
        using var recorder = new ActivityRecorder();
        var topic = NewTopic();
        var sink = new MessageSink<string>();
        using var host = BuildHost(
            services => services.AddSingleton(sink).AddSingleton(new InvocationCounter()),
            kafka => kafka.Topic<string, string>(topic, t =>
            {
                t.UseUtf8Serialization();
                t.Producer(p => p.UseDirect());
                t.ConsumerGroup(NewGroup(), group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.OnError(e => e.Default(d => d.Retry(3, Backoff.None).Discard()));
                    group.AddConsumer<FailsTwiceConsumer>();
                });
            }));
        await host.StartAsync();

        try
        {
            // Act
            var root = new Activity("test").Start();
            await ProduceAsync(host, topic, new EventMessage<string, string>("order-1", "placed"), throughOutbox: false);
            root.Stop();
            await ReceiveAsync(sink, 1);

            // Assert: the first attempt runs in the process span; the two retries have their own
            var process = Assert.Single(await recorder.WaitForAsync($"process {topic}"));
            AssertMessagingTags(process, "process", topic);
            Assert.Equal(ActivityStatusCode.Unset, process.Status);
            var retries = recorder.ChildrenOf(process).Where(a => a.DisplayName == "emit.retry").OrderBy(a => a.StartTimeUtc).ToList();
            Assert.Equal(2, retries.Count);
            Assert.All(retries, retry =>
            {
                Assert.Equal(ActivityKind.Internal, retry.Kind);
                AssertTagsSetOnce(retry);
            });
            Assert.Equal([1, 2], retries.Select(r => (int)r.GetTagItem("emit.retry.attempt")!));
            Assert.Equal(ActivityStatusCode.Error, retries[0].Status);
            Assert.Equal(typeof(InvalidOperationException).FullName, retries[0].GetTagItem("error.type"));
            Assert.Equal(ActivityStatusCode.Unset, retries[1].Status);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenMessageThatFails_WhenDeadLettered_ThenTheDeadLetterContinuesTheTraceOfTheProcessingThatFailed(
        bool rejectedByValidation)
    {
        // Arrange
        using var recorder = new ActivityRecorder();
        var topic = NewTopic();
        var deadLetterTopic = NewTopic();
        using var host = BuildHost(
            services => services.AddSingleton(new MessageSink<byte[]>()),
            kafka =>
            {
                kafka.DeadLetter(deadLetterTopic, t => t.ConsumerGroup(NewGroup(), group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<DlqCaptureConsumer>();
                }));
                kafka.Topic<string, string>(topic, t =>
                {
                    t.UseUtf8Serialization();
                    t.Producer(p => p.UseDirect());
                    t.ConsumerGroup(NewGroup(), group =>
                    {
                        group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                        group.OnError(e => e.Default(d => d.DeadLetter()));
                        if (rejectedByValidation)
                        {
                            group.Validate(_ => MessageValidationResult.Fail("invalid"), a => a.DeadLetter());
                        }

                        group.AddConsumer<AlwaysFailingConsumer>();
                    });
                });
            });
        await host.StartAsync();

        try
        {
            // Act
            var root = new Activity("test").Start();
            await ProduceAsync(host, topic, new EventMessage<string, string>("order-1", "placed"), throughOutbox: false);
            root.Stop();

            // Assert: a rejection is a decided outcome, a handler that failed is an error
            var process = Assert.Single(await recorder.WaitForAsync($"process {topic}"));
            AssertMessagingTags(process, "process", topic);
            if (rejectedByValidation)
            {
                Assert.Equal(ActivityStatusCode.Unset, process.Status);
            }
            else
            {
                Assert.Equal(ActivityStatusCode.Error, process.Status);
                Assert.Equal(typeof(InvalidOperationException).FullName, process.GetTagItem("error.type"));
            }

            var send = Assert.Single(await recorder.WaitForAsync($"send {deadLetterTopic}"));
            Assert.Equal(EmitActivitySourceNames.Consumer, send.Source.Name);
            Assert.Equal(ActivityKind.Producer, send.Kind);
            Assert.Equal(process.SpanId, send.ParentSpanId);
            AssertMessagingTags(send, "send", deadLetterTopic);

            var deadLetterProcess = Assert.Single(await recorder.WaitForAsync($"process {deadLetterTopic}"));
            AssertMessagingTags(deadLetterProcess, "process", deadLetterTopic);
            Assert.Equal(send.SpanId, deadLetterProcess.ParentSpanId);
            Assert.Equal(root.TraceId, deadLetterProcess.TraceId);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenMessageThatCannotBeDeserialized_WhenDeadLettered_ThenTheDeadLetterSendIsAChildOfItsCreationContext()
    {
        // Arrange: a key of 5 bytes, which the Int32 deserializer rejects
        using var recorder = new ActivityRecorder();
        var topic = NewTopic();
        var deadLetterTopic = NewTopic();
        using var host = BuildHost(
            services => services.AddSingleton(new MessageSink<byte[]>()),
            kafka =>
            {
                kafka.DeadLetter(deadLetterTopic, t => t.ConsumerGroup(NewGroup(), group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<DlqCaptureConsumer>();
                }));
                kafka.Topic<int, string>(topic, t =>
                {
                    t.SetInt32KeyDeserializer();
                    t.SetUtf8ValueDeserializer();
                    t.ConsumerGroup(NewGroup(), group =>
                    {
                        group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                        group.OnDeserializationError(e => e.DeadLetter());
                        group.AddConsumer<IntKeyConsumer>();
                    });
                });
            });
        await host.StartAsync();

        try
        {
            // Act: nothing processes the message, so its creation context is all there is to join
            var creation = new Activity("producer").Start();
            creation.Stop();
            await ProduceRawAsync(topic, [1, 2, 3, 4, 5], Encoding.UTF8.GetBytes("placed"), [new(WellKnownHeaders.TraceParent, creation.Id!)]);

            // Assert
            var send = Assert.Single(await recorder.WaitForAsync($"send {deadLetterTopic}"));
            AssertMessagingTags(send, "send", deadLetterTopic);
            Assert.Equal(creation.TraceId, send.TraceId);
            Assert.Equal(creation.SpanId, send.ParentSpanId);
            Assert.Empty(recorder.Named($"process {topic}"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenMediatorRequestThatProduces_WhenHandled_ThenTheMessageIsCreatedWithinTheRequestSpan(bool handlerFails)
    {
        // Arrange
        using var recorder = new ActivityRecorder();
        var topic = NewTopic();
        using var host = BuildHost(
            _ => { },
            kafka => kafka.Topic<string, string>(topic, t =>
            {
                t.UseUtf8Serialization();
                t.Producer(p => p.UseDirect());
            }),
            configureMediator: mediator => mediator.AddHandler<ProducingRequestHandler>());
        await host.StartAsync();

        try
        {
            // Act
            var root = new Activity("test").Start();
            using (var scope = host.Services.CreateScope())
            {
                var send = scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new ProducingRequest(handlerFails));
                await (handlerFails ? Assert.ThrowsAsync<InvalidOperationException>(() => send) : send);
            }

            root.Stop();

            // Assert
            var request = Assert.Single(await recorder.WaitForAsync(nameof(ProducingRequest)));
            Assert.Equal(EmitActivitySourceNames.Mediator, request.Source.Name);
            Assert.Equal(ActivityKind.Internal, request.Kind);
            AssertTagsSetOnce(request);
            Assert.Equal(root.SpanId, request.ParentSpanId);
            Assert.Equal(handlerFails ? ActivityStatusCode.Error : ActivityStatusCode.Unset, request.Status);
            var create = Assert.Single(await recorder.WaitForAsync($"create {topic}"));
            Assert.Equal(request.SpanId, create.ParentSpanId);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    // ── Consumers and handlers ──

    /// <summary>
    /// The route consumer of the router: writes to the sink like <see cref="SinkConsumer{TMessage}"/>.
    /// </summary>
    public sealed class RoutedSinkConsumer(MessageSink<string> sink) : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) =>
            sink.WriteAsync(context, cancellationToken);
    }

    /// <summary>
    /// Fails the first two times it is called, then writes to the sink.
    /// </summary>
    public sealed class FailsTwiceConsumer(MessageSink<string> sink, InvocationCounter calls) : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) =>
            calls.Increment() <= 2
                ? throw new InvalidOperationException("Simulated failure.")
                : sink.WriteAsync(context, cancellationToken);
    }

    /// <summary>
    /// The consumer of a topic with Int32 keys, which the tests never reach.
    /// </summary>
    public sealed class IntKeyConsumer : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// A mediator request whose handler produces a message, then fails when <paramref name="Fail"/> is set.
    /// </summary>
    public sealed record ProducingRequest(bool Fail) : IRequest;

    /// <summary>
    /// Produces a message, then fails when the request asks for it.
    /// </summary>
    public sealed class ProducingRequestHandler(IEventProducer<string, string> producer) : IRequestHandler<ProducingRequest>
    {
        public async Task HandleAsync(ProducingRequest request, CancellationToken cancellationToken = default)
        {
            await producer.ProduceAsync(new EventMessage<string, string>("order-1", "placed"), cancellationToken);
            if (request.Fail)
            {
                throw new InvalidOperationException("Simulated failure.");
            }
        }
    }

    // ── Helpers ──

    private static string NewTopic() => $"test-tracing-{Guid.NewGuid():N}";

    private static string NewGroup() => $"group-{Guid.NewGuid():N}";

    private static void ConfigureProducer(KafkaTopicBuilder<string, string> topic, bool throughOutbox)
    {
        if (throughOutbox)
        {
            topic.Producer();
        }
        else
        {
            topic.Producer(p => p.UseDirect());
        }
    }

    /// <summary>
    /// Asserts the attributes every messaging span has, given at its creation, each set once.
    /// </summary>
    private static void AssertMessagingTags(Activity activity, string operation, string destination)
    {
        AssertTagsSetOnce(activity);
        Assert.Equal("kafka", activity.GetTagItem("messaging.system"));
        Assert.Equal(operation, activity.GetTagItem("messaging.operation.name"));
        Assert.Equal(operation, activity.GetTagItem("messaging.operation.type"));
        Assert.Equal(destination, activity.GetTagItem("messaging.destination.name"));
        Assert.NotNull(activity.GetTagItem("server.address"));
    }

    /// <summary>
    /// Asserts that no attribute of <paramref name="activity"/> is set more than once.
    /// </summary>
    private static void AssertTagsSetOnce(Activity activity)
    {
        var keys = activity.TagObjects.Select(tag => tag.Key).ToList();
        Assert.Equal(keys.Distinct().Order(), keys.Order());
    }

    private IHost BuildHost(
        Action<IServiceCollection> configureServices,
        Action<KafkaBuilder> configureKafka,
        Action<EmitTracingOptions>? configureTracing = null,
        Action<MediatorBuilder>? configureMediator = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                configureServices(services);
                services.AddEmit(emit =>
                {
                    ConfigurePersistence(emit, outbox => outbox.PollingInterval = TimeSpan.FromSeconds(1));
                    if (configureTracing is not null)
                    {
                        emit.ConfigureTracing(tracing => tracing.Configure(configureTracing));
                    }

                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config => config.BootstrapServers = BootstrapServers);
                        kafka.AutoProvision();
                        configureKafka(kafka);
                    });

                    if (configureMediator is not null)
                    {
                        emit.AddMediator(configureMediator);
                    }
                });
            })
            .Build();
    }

    /// <summary>
    /// Produces a message through the host's producer, in a committed unit of work when it goes through the outbox.
    /// </summary>
    private async Task ProduceAsync(IHost host, string topic, EventMessage<string, string> message, bool throughOutbox)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var producer = services.GetRequiredService<IEventProducer<string, string>>();
        if (!throughOutbox)
        {
            await producer.ProduceAsync(message);
            return;
        }

        await using var transaction = await services.GetRequiredService<IUnitOfWork>().BeginAsync();
        await producer.ProduceAsync(message);
        await FlushBeforeCommitAsync(services);
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Produces a record with Kafka's own client, as an application not using Emit would.
    /// </summary>
    private async Task ProduceRawAsync(string topic, byte[] key, byte[] value, IReadOnlyList<KeyValuePair<string, string>>? headers = null)
    {
        using var producer = new ConfluentKafka.ProducerBuilder<byte[], byte[]>(
            new ConfluentKafka.ProducerConfig { BootstrapServers = BootstrapServers }).Build();
        var kafkaHeaders = new ConfluentKafka.Headers();
        foreach (var (headerKey, headerValue) in headers ?? [])
        {
            kafkaHeaders.Add(headerKey, Encoding.UTF8.GetBytes(headerValue));
        }

        await producer.ProduceAsync(topic, new ConfluentKafka.Message<byte[], byte[]> { Key = key, Value = value, Headers = kafkaHeaders });
    }

    private static async Task<List<ConsumeContext<string>>> ReceiveAsync(MessageSink<string> sink, int count)
    {
        List<ConsumeContext<string>> received = [];
        for (var i = 0; i < count; i++)
        {
            received.Add(await sink.WaitForMessageAsync(ReceiveTimeout));
        }

        return received;
    }
}
