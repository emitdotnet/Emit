namespace Emit.Metrics;

using System.Diagnostics.Metrics;
using Emit.Abstractions.Metrics;

/// <summary>
/// Instruments for the <c>Emit</c> meter — pipeline, error handling, validation,
/// circuit breaker, rate limiting, and dead letter queue metrics.
/// </summary>
public sealed class EmitMetrics
{
    private readonly EmitMetricsEnrichment enrichment;

    private Func<int>? circuitBreakerStateCallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmitMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Optional meter factory for managed meter lifetime. Falls back to an unmanaged meter when <c>null</c>.</param>
    /// <param name="enrichment">Static enrichment tags appended to all recordings.</param>
    public EmitMetrics(IMeterFactory? meterFactory, EmitMetricsEnrichment enrichment)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        this.enrichment = enrichment;

        var meter = meterFactory?.Create(MeterNames.Emit) ?? new Meter(MeterNames.Emit);

        // Pipeline
        ProduceDuration = meter.CreateHistogram<double>(
            "emit.pipeline.produce.duration", "s", "Total outbound pipeline execution time.");

        ProduceCompleted = meter.CreateCounter<long>(
            "emit.pipeline.produce.completed", "{message}", "Count of completed produce pipeline executions.");

        ConsumeDuration = meter.CreateHistogram<double>(
            "emit.pipeline.consume.duration", "s", "Total inbound pipeline execution time.");

        ConsumeCompleted = meter.CreateCounter<long>(
            "emit.pipeline.consume.completed", "{message}", "Count of completed consume pipeline executions.");

        ConsumeMessages = meter.CreateCounter<long>(
            "emit.pipeline.consume.messages", "{message}", "Count of individual messages processed (batch-aware).");

        // Error handling
        ErrorActions = meter.CreateCounter<long>(
            "emit.consumer.error.actions", "{action}", "Count of error actions executed.");

        RetryAttempts = meter.CreateHistogram<int>(
            "emit.consumer.retry.attempts", "{attempt}", "Number of retry attempts per retry sequence.");

        RetryDuration = meter.CreateHistogram<double>(
            "emit.consumer.retry.duration", "s", "Wall-clock time spent in retry loops.");

        Discards = meter.CreateCounter<long>(
            "emit.consumer.discards", "{message}", "Count of messages explicitly discarded.");

        // Validation
        ValidationCompleted = meter.CreateCounter<long>(
            "emit.consumer.validation.completed", "{validation}", "Count of validation executions.");

        ValidationDuration = meter.CreateHistogram<double>(
            "emit.consumer.validation.duration", "s", "Validator execution time.");

        // Circuit breaker
        StateTransitions = meter.CreateCounter<long>(
            "emit.consumer.circuit_breaker.state_transitions", "{transition}", "Count of circuit breaker state transitions.");

        CircuitBreakerOpenDuration = meter.CreateHistogram<double>(
            "emit.consumer.circuit_breaker.open_duration", "s", "Duration from circuit opened to half-open.");

        meter.CreateObservableGauge(
            "emit.consumer.circuit_breaker.state", ObserveCircuitBreakerState, description: "Current circuit breaker state (0=closed, 1=open, 2=half_open).");

        // Rate limiting
        RateLimitWaitDuration = meter.CreateHistogram<double>(
            "emit.consumer.rate_limit.wait_duration", "s", "Time spent waiting for rate limit permits.");

        // Dead letter queue
        DlqProduced = meter.CreateCounter<long>(
            "emit.consumer.dlq.produced", "{message}", "Count of messages sent to a dead-letter topic.");

        DlqProduceErrors = meter.CreateCounter<long>(
            "emit.consumer.dlq.produce_errors", "{error}", "Count of failed DLQ produce attempts.");
    }

    // Pipeline
    internal Histogram<double> ProduceDuration { get; }

    internal Counter<long> ProduceCompleted { get; }

    internal Histogram<double> ConsumeDuration { get; }

    internal Counter<long> ConsumeCompleted { get; }

    internal Counter<long> ConsumeMessages { get; }

    // Error handling
    internal Counter<long> ErrorActions { get; }

    internal Histogram<int> RetryAttempts { get; }

    internal Histogram<double> RetryDuration { get; }

    internal Counter<long> Discards { get; }

    // Validation
    internal Counter<long> ValidationCompleted { get; }

    internal Histogram<double> ValidationDuration { get; }

    // Circuit breaker
    internal Counter<long> StateTransitions { get; }

    internal Histogram<double> CircuitBreakerOpenDuration { get; }

    // Rate limiting
    internal Histogram<double> RateLimitWaitDuration { get; }

    // Dead letter queue
    internal Counter<long> DlqProduced { get; }

    internal Counter<long> DlqProduceErrors { get; }

    // ── Pipeline recording methods ──

    internal void RecordProduceCompleted(double durationSeconds, string provider, string result)
    {
        var tags = enrichment.CreateTags([new("provider", provider), new("result", result)]);
        ProduceDuration.Record(durationSeconds, tags);
        ProduceCompleted.Add(1, tags);
    }

    internal void RecordConsumeCompleted(double durationSeconds, string provider, string result, string consumer)
    {
        var tags = enrichment.CreateTags([new("provider", provider), new("result", result), new("consumer", consumer)]);
        ConsumeDuration.Record(durationSeconds, tags);
        ConsumeCompleted.Add(1, tags);
    }

    internal void RecordConsumeMessages(long count, string provider, string result, string consumer)
    {
        var tags = enrichment.CreateTags([new("provider", provider), new("result", result), new("consumer", consumer)]);
        ConsumeMessages.Add(count, tags);
    }

    // ── Error handling recording methods ──

    internal void RecordErrorAction(string action)
    {
        var tags = enrichment.CreateTags([new("action", action)]);
        ErrorActions.Add(1, tags);
    }

    internal void RecordRetryAttempts(int attempts, string result)
    {
        var tags = enrichment.CreateTags([new("result", result)]);
        RetryAttempts.Record(attempts, tags);
    }

    internal void RecordRetryDuration(double seconds, string result)
    {
        var tags = enrichment.CreateTags([new("result", result)]);
        RetryDuration.Record(seconds, tags);
    }

    internal void RecordDiscard()
    {
        var tags = enrichment.CreateTags();
        Discards.Add(1, tags);
    }

    // ── Validation recording methods ──

    internal void RecordValidationCompleted(string result, string action, long count = 1)
    {
        if (count <= 0) return;
        var tags = enrichment.CreateTags([new("result", result), new("action", action)]);
        ValidationCompleted.Add(count, tags);
    }

    internal void RecordValidationDuration(double seconds)
    {
        var tags = enrichment.CreateTags();
        ValidationDuration.Record(seconds, tags);
    }

    // ── Circuit breaker recording methods ──

    internal void RecordStateTransition(string toState)
    {
        var tags = enrichment.CreateTags([new("to_state", toState)]);
        StateTransitions.Add(1, tags);
    }

    internal void RecordCircuitBreakerOpenDuration(double seconds)
    {
        var tags = enrichment.CreateTags();
        CircuitBreakerOpenDuration.Record(seconds, tags);
    }

    internal void RegisterCircuitBreakerStateCallback(Func<int> callback)
    {
        Volatile.Write(ref circuitBreakerStateCallback, callback);
    }

    // ── Rate limiting recording methods ──

    internal void RecordRateLimitWaitDuration(double seconds)
    {
        var tags = enrichment.CreateTags();
        RateLimitWaitDuration.Record(seconds, tags);
    }

    // ── DLQ recording methods ──

    internal void RecordDlqProduced(string reason)
    {
        var tags = enrichment.CreateTags([new("reason", reason)]);
        DlqProduced.Add(1, tags);
    }

    /// <summary>
    /// Records a DLQ produce error.
    /// </summary>
    public void RecordDlqProduceErrors(string reason)
    {
        var tags = enrichment.CreateTags([new("reason", reason)]);
        DlqProduceErrors.Add(1, tags);
    }

    private Measurement<int> ObserveCircuitBreakerState()
    {
        var value = Volatile.Read(ref circuitBreakerStateCallback)?.Invoke() ?? 0;
        var tags = enrichment.CreateTags();
        return new Measurement<int>(value, tags);
    }
}
