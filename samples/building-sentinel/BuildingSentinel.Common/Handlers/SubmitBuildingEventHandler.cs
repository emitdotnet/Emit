namespace BuildingSentinel.Common.Handlers;

using BuildingSentinel.Common.Commands;
using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using Emit.Abstractions;
using Emit.Mediator;

/// <summary>
/// Handles <see cref="SubmitBuildingEventCommand"/> by persisting the event and enqueuing it
/// to the Kafka outbox. The <see cref="TransactionalAttribute"/> ensures that all writes
/// (business data + outbox entry) are committed atomically.
/// </summary>
[Transactional]
public sealed class SubmitBuildingEventHandler(
    IBuildingEventRepository eventRepository,
    IEventProducer<string, BuildingEvent> producer) : IRequestHandler<SubmitBuildingEventCommand>
{
    public async Task HandleAsync(
        SubmitBuildingEventCommand request,
        CancellationToken cancellationToken = default)
    {
        await eventRepository.InsertAsync(request.Event, cancellationToken).ConfigureAwait(false);

        await producer.ProduceAsync(
            request.Event.DeviceId, request.Event,
            cancellationToken).ConfigureAwait(false);
    }
}
