# Tracing/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `KafkaSendActivity.cs` | The `send` span of a message sent to Kafka, directly, from the outbox or to a dead letter topic: started before the send, ended with its partition and offset or its error | Change how sends to Kafka are traced |
| `KafkaActivityEnricher.cs` | Enricher registered by `AddKafka` adding the key (when a string) and tombstone attributes to the `create` span, from the key payload and the message | Change the Kafka attributes of a message being produced |
| `KafkaActivityTagNames.cs` | The OpenTelemetry convention attributes the Kafka provider sets: consumer group, partition, offset, key, tombstone | Reference Kafka span attributes |
