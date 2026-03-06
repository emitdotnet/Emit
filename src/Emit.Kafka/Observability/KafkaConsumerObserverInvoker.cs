namespace Emit.Kafka.Observability;

using Microsoft.Extensions.Logging;

/// <summary>
/// Invokes all registered <see cref="IKafkaConsumerObserver"/> instances for Kafka consumer
/// lifecycle events. Not a middleware — these events happen outside the message pipeline.
/// </summary>
internal sealed class KafkaConsumerObserverInvoker(
    IEnumerable<IKafkaConsumerObserver> observers,
    ILogger<KafkaConsumerObserverInvoker> logger)
{
    private readonly IKafkaConsumerObserver[] observers = observers.ToArray();

    /// <summary>
    /// Gets a value indicating whether any observers are registered.
    /// </summary>
    internal bool HasObservers => observers.Length > 0;

    internal async Task OnConsumerStartedAsync(ConsumerStartedEvent e)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnConsumerStartedAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IKafkaConsumerObserver.OnConsumerStartedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    internal async Task OnConsumerStoppedAsync(ConsumerStoppedEvent e)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnConsumerStoppedAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IKafkaConsumerObserver.OnConsumerStoppedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    internal async Task OnConsumerFaultedAsync(ConsumerFaultedEvent e)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnConsumerFaultedAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IKafkaConsumerObserver.OnConsumerFaultedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    internal async Task OnPartitionsAssignedAsync(PartitionsAssignedEvent e)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnPartitionsAssignedAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IKafkaConsumerObserver.OnPartitionsAssignedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    internal async Task OnPartitionsRevokedAsync(PartitionsRevokedEvent e)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnPartitionsRevokedAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IKafkaConsumerObserver.OnPartitionsRevokedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    internal async Task OnPartitionsLostAsync(PartitionsLostEvent e)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnPartitionsLostAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IKafkaConsumerObserver.OnPartitionsLostAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    internal async Task OnOffsetsCommittedAsync(OffsetsCommittedEvent e)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnOffsetsCommittedAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IKafkaConsumerObserver.OnOffsetsCommittedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    internal async Task OnOffsetCommitErrorAsync(OffsetCommitErrorEvent e)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnOffsetCommitErrorAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IKafkaConsumerObserver.OnOffsetCommitErrorAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    internal async Task OnDeserializationErrorAsync(DeserializationErrorEvent e)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnDeserializationErrorAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IKafkaConsumerObserver.OnDeserializationErrorAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }
}
