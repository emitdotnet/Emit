# Emit.EntityFrameworkCore.HealthChecks/

ASP.NET Core health check for EF Core / PostgreSQL connectivity via the registered IEmitDbContextFactory.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Package description and usage overview | Understand what this package provides |
| `EfCoreHealthCheck.cs` | IHealthCheck implementation that opens a connection via EF Core to verify database reachability | Understand the health check logic or customize the check behavior |
| `EfCoreHealthChecksBuilderExtensions.cs` | AddEmitEfCore extension on IHealthChecksBuilder for registering the health check | Register the health check in an application's DI setup |
| `Emit.EntityFrameworkCore.HealthChecks.csproj` | Project file | Configure package metadata or dependencies |
