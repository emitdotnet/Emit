# Emit.Kafka.AvroSerializer/

Avro serializer extension for Kafka topic builder with Confluent Schema Registry support.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `Emit.Kafka.AvroSerializer.csproj` | Project file with Confluent.SchemaRegistry.Serdes.Avro dependency | Configure Avro serializer dependencies |
| `KafkaTopicBuilderAvroExtensions.cs` | Extension methods for KafkaTopicBuilder providing Avro serializer/deserializer declarations | Configure Avro serialization for Kafka topics |
