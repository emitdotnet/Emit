# BuildingSentinel.PostgreSQL/

PostgreSQL startup project for Building Sentinel. Wires up EF Core persistence and starts the application.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `Program.cs` | Application entry point: DI setup for EF Core + Npgsql, Emit, Kafka, mediator, and health checks | Understand startup wiring or modify service registration |
| `SampleDbContext.cs` | EF Core DbContext with entity sets and Emit model builder integration | Understand the schema or modify entity configuration |
| `appsettings.json` | Default configuration: connection string, Kafka broker address, topic names | Change connection settings for local development |
| `BuildingSentinel.PostgreSQL.csproj` | Project file | Configure dependencies |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Entities/` | EF Core entity classes for building events, access denial alerts, and device heartbeats | Understand the relational schema or add new entities |
| `Repositories/` | EF Core implementations of IAlertRepository, IBuildingEventRepository, IDeviceHeartbeatRepository | Understand EF Core query patterns or modify repository implementations |
| `Transactions/` | EfTransactionFactory implementing ITransactionFactory using EF Core DbContext transactions | Understand how PostgreSQL transactions are created |
