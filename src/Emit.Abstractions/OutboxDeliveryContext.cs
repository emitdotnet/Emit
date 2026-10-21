namespace Emit.Abstractions;

/// <summary>
/// The run of the outbox daemon a delivery belongs to.
/// </summary>
/// <param name="FencingKey">
/// Identifies the outbox daemon of this service in this database, the same for every run of it.
/// </param>
/// <param name="Generation">The generation of the run's assignment; later runs have higher ones.</param>
public sealed record OutboxDeliveryContext(Guid FencingKey, long Generation);
