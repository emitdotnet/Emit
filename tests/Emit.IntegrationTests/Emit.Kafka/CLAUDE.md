# Emit.Kafka/

Kafka integration tests: compliance implementations proving Kafka provider correctness, plus Kafka-specific edge case tests.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `KafkaProduceConsumeComplianceTests.cs` | Inherits ProduceConsumeCompliance | Debug or extend basic produce/consume tests |
| `KafkaCircuitBreakerComplianceTests.cs` | Inherits CircuitBreakerCompliance | Debug or extend circuit breaker tests |
| `KafkaConsumeObserverComplianceTests.cs` | Inherits ConsumeObserverCompliance | Debug or extend observer tests |
| `KafkaDeadLetterComplianceTests.cs` | Inherits DeadLetterCompliance | Debug or extend DLQ delivery tests |
| `KafkaDeadLetterConventionComplianceTests.cs` | Inherits DeadLetterConventionCompliance | Verify or change dead-letter envelope format |
| `KafkaErrorPolicyComplianceTests.cs` | Inherits ErrorPolicyCompliance | Debug or extend error policy tests |
| `KafkaExternalMessageComplianceTests.cs` | Inherits ExternalMessageCompliance | Debug or extend external message ingestion tests |
| `KafkaFanOutComplianceTests.cs` | Inherits FanOutCompliance | Debug or extend fan-out consumer group tests |
| `KafkaFilterComplianceTests.cs` | Inherits FilterCompliance | Debug or extend consumer filter tests |
| `KafkaGlobalPipelineComplianceTests.cs` | Inherits GlobalPipelineCompliance | Debug or extend global middleware tests |
| `KafkaOffsetCommitComplianceTests.cs` | Inherits OffsetCommitCompliance | Debug or extend offset commit correctness tests |
| `KafkaPerConsumerMiddlewareComplianceTests.cs` | Inherits PerConsumerMiddlewareCompliance | Debug or extend per-consumer middleware tests |
| `KafkaRateLimiterComplianceTests.cs` | Inherits RateLimiterCompliance | Debug or extend rate limiter tests |
| `KafkaRetryComplianceTests.cs` | Inherits RetryCompliance | Debug or extend retry behavior tests |
| `KafkaRoutingComplianceTests.cs` | Inherits RoutingCompliance | Debug or extend content-based routing tests |
| `KafkaValidationComplianceTests.cs` | Inherits ValidationCompliance | Debug or extend message validation tests |
| `KafkaWorkerDistributionComplianceTests.cs` | Inherits WorkerDistributionCompliance | Debug or extend worker distribution strategy tests |
| `KafkaWorkerPoolRebalanceRecoveryComplianceTests.cs` | Inherits WorkerPoolRebalanceRecoveryCompliance | Debug or extend rebalance recovery tests |
| `KafkaDeserializationErrorTests.cs` | Kafka-specific: tests behavior when a message cannot be deserialized | Debug or extend deserialization error handling |
| `KafkaOrderedDeliveryTests.cs` | Kafka-specific: verifies per-key ordering with ByKeyHash distribution | Debug or extend ordering guarantees |
| `KafkaRetryDiscardComplianceTests.cs` | Kafka-specific: retry exhaustion resulting in discard (no DLQ) | Debug or extend retry-to-discard behavior |
| `KafkaValidationThrowsComplianceTests.cs` | Kafka-specific: validator that throws (transient error path) triggers retry | Debug or extend validation transient error behavior |
| `KafkaDlqKeyPreservationComplianceTests.cs` | Kafka-specific: original message key is preserved in DLQ message | Verify or change DLQ key forwarding |
| `KafkaDlqMisconfigurationTests.cs` | Kafka-specific: behavior when DLQ topic is misconfigured | Debug or extend DLQ misconfiguration handling |
| `KafkaDlqProduceFailureTests.cs` | Kafka-specific: behavior when DLQ produce itself fails | Debug or extend DLQ produce failure handling |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `TestInfrastructure/` | Kafka + Schema Registry Testcontainers fixture | Understand container lifecycle or modify Kafka test infrastructure |
