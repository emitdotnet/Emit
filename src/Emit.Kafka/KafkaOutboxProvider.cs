namespace Emit.Kafka;

using System.Diagnostics;
using System.Text;
using Emit.Abstractions;
using Emit.Kafka.Metrics;
using Emit.Kafka.Tracing;
using Emit.Models;
using Emit.Tracing;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka implementation of <see cref="IOutboxProvider"/>: each delivery is a Kafka transaction.
/// </summary>
/// <remarks>
/// <para>
/// The provider reads the message body, headers, and properties directly from the <see cref="OutboxEntry"/>,
/// and sends with a transactional producer of its own, whose transactional id is derived from the outbox
/// daemon's fencing key. A run's first delivery initializes its transactions, which fences any earlier run of
/// the daemon in Kafka: its open transaction is aborted and nothing it sends afterwards becomes visible. Consumers
/// that read committed messages, the default of the Kafka client Emit uses, only ever see the entries of the current run.
/// </para>
/// <para>
/// Sends are not cancelled with the daemon: cancelling would only stop the wait while the producer keeps sending.
/// The producer's message timeout, and the time given to start or commit a transaction, are at most the daemon
/// drain timeout. A delivery that did not
/// commit is never waited for: its producer is replaced, and the next one's initialization aborts whatever it
/// left open, so a stopped daemon ends within its drain timeout.
/// </para>
/// </remarks>
internal sealed class KafkaOutboxProvider : IOutboxProvider, IDisposable
{
    /// <summary>
    /// Errors of a send that could not reach the broker: the client retried it until its message timeout, or lost
    /// every broker. Another node may reach them; an error the broker returns for the entry it would return to any.
    /// </summary>
    private static readonly HashSet<ConfluentKafka.ErrorCode> BrokerUnreachable =
    [
        ConfluentKafka.ErrorCode.Local_MsgTimedOut,
        ConfluentKafka.ErrorCode.Local_Transport,
        ConfluentKafka.ErrorCode.Local_AllBrokersDown,
    ];


    private readonly Func<string, ConfluentKafka.IProducer<byte[], byte[]>> createProducer;
    private readonly TimeSpan transactionTimeout;
    private readonly KafkaMetrics kafkaMetrics;
    private readonly INodeIdentity nodeIdentity;
    private readonly EmitTracingOptions tracingOptions;
    private readonly ILogger<KafkaOutboxProvider> logger;
    private readonly SemaphoreSlim oneDeliveryAtATime = new(1, 1);
    private TransactionalProducer? current;

    public KafkaOutboxProvider(
        Func<string, ConfluentKafka.IProducer<byte[], byte[]>> createProducer,
        TimeSpan transactionTimeout,
        KafkaMetrics kafkaMetrics,
        INodeIdentity nodeIdentity,
        EmitTracingOptions tracingOptions,
        ILogger<KafkaOutboxProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(createProducer);
        ArgumentNullException.ThrowIfNull(kafkaMetrics);
        ArgumentNullException.ThrowIfNull(nodeIdentity);
        ArgumentNullException.ThrowIfNull(logger);

        this.createProducer = createProducer;
        this.transactionTimeout = transactionTimeout;
        this.kafkaMetrics = kafkaMetrics;
        this.nodeIdentity = nodeIdentity;
        this.tracingOptions = tracingOptions;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public string SystemId => Provider.Identifier;

    /// <summary>
    /// Returns the transactional id of the outbox daemon with <paramref name="fencingKey"/>.
    /// </summary>
    internal static string TransactionalIdOf(Guid fencingKey) => $"emit-outbox-{fencingKey:N}";

    /// <inheritdoc/>
    public async Task<IOutboxDelivery> BeginDeliveryAsync(OutboxDeliveryContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A producer runs one transaction at a time
        await oneDeliveryAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var producer = await ProducerForAsync(context, cancellationToken).ConfigureAwait(false);
            try
            {
                producer.Producer.BeginTransaction();
            }
            catch (ConfluentKafka.KafkaException)
            {
                // Nothing was sent through it: it is replaced at once
                if (Release(producer))
                {
                    producer.Producer.Dispose();
                }

                throw;
            }

            return new Delivery(this, producer);
        }
        catch
        {
            oneDeliveryAtATime.Release();
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        current?.Producer.Dispose();
        oneDeliveryAtATime.Dispose();
    }

    /// <summary>
    /// Returns the producer of the run, initializing a new one for a new run or after the previous one failed.
    /// Initializing fences every earlier producer with the same transactional id.
    /// </summary>
    private async Task<TransactionalProducer> ProducerForAsync(OutboxDeliveryContext context, CancellationToken cancellationToken)
    {
        if (current is { } existing && ReferenceEquals(existing.Run, context))
        {
            return existing;
        }

        current?.Producer.Dispose();
        current = null;

        var producer = createProducer(TransactionalIdOf(context.FencingKey));
        try
        {
            // A broker whose transaction coordinator is not ready yet, as after it restarts, asks to be asked again;
            // each attempt waits up to the transaction timeout. A broker out of reach fails the delivery instead.
            while (true)
            {
                try
                {
                    await Task.Run(() => producer.InitTransactions(transactionTimeout), CancellationToken.None).ConfigureAwait(false);
                    break;
                }
                catch (ConfluentKafka.KafkaRetriableException ex)
                    when (!BrokerUnreachable.Contains(ex.Error.Code) && ex.Error.Code != ConfluentKafka.ErrorCode.Local_TimedOut && !cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("Outbox producer initialization is retried: {Reason}", ex.Error.Reason);
                }
            }
        }
        catch
        {
            producer.Dispose();
            throw;
        }

        logger.LogInformation(
            "Outbox producer initialized for generation {Generation}; earlier runs of the outbox daemon are fenced",
            context.Generation);
        current = new TransactionalProducer(context, producer);
        return current;
    }

    /// <summary>
    /// Drops the producer of a delivery that did not commit, disposing it once its commit, if any, has settled.
    /// </summary>
    private void Abandon(TransactionalProducer producer, Task? commit)
    {
        if (Release(producer))
        {
            _ = DisposeOnceSettledAsync(producer, commit);
        }
    }

    /// <summary>
    /// Stops using <paramref name="producer"/> for new deliveries. Returns <c>false</c> when it was already dropped.
    /// </summary>
    private bool Release(TransactionalProducer producer) =>
        ReferenceEquals(Interlocked.CompareExchange(ref current, null, producer), producer);

    private async Task DisposeOnceSettledAsync(TransactionalProducer producer, Task? commit)
    {
        try
        {
            if (commit is not null)
            {
                await commit.ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "The commit of an abandoned outbox transaction failed");
        }

        producer.Producer.Dispose();
    }

    /// <summary>
    /// Hands an entry to the producer, which keeps the messages of a partition in the order they are handed over.
    /// The send's outcome is recorded on <paramref name="delivery"/>, which commits only if every send succeeded.
    /// </summary>
    private void Send(TransactionalProducer producer, Delivery delivery, OutboxEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // A stopped daemon starts no new send
        cancellationToken.ThrowIfCancellationRequested();

        var destination = new Uri(entry.Destination);
        var topic = EmitEndpointAddress.GetEntityName(destination);
        if (string.IsNullOrEmpty(topic))
        {
            var errorMessage = $"Cannot extract topic from destination '{entry.Destination}' for entry {entry.Id}.";
            logger.LogError("Cannot extract topic from destination '{Destination}' for entry {EntryId}", entry.Destination, entry.Id);
            throw new InvalidOperationException(errorMessage);
        }

        var message = BuildMessage(entry);
        var messageSize = (message.Key?.Length ?? 0) + (message.Value?.Length ?? 0);
        kafkaMetrics.RecordProduceMessageSize(messageSize, topic);

        // A child of the message's create span, in the trace the producer stored with the entry
        var current = Activity.Current;
        var activity = KafkaSendActivity.Start(
            EmitActivitySources.Producer,
            tracingOptions,
            ActivityKind.Client,
            destination,
            entry.Headers.GetFirstValue(WellKnownHeaders.MessageId) is { } messageId ? Encoding.UTF8.GetString(messageId) : null,
            entry.Body is null,
            MessagingActivity.CreationContext(entry.Headers),
            [
                new(ActivityTagNames.NodeId, nodeIdentity.NodeId.ToString()),
                new(ActivityTagNames.Sequence, entry.Sequence),
                new(ActivityTagNames.GroupKey, entry.GroupKey),
            ]);

        // The send ends in its delivery report: the daemon goes on outside its span
        Activity.Current = current;

        var start = Stopwatch.GetTimestamp();
        try
        {
            producer.Producer.Produce(topic, message, report =>
            {
                var outcome = report.Error.IsError ? KafkaMetrics.ResultError : KafkaMetrics.ResultSuccess;
                kafkaMetrics.RecordProduceDuration(Stopwatch.GetElapsedTime(start).TotalSeconds, topic, outcome);
                kafkaMetrics.RecordProduceMessage(topic, outcome);
                if (report.Error.IsError)
                {
                    KafkaSendActivity.Fail(activity, report.Error);
                    delivery.RecordFailure(report.Error);
                }
                else
                {
                    KafkaSendActivity.Complete(activity, report.Partition, report.Offset);
                }

                activity?.Dispose();
            });
        }
        catch (Exception exception)
        {
            KafkaSendActivity.Fail(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Returns the exception a delivery that failed with <paramref name="error"/> throws: a broker out of reach is
    /// reported as such, so the outbox daemon delivers from another node.
    /// </summary>
    private static Exception FailureOf(ConfluentKafka.Error error, ConfluentKafka.KafkaException exception) =>
        BrokerUnreachable.Contains(error.Code)
            ? new MessageBrokerUnreachableException($"The outbox transaction could not reach the broker: {error.Reason}", exception)
            : exception;

    private static ConfluentKafka.Message<byte[], byte[]> BuildMessage(OutboxEntry entry)
    {
        // Decode key from base64 in properties
        byte[]? keyBytes = null;
        if (entry.Properties.TryGetValue(OutboxPropertyKeys.Key, out var keyBase64))
        {
            keyBytes = Convert.FromBase64String(keyBase64);
        }

        var kafkaMessage = new ConfluentKafka.Message<byte[], byte[]>
        {
            Key = keyBytes!,
            Value = entry.Body!
        };

        // Copy headers if present
        if (entry.Headers is { Count: > 0 })
        {
            kafkaMessage.Headers = [];
            foreach (var (key, value) in entry.Headers)
            {
                kafkaMessage.Headers.Add(key, value);
            }
        }

        return kafkaMessage;
    }

    /// <summary>
    /// The producer of a run of the outbox daemon.
    /// </summary>
    private sealed record TransactionalProducer(OutboxDeliveryContext Run, ConfluentKafka.IProducer<byte[], byte[]> Producer);

    /// <summary>
    /// A Kafka transaction, committed when completed if every message handed over was sent. Disposing one that did
    /// not commit abandons its producer, which the next delivery replaces.
    /// </summary>
    private sealed class Delivery(KafkaOutboxProvider provider, TransactionalProducer producer) : IOutboxDelivery
    {
        private ConfluentKafka.Error? failure;
        private Task? commit;
        private bool committed;

        public Task SendAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
        {
            provider.Send(producer, this, entry, cancellationToken);
            return Task.CompletedTask;
        }

        public async Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref failure) is { } failed)
            {
                throw FailureOf(failed, new ConfluentKafka.KafkaException(failed));
            }

            // Committing waits for every message handed over, and fails if one of them could not be sent. Once
            // started, its outcome is Kafka's to decide, so it is not cancelled: a later run's initialization aborts
            // it if it has not committed yet.
            commit = Task.Run(() => producer.Producer.CommitTransaction(provider.transactionTimeout), CancellationToken.None);
            try
            {
                await commit.WaitAsync(cancellationToken).ConfigureAwait(false);
                committed = true;
            }
            catch (ConfluentKafka.KafkaException ex)
            {
                throw FailureOf(Volatile.Read(ref failure) ?? ex.Error, ex);
            }
        }

        public ValueTask DisposeAsync()
        {
            if (!committed)
            {
                // Neither its commit nor an abort is waited for, as each can take up to the transaction timeout
                provider.Abandon(producer, commit);
            }

            provider.oneDeliveryAtATime.Release();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Records the first message that could not be sent: the transaction is then not committed.
        /// </summary>
        public void RecordFailure(ConfluentKafka.Error error) => Interlocked.CompareExchange(ref failure, error, null);
    }
}
