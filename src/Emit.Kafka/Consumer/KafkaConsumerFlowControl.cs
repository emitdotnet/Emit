namespace Emit.Kafka.Consumer;

using Emit.Abstractions;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Pauses and resumes the partitions of one consumer group declaration while its consumer stays in the group.
/// The poll loop keeps calling <c>Consume()</c> while paused, which sends heartbeats to the broker and keeps
/// <c>max.poll.interval.ms</c> from expiring.
/// </summary>
/// <remarks>
/// <para>
/// The application and the circuit breaker each hold their own pause, through <see cref="Application"/> and
/// <see cref="CircuitBreaker"/>: the partitions are paused while either holds it, and each only releases its own.
/// </para>
/// <para>
/// The partitions are the ones the rebalance handlers report, not the consumer's assignment, which the client
/// only updates after the assigned handler returns. A partition can only be paused once the client has assigned
/// it, so a newly assigned partition is paused when its first message arrives: <see cref="TryBeginHandling"/>
/// refuses that message, and the poll loop seeks back to it so it is fetched again on resume.
/// </para>
/// </remarks>
internal sealed class KafkaConsumerFlowControl
{
    private static readonly AsyncLocal<KafkaConsumerFlowControl?> handlerOf = new();

    private readonly object sync = new();
    private readonly HashSet<ConfluentKafka.TopicPartition> partitions = [];
    private readonly ILogger<KafkaConsumerFlowControl> logger;
    private ConfluentKafka.IConsumer<byte[], byte[]>? consumer;
    private bool pausedByApplication;
    private bool pausedByCircuitBreaker;
    private int handling;
    private TaskCompletionSource? idle;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaConsumerFlowControl"/> class.
    /// </summary>
    public KafkaConsumerFlowControl(ILogger<KafkaConsumerFlowControl> logger)
    {
        this.logger = logger;
        Application = new Hold(this, application: true);
        CircuitBreaker = new Hold(this, application: false);
    }

    /// <summary>
    /// Gets the flow control the application resolves for this declaration. Its pause returns once no message
    /// of the declaration is being handled.
    /// </summary>
    internal IConsumerFlowControl Application { get; }

    /// <summary>
    /// Gets the flow control the circuit breaker of this declaration uses. Its pause does not wait for the
    /// messages being handled, since the circuit breaker pauses from inside one of them.
    /// </summary>
    internal IConsumerFlowControl CircuitBreaker { get; }

    /// <summary>
    /// Gets whether the application or the circuit breaker holds the declaration paused.
    /// </summary>
    internal bool IsPaused
    {
        get
        {
            lock (sync)
            {
                return IsHeld;
            }
        }
    }

    private bool IsHeld => pausedByApplication || pausedByCircuitBreaker;

    /// <summary>
    /// Sets the consumer the partitions are paused and resumed on. Called by the consumer group worker after it
    /// built the consumer.
    /// </summary>
    internal void SetConsumer(ConfluentKafka.IConsumer<byte[], byte[]> consumer)
    {
        lock (sync)
        {
            this.consumer = consumer;
        }
    }

    /// <summary>
    /// Detaches the consumer before it is closed: later pauses and resumes only record the state. Messages left
    /// in the workers are never handled, so nothing is waited for anymore.
    /// </summary>
    internal void Detach()
    {
        lock (sync)
        {
            consumer = null;
            partitions.Clear();
            handling = 0;
            ReleaseIdle();
        }
    }

    /// <summary>
    /// Records partitions newly assigned to the consumer. Called on the poll thread by the assigned handler,
    /// before the client applies the assignment.
    /// </summary>
    internal void OnPartitionsAssigned(IEnumerable<ConfluentKafka.TopicPartition> assigned)
    {
        lock (sync)
        {
            partitions.UnionWith(assigned);
        }
    }

    /// <summary>
    /// Records partitions taken from the consumer, revoked or lost. Called on the poll thread.
    /// </summary>
    internal void OnPartitionsRevoked(IEnumerable<ConfluentKafka.TopicPartition> taken)
    {
        lock (sync)
        {
            partitions.ExceptWith(taken);
        }
    }

    /// <summary>
    /// Decides on the poll thread whether a message just fetched from <paramref name="partition"/> may be handed
    /// to a worker, and counts it as being handled if so. While the declaration is paused, the message is refused
    /// and its partition paused, which a partition assigned during the pause is not yet; the caller then seeks
    /// back to the message so it is fetched again on resume.
    /// </summary>
    internal bool TryBeginHandling(ConfluentKafka.TopicPartition partition)
    {
        lock (sync)
        {
            if (IsHeld)
            {
                Apply(pause: true, [partition]);
                return false;
            }

            handling++;
            return true;
        }
    }

    /// <summary>
    /// Records that messages counted by <see cref="TryBeginHandling"/> are no longer being handled: handled,
    /// whatever the outcome, or never to be handled.
    /// </summary>
    internal void EndHandling(int count)
    {
        lock (sync)
        {
            handling = Math.Max(0, handling - count);
            if (handling == 0)
            {
                ReleaseIdle();
            }
        }
    }

    /// <summary>
    /// Marks the calling flow as a worker handling this declaration's messages, so a pause it requests does not
    /// wait for the messages being handled, among which its own.
    /// </summary>
    internal void EnterHandler() => handlerOf.Value = this;

    private Task PauseAsync(bool application, CancellationToken cancellationToken)
    {
        Task wait;
        lock (sync)
        {
            var wasHeld = IsHeld;
            if (application)
            {
                pausedByApplication = true;
            }
            else
            {
                pausedByCircuitBreaker = true;
            }

            if (!wasHeld)
            {
                Apply(pause: true, partitions);
            }

            if (!application || handling == 0 || handlerOf.Value == this)
            {
                return Task.CompletedTask;
            }

            wait = (idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }

        return wait.WaitAsync(cancellationToken);
    }

    private Task ResumeAsync(bool application)
    {
        lock (sync)
        {
            if (application)
            {
                if (!pausedByApplication)
                {
                    return Task.CompletedTask;
                }

                pausedByApplication = false;

                // A pause still waiting for the messages being handled has been overtaken
                ReleaseIdle();
            }
            else
            {
                if (!pausedByCircuitBreaker)
                {
                    return Task.CompletedTask;
                }

                pausedByCircuitBreaker = false;
            }

            if (!IsHeld)
            {
                Apply(pause: false, partitions);
            }
        }

        return Task.CompletedTask;
    }

    private void ReleaseIdle()
    {
        idle?.TrySetResult();
        idle = null;
    }

    /// <summary>
    /// Pauses or resumes partitions on the consumer, if attached. Must be called under <see cref="sync"/>.
    /// </summary>
    private void Apply(bool pause, IEnumerable<ConfluentKafka.TopicPartition> targets)
    {
        if (consumer is null)
        {
            return;
        }

        List<ConfluentKafka.TopicPartition> list = [.. targets];
        if (list.Count == 0)
        {
            return;
        }

        try
        {
            if (pause)
            {
                consumer.Pause(list);
            }
            else
            {
                consumer.Resume(list);
            }

            logger.LogInformation("{Action} {Count} partitions", pause ? "Paused" : "Resumed", list.Count);
        }
        catch (ConfluentKafka.TopicPartitionException ex)
        {
            // A partition the client has not assigned yet is neither paused nor resumable: the others were
            // applied, and a paused declaration pauses it when its first message arrives
            logger.LogDebug(ex, "{Action} the assigned partitions; the others are not assigned yet", pause ? "Paused" : "Resumed");
        }
    }

    /// <summary>
    /// One holder's view of the declaration's pause.
    /// </summary>
    private sealed class Hold(KafkaConsumerFlowControl owner, bool application) : IConsumerFlowControl
    {
        public Task PauseAsync(CancellationToken cancellationToken) => owner.PauseAsync(application, cancellationToken);

        public Task ResumeAsync(CancellationToken cancellationToken) => owner.ResumeAsync(application);
    }
}
