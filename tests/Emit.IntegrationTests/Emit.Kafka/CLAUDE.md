# Emit.Kafka/

Kafka integration tests: compliance implementations proving Kafka provider correctness, plus Kafka-specific edge case tests.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `KafkaProduceConsumeCompliance.cs` | Inherits ProduceConsumeCompliance | Debug or extend basic produce/consume tests |
| `KafkaBatchConsumerCompliance.cs` | Inherits BatchConsumerCompliance | Debug or extend batch consumer tests |
| `KafkaCircuitBreakerCompliance.cs` | Inherits CircuitBreakerCompliance | Debug or extend circuit breaker tests |
| `KafkaCircuitBreakerRebalanceTests.cs` | Kafka-specific: circuit breaker behavior during partition rebalance | Debug or extend circuit breaker rebalance interaction |
| `KafkaConsumeObserverCompliance.cs` | Inherits ConsumeObserverCompliance | Debug or extend consume observer tests |
| `KafkaConsumerObserverCompliance.cs` | Inherits ConsumerObserverCompliance | Debug or extend consumer observer tests |
| `KafkaDeadLetterCompliance.cs` | Inherits DeadLetterCompliance | Debug or extend DLQ delivery tests |
| `KafkaErrorPolicyCompliance.cs` | Inherits ErrorPolicyCompliance | Debug or extend error policy tests |
| `KafkaExternalMessageCompliance.cs` | Inherits ExternalMessageCompliance | Debug or extend external message ingestion tests |
| `KafkaFanOutCompliance.cs` | Inherits FanOutCompliance | Debug or extend fan-out consumer group tests |
| `KafkaFilterCompliance.cs` | Inherits FilterCompliance | Debug or extend consumer filter tests |
| `KafkaGlobalPipelineCompliance.cs` | Inherits GlobalPipelineCompliance | Debug or extend global middleware tests |
| `KafkaOffsetCommitCompliance.cs` | Inherits OffsetCommitCompliance | Debug or extend offset commit correctness tests |
| `KafkaOutboundMiddlewareHeadersCompliance.cs` | Inherits OutboundMiddlewareHeadersCompliance | Debug or extend outbound middleware header tests |
| `KafkaPerConsumerMiddlewareCompliance.cs` | Inherits PerConsumerMiddlewareCompliance | Debug or extend per-consumer middleware tests |
| `KafkaProduceObserverCompliance.cs` | Inherits ProduceObserverCompliance | Debug or extend produce observer tests |
| `KafkaProviderPipelineCompliance.cs` | Inherits ProviderPipelineCompliance | Debug or extend provider pipeline tests |
| `KafkaRateLimiterCompliance.cs` | Inherits RateLimiterCompliance | Debug or extend rate limiter tests |
| `KafkaRetryCompliance.cs` | Inherits RetryCompliance | Debug or extend retry behavior tests |
| `KafkaRoutingCompliance.cs` | Inherits RoutingCompliance | Debug or extend content-based routing tests |
| `KafkaValidationCompliance.cs` | Inherits ValidationCompliance | Debug or extend message validation tests |
| `KafkaWorkerDistributionCompliance.cs` | Inherits WorkerDistributionCompliance | Debug or extend worker distribution strategy tests |
| `KafkaWorkerPoolRebalanceRecoveryCompliance.cs` | Inherits WorkerPoolRebalanceRecoveryCompliance | Debug or extend rebalance recovery tests |
| `KafkaDeserializationErrorTests.cs` | Kafka-specific: behavior when a message cannot be deserialized | Debug or extend deserialization error handling |
| `KafkaDlqKeyPreservationCompliance.cs` | Kafka-specific: original message key is preserved in DLQ message | Verify or change DLQ key forwarding |
| `KafkaDlqMisconfigurationTests.cs` | Kafka-specific: behavior when DLQ topic is misconfigured | Debug or extend DLQ misconfiguration handling |
| `KafkaDlqProduceFailureTests.cs` | Kafka-specific: behavior when DLQ produce itself fails | Debug or extend DLQ produce failure handling |
| `KafkaOrderedDeliveryTests.cs` | Kafka-specific: per-key ordering with ByKeyHash distribution | Debug or extend ordering guarantees |
| `KafkaRetryDiscardCompliance.cs` | Kafka-specific: retry exhaustion resulting in discard (no DLQ) | Debug or extend retry-to-discard behavior |
| `KafkaValidationThrowsCompliance.cs` | Kafka-specific: validator that throws (transient error path) triggers retry | Debug or extend validation transient error behavior |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `TestInfrastructure/` | Kafka + Schema Registry Testcontainers fixture | Understand container lifecycle or modify Kafka test infrastructure |
