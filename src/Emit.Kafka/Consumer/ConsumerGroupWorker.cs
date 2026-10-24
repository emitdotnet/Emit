namespace Emit.Kafka.Consumer;

using System.Linq;
using System.Threading.Channels;
using Emit.Abstractions;
using Emit.Consumer;
using Emit.Kafka.Metrics;
using Emit.Kafka.Observability;
using Emit.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Background service managing a Kafka consumer group. One instance per consumer group declaration.
/// Orchestrates the Kafka consumer lifecycle, partition rebalancing, and poll loop. Worker pool
/// management is delegated to <see cref="WorkerPoolSupervisor{TKey,TValue}"/>.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
internal sealed class ConsumerGroupWorker<TKey, TValue>(
    ConsumerGroupRegistration<TKey, TValue> registration,
    KafkaConsumerFlowControl flowControl,
    CircuitBreakerObserver<TValue>? circuitBreakerObserver,
    IServiceScopeFactory scopeFactory,
    ILoggerFactory loggerFactory,
    KafkaConsumerObserverInvoker observerInvoker,
    KafkaMetrics kafkaMetrics,
    KafkaBrokerMetrics kafkaBrokerMetrics,
    EmitMetrics emitMetrics,
    IDeadLetterSink? deadLetterSink) : BackgroundService
{
    private readonly ConsumerGroupRegistration<TKey, TValue> registration = registration;
    private readonly KafkaConsumerFlowControl flowControl = flowControl;
    private readonly CircuitBreakerObserver<TValue>? circuitBreakerObserver = circuitBreakerObserver;
    private readonly IServiceScopeFactory scopeFactory = scopeFactory;
    private readonly ILoggerFactory loggerFactory = loggerFactory;
    private readonly KafkaMetrics kafkaMetrics = kafkaMetrics;
    private readonly KafkaBrokerMetrics kafkaBrokerMetrics = kafkaBrokerMetrics;
    private readonly EmitMetrics emitMetrics = emitMetrics;
    private readonly IDeadLetterSink? deadLetterSink = deadLetterSink;
    private readonly ILogger logger = loggerFactory.CreateLogger<ConsumerGroupWorker<TKey, TValue>>();

    // Runtime state — created on the background thread, not in the constructor.
    private WorkerPoolSupervisor<TKey, TValue>? supervisor;
    private OffsetCommitter? committer;
    private OffsetManager? offsetManager;
    private CancellationToken executionToken;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        executionToken = stoppingToken;

        StartupDiagnosticsLogger.Log(registration, deadLetterSink, logger);

        kafkaMetrics.RegisterConsumerGroup(
            registration.Name,
            registration.GroupId,
            registration.TopicName,
            registration.WorkerCount,
            () =>
            {
                var w = supervisor?.Workers;
                if (w is null) return [];
                var depths = new int[w.Length];
                for (var i = 0; i < w.Length; i++)
                    depths[i] = w[i].ChannelCount;
                return depths;
            });

        supervisor = new WorkerPoolSupervisor<TKey, TValue>(
            registration,
            CreateWorkerPool,
            kafkaMetrics,
            observerInvoker,
            logger,
            flowControl.EndHandling);

        var consumer = BuildConfluentConsumer();
        flowControl.SetConsumer(consumer);

        try
        {
            consumer.Subscribe(registration.TopicName);

            committer = new OffsetCommitter(consumer, registration.CommitInterval, registration.GroupId, observerInvoker, kafkaMetrics, logger);
            offsetManager = new OffsetManager(committer);

            await RunPollLoopAsync(consumer, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            if (supervisor is not null)
            {
                await supervisor.StopAsync().ConfigureAwait(false);
            }

            if (committer is not null)
            {
                committer.Flush();
                await committer.DisposeAsync().ConfigureAwait(false);
            }

            supervisor = null;

            kafkaMetrics.DeregisterConsumerGroup(registration.Name);

            // Dispose circuit breaker observer to cancel any pending half-open transition
            // before closing the consumer. This prevents ObjectDisposedException from
            // the observer trying to resume a disposed consumer.
            circuitBreakerObserver?.Dispose();

            // Pauses and resumes can come from any thread: they must not reach the closed consumer
            flowControl.Detach();

            await observerInvoker.OnConsumerStoppedAsync(new ConsumerStoppedEvent(registration.GroupId, registration.TopicName)).ConfigureAwait(false);

            try { consumer.Close(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error closing consumer for group '{GroupId}'", registration.GroupId);
            }

            consumer.Dispose();
        }
    }

    private ConfluentKafka.IConsumer<byte[], byte[]> BuildConfluentConsumer()
    {
        var config = registration.BuildConsumerConfig();
        config.StatisticsIntervalMs ??= 5000;

        return new ConfluentKafka.ConsumerBuilder<byte[], byte[]>(config)
            .SetPartitionsAssignedHandler(OnPartitionsAssigned)
            .SetPartitionsRevokedHandler(OnPartitionsRevoked)
            .SetPartitionsLostHandler(OnPartitionsLost)
            .SetStatisticsHandler((_, json) => kafkaBrokerMetrics.HandleStatistics(json))
            .SetErrorHandler((_, e) => logger.LogError("Kafka consumer error in group '{GroupId}': {Reason}",
                registration.GroupId, e.Reason))
            .Build();
    }

    private async Task RunPollLoopAsync(
        ConfluentKafka.IConsumer<byte[], byte[]> consumer,
        CancellationToken cancellationToken)
    {
        await observerInvoker.OnConsumerStartedAsync(new ConsumerStartedEvent(registration.GroupId, registration.TopicName, registration.WorkerCount)).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (supervisor is not null && supervisor.IsFaulted)
            {
                await HandlePoolFaultAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var result = consumer.Consume(cancellationToken);
                if (result is null || result.IsPartitionEOF)
                {
                    continue;
                }

                var workers = supervisor?.Workers;
                var strategy = supervisor?.DistributionStrategy;

                if (workers is null || strategy is null || offsetManager is null)
                {
                    continue;
                }

                // The poll loop never waits for room in a worker queue, so the consumer stays in the group however
                // long the handlers take: the message's partition is paused until the queue has room, and the next
                // fetch on resume starts from it. The poll loop is the queue's only writer, so the room it saw
                // is still there when it writes
                var worker = workers[strategy.SelectWorker(result.Message.Key, result.Partition.Value)];
                if (!worker.HasRoom)
                {
                    PauseUntilRoom(worker, result.TopicPartition);
                    consumer.Seek(result.TopicPartitionOffset);
                    continue;
                }

                // A message fetched before the group was paused, or from a partition assigned during the pause,
                // is not handed over: its partition is paused, and the next fetch on resume starts from it
                if (!flowControl.TryBeginHandling(result.TopicPartition))
                {
                    consumer.Seek(result.TopicPartitionOffset);
                    continue;
                }

                try
                {
                    offsetManager.Enqueue(result.Topic, result.Partition.Value, result.Offset.Value);
                    if (!worker.Writer.TryWrite(result))
                    {
                        throw new ChannelClosedException();
                    }

                    kafkaMetrics.RecordConsumeMessage(registration.GroupId, result.Topic, result.Partition.Value);
                }
                catch
                {
                    // Never written to a worker, so never handled
                    flowControl.EndHandling(1);
                    throw;
                }
            }
            catch (ChannelClosedException) when (supervisor is not null && supervisor.IsFaulted)
            {
                await HandlePoolFaultAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ConfluentKafka.ConsumeException ex)
            {
                logger.LogError(ex, "Consume error in group '{GroupId}' on topic '{Topic}'",
                    registration.GroupId, registration.TopicName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void PauseUntilRoom(ConsumerWorker<TKey, TValue> worker, ConfluentKafka.TopicPartition partition)
    {
        flowControl.PauseForFullQueue(partition);
        _ = worker.WaitForRoomAsync().ContinueWith(
            _ => flowControl.ResumeAfterFullQueue(partition),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandlePoolFaultAsync(CancellationToken cancellationToken)
    {
        if (supervisor is null)
        {
            return;
        }

        logger.LogInformation("Restarting worker pool after fault for group '{GroupId}'",
            registration.GroupId);

        await supervisor.StopAsync().ConfigureAwait(false);

        ReplaceOffsetManager();
        committer?.Flush();

        if (offsetManager is not null && !cancellationToken.IsCancellationRequested)
        {
            supervisor.Start(offsetManager, cancellationToken);
        }
    }

    private WorkerPool<TKey, TValue> CreateWorkerPool(
        OffsetManager offsetManager,
        CancellationToken cancellationToken)
    {
        var poolSuffix = Guid.NewGuid().ToString("N")[..4];
        var poolWorkers = new ConsumerWorker<TKey, TValue>[registration.WorkerCount];
        var poolTasks = new Task[registration.WorkerCount];

        for (var i = 0; i < registration.WorkerCount; i++)
        {
            var workerId = $"{registration.GroupId}/Worker[{i}]-{poolSuffix}";
            var workerLogger = loggerFactory.CreateLogger(
                $"{typeof(ConsumerGroupWorker<TKey, TValue>).FullName}.Worker[{i}]");
            var worker = new ConsumerWorker<TKey, TValue>(
                workerId, registration, offsetManager, scopeFactory,
                registration.GroupId, registration.DestinationAddress,
                observerInvoker, kafkaMetrics, emitMetrics, deadLetterSink, workerLogger, flowControl);
            poolWorkers[i] = worker;
            poolTasks[i] = worker.RunAsync(cancellationToken);
        }

        return new WorkerPool<TKey, TValue>(poolWorkers, poolTasks);
    }

    private void OnPartitionsRevoked(
        ConfluentKafka.IConsumer<byte[], byte[]> consumer,
        List<ConfluentKafka.TopicPartitionOffset> partitions)
    {
        logger.LogInformation("Partitions revoked for group '{GroupId}': [{Partitions}]",
            registration.GroupId, string.Join(", ", partitions));

        kafkaMetrics.RecordPartitionEvent(registration.GroupId, registration.TopicName, KafkaMetrics.PartitionEventRevoked, partitions.Count);
        observerInvoker.OnPartitionsRevokedAsync(new PartitionsRevokedEvent(registration.GroupId, registration.TopicName, partitions.Select(p => p.Partition.Value).ToList())).GetAwaiter().GetResult();

        supervisor?.StopAsync().GetAwaiter().GetResult();
        ReplaceOffsetManager();
        committer?.Flush();
        flowControl.OnPartitionsRevoked(partitions.Select(p => p.TopicPartition));
    }

    private void OnPartitionsLost(
        ConfluentKafka.IConsumer<byte[], byte[]> consumer,
        List<ConfluentKafka.TopicPartitionOffset> partitions)
    {
        logger.LogWarning("Partitions lost for group '{GroupId}': [{Partitions}]",
            registration.GroupId, string.Join(", ", partitions));

        kafkaMetrics.RecordPartitionEvent(registration.GroupId, registration.TopicName, KafkaMetrics.PartitionEventLost, partitions.Count);
        observerInvoker.OnPartitionsLostAsync(new PartitionsLostEvent(registration.GroupId, registration.TopicName, partitions.Select(p => p.Partition.Value).ToList())).GetAwaiter().GetResult();

        supervisor?.StopAsync().GetAwaiter().GetResult();
        ReplaceOffsetManager();
        flowControl.OnPartitionsRevoked(partitions.Select(p => p.TopicPartition));
    }

    private void OnPartitionsAssigned(
        ConfluentKafka.IConsumer<byte[], byte[]> consumer,
        List<ConfluentKafka.TopicPartition> partitions)
    {
        var totalPartitions = consumer.Assignment.Concat(partitions).Select(p => p.Partition.Value).Distinct().Order().ToList();

        logger.LogInformation(
            "Rebalance for group '{GroupId}' on topic '{TopicName}': {NewCount} new partition(s) [{NewPartitions}], total assignment: [{TotalPartitions}]",
            registration.GroupId,
            registration.TopicName,
            partitions.Count,
            partitions.Count > 0
                ? string.Join(", ", partitions.Select(p => p.Partition.Value))
                : "none",
            totalPartitions.Count > 0
                ? string.Join(", ", totalPartitions)
                : "none");

        kafkaMetrics.RecordPartitionEvent(registration.GroupId, registration.TopicName, KafkaMetrics.PartitionEventAssigned, partitions.Count);
        observerInvoker.OnPartitionsAssignedAsync(new PartitionsAssignedEvent(registration.GroupId, registration.TopicName, partitions.Select(p => p.Partition.Value).ToList())).GetAwaiter().GetResult();

        flowControl.OnPartitionsAssigned(partitions);

        if (offsetManager is null || supervisor is null)
        {
            return;
        }

        supervisor.Start(offsetManager, executionToken);
    }

    /// <summary>
    /// Gives the next worker pool an offset manager of its own. A worker of the stopped pool that outlived its
    /// stop timeout keeps its retired manager, so what it finishes later cannot move the new pool's commits.
    /// </summary>
    private void ReplaceOffsetManager()
    {
        if (offsetManager is null || committer is null)
        {
            return;
        }

        offsetManager.Retire();
        offsetManager = new OffsetManager(committer);
    }
}
