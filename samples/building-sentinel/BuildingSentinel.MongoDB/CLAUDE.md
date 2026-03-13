# BuildingSentinel.MongoDB/

MongoDB startup project for Building Sentinel. Wires up MongoDB persistence and starts the application.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `Program.cs` | Application entry point: DI setup for MongoDB, Emit, Kafka, mediator, and health checks | Understand startup wiring or modify service registration |
| `appsettings.json` | Default configuration: connection strings, Kafka broker address, topic names | Change connection settings for local development |
| `BuildingSentinel.MongoDB.csproj` | Project file | Configure dependencies |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Repositories/` | MongoDB implementations of IAlertRepository, IBuildingEventRepository, IDeviceHeartbeatRepository | Understand MongoDB query patterns or modify repository implementations |
