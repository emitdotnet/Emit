# Emit.MongoDB.HealthChecks/

ASP.NET Core health check for MongoDB connectivity via the registered IMongoDatabase.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Package description and usage overview | Understand what this package provides |
| `MongoDbHealthCheck.cs` | IHealthCheck implementation that pings MongoDB to verify broker reachability | Understand the health check logic or customize the check behavior |
| `MongoDbHealthChecksBuilderExtensions.cs` | AddEmitMongoDB extension on IHealthChecksBuilder for registering the health check | Register the health check in an application's DI setup |
| `Emit.MongoDB.HealthChecks.csproj` | Project file | Configure package metadata or dependencies |
