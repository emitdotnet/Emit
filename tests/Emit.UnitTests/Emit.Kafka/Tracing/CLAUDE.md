# Tracing/

Unit tests for the Kafka provider's tracing.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `KafkaActivityEnricherTests.cs` | The `create` span gets the key when it is a string and the tombstone attribute; other phases get nothing | Debugging Kafka enricher test failures |
| `KafkaSendActivityTests.cs` | A failed send reports the Kafka error code as `error.type`, from an exception as from a delivery report | Debugging send span failure test failures |
