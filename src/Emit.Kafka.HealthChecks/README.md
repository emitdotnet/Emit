# Emit.Kafka.HealthChecks

ASP.NET Core health check for Emit's Kafka provider. Exposes a broker connectivity probe via `AddEmitKafka()` on `IHealthChecksBuilder`, reusing the producer already registered in your DI container.

Requires `Emit.Kafka`.
