namespace BuildingSentinel.Common.Handlers;

using BuildingSentinel.Common.Commands;
using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using BuildingSentinel.Common.Transactions;
using Emit.Abstractions;
using Emit.Mediator;

/// <summary>
/// Handles <see cref="SubmitBuildingEventCommand"/> by opening a database transaction, persisting the event,
/// enqueuing it to the Kafka outbox, and committing — guaranteeing exactly-once delivery to Kafka even in the presence of failures.
/// </summary>
public sealed class SubmitBuildingEventHandler(
    IEmitContext emitContext,
    IBuildingEventRepository eventRepository,
    IEventProducer<string, BuildingEvent> producer,
    ITransactionFactory transactionFactory) : IRequestHandler<SubmitBuildingEventCommand>
{
    public async Task HandleAsync(
        SubmitBuildingEventCommand request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await transactionFactory
            .BeginTransactionAsync(emitContext, cancellationToken)
            .ConfigureAwait(false);

        await eventRepository.SaveAsync(request.Event, cancellationToken).ConfigureAwait(false);

        await producer.ProduceAsync(
            new EventMessage<string, BuildingEvent>(request.Event.DeviceId, request.Event),
            cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
