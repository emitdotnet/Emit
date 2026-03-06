# Emit.Kafka.ProtobufSerializer/

Protobuf serializer extension for Kafka topic builder with Confluent Schema Registry support.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `Emit.Kafka.ProtobufSerializer.csproj` | Project file with Confluent.SchemaRegistry.Serdes.Protobuf dependency | Configure Protobuf serializer dependencies |
| `KafkaTopicBuilderProtobufExtensions.cs` | Extension methods for KafkaTopicBuilder providing Protobuf serializer/deserializer declarations | Configure Protobuf serialization for Kafka topics |
