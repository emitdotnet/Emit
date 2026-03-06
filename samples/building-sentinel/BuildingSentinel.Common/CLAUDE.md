# BuildingSentinel.Common/

Shared application logic for BuildingSentinel: domain, consumers, handlers, endpoints, and Emit configuration. Referenced by both startup projects.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `BuildingSentinel.Common.csproj` | Shared project file | Configure shared dependencies |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Commands/` | SubmitBuildingEventCommand: the command dispatched by the API endpoint through IMediator | Understand the command model or modify command fields |
| `Consumers/` | AccessDeniedConsumer (router target) and DeviceHeartbeatConsumer (simple consumer) | Understand consumer logic or add new consumers |
| `Domain/` | Domain records: BuildingEvent, AccessDenialAlert, DeviceHeartbeat | Understand the domain model or add new domain types |
| `Endpoints/` | Minimal API endpoint registrations for building event submission | Add or modify API endpoints |
| `Extensions/` | KafkaBuilderExtensions and MediatorBuilderExtensions for registering topics and handlers | Understand how Kafka and mediator are configured for the sample |
| `Handlers/` | SubmitBuildingEventHandler: mediator handler that persists and enqueues events transactionally | Understand the transactional outbox pattern or modify command handling |
| `Repositories/` | Repository interfaces: IAlertRepository, IBuildingEventRepository, IDeviceHeartbeatRepository | Implement a new persistence backend or understand the repository contracts |
| `Serialization/` | JsonKafkaSerializer: JSON-based Kafka serializer for building events | Understand or replace the Kafka serialization strategy |
| `Simulation/` | BuildingSimulatorService: background service that fires realistic events automatically after startup | Understand the automatic event generation or tune simulator behavior |
| `Transactions/` | ITransactionFactory: abstraction for creating persistence-backend-specific transactions | Understand how transactions are created across MongoDB and PostgreSQL |
