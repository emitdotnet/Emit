# Emit.Kafka.HealthChecks/

ASP.NET Core health check for Kafka broker connectivity via the registered IProducer.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Package description and usage overview | Understand what this package provides |
| `KafkaHealthCheck.cs` | IHealthCheck implementation that queries Kafka broker metadata to verify connectivity | Understand the health check logic or customize the check behavior |
| `KafkaHealthChecksBuilderExtensions.cs` | AddEmitKafka extension on IHealthChecksBuilder for registering the health check | Register the health check in an application's DI setup |
| `Emit.Kafka.HealthChecks.csproj` | Project file | Configure package metadata or dependencies |
