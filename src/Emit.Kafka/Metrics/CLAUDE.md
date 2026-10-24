# Metrics/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `KafkaMetrics.cs` | Kafka provider-specific metrics: produce/consume duration, offset lag, message throughput, and worker gauges per consumer group declaration | Monitor Kafka performance or debug consumer lag |
| `KafkaMeterNames.cs` | The names of the Kafka provider's meters, registered as package meters by `AddKafka` | Reference or disable the Kafka meters |
| `KafkaBrokerMetrics.cs` | Kafka broker-level metrics exposed via librdkafka statistics callback | Monitor broker connectivity or debug partition assignment |
