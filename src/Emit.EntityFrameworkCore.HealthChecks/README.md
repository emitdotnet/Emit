# Emit.EntityFrameworkCore.HealthChecks

ASP.NET Core health check for Emit's Entity Framework Core persistence provider. Exposes a database connectivity probe via `AddEmitPostgreSQL<TDbContext>()` on `IHealthChecksBuilder`, reusing the `IDbContextFactory<TDbContext>` already registered in your DI container.

Requires `Emit.EntityFrameworkCore`.
