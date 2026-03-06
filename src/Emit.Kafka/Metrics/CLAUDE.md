# Metrics/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `KafkaMetrics.cs` | Kafka provider-specific metrics: produce/consume duration, offset lag, message throughput | Monitor Kafka performance or debug consumer lag |
| `KafkaBrokerMetrics.cs` | Kafka broker-level metrics exposed via librdkafka statistics callback | Monitor broker connectivity or debug partition assignment |
