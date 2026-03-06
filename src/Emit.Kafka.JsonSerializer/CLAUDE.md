# Emit.Kafka.JsonSerializer/

JSON Schema serializer extension for Kafka topic builder with Confluent Schema Registry support.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `Emit.Kafka.JsonSerializer.csproj` | Project file with Confluent.SchemaRegistry.Serdes.Json dependency | Configure JSON Schema serializer dependencies |
| `KafkaTopicBuilderJsonSchemaExtensions.cs` | Extension methods for KafkaTopicBuilder providing JSON Schema serializer/deserializer declarations | Configure JSON Schema serialization for Kafka topics |
