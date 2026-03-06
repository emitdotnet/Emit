namespace BuildingSentinel.Common.Commands;

using BuildingSentinel.Common.Domain;
using Emit.Mediator;

/// <summary>
/// Requests that a <see cref="BuildingEvent"/> be persisted and published to the Kafka outbox in a single transaction.
/// </summary>
public sealed record SubmitBuildingEventCommand(BuildingEvent Event) : IRequest;
