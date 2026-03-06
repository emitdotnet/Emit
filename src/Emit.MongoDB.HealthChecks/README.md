# Emit.MongoDB.HealthChecks

ASP.NET Core health check for Emit's MongoDB persistence provider. Exposes a database ping probe via `AddEmitMongoDB()` on `IHealthChecksBuilder`, reusing the MongoDB client already registered in your DI container.

Requires `Emit.MongoDB`.
