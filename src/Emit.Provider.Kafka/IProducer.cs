namespace Emit.Provider.Kafka;

using Confluent.Kafka;

/// <summary>
/// A Kafka producer interface that enqueues messages to the transactional outbox.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
/// <remarks>
/// <para>
/// This interface mirrors the shape of Confluent.Kafka's <c>IProducer&lt;TKey, TValue&gt;</c>
/// but does <b>not</b> implement it. Instead, messages are serialized and enqueued to
/// the outbox within the current database transaction. A background worker later processes
/// the outbox and produces messages to Kafka.
/// </para>
/// <para>
/// <b>Important differences from Confluent's producer:</b>
/// <list type="bullet">
/// <item><description>
/// Messages are persisted to the outbox, not sent directly to Kafka.
/// </description></item>
/// <item><description>
/// <c>ProduceAsync</c> returns when the outbox write succeeds, not when Kafka acknowledges.
/// </description></item>
/// <item><description>
/// Transaction methods (InitTransactions, BeginTransaction, etc.) are not available
/// since transactions are managed at the database level.
/// </description></item>
/// <item><description>
/// <c>Flush</c> and <c>Poll</c> are not available as they are not applicable to outbox semantics.
/// </description></item>
/// </list>
/// </para>
/// </remarks>
public interface IProducer<TKey, TValue>
{
    /// <summary>
    /// Asynchronously enqueues a message to the outbox for later production to Kafka.
    /// </summary>
    /// <param name="topic">The target topic.</param>
    /// <param name="message">The message to produce.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A <see cref="DeliveryResult{TKey, TValue}"/> indicating the message was accepted
    /// into the outbox. The <c>Offset</c> contains the outbox sequence number.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The returned <see cref="DeliveryResult{TKey, TValue}"/> indicates the message was
    /// successfully persisted to the outbox, <b>not</b> that it was delivered to Kafka.
    /// Actual Kafka delivery happens asynchronously via the outbox worker.
    /// </para>
    /// <para>
    /// If a database transaction is active (via <c>ITransactionContext</c>), the outbox
    /// entry is written within that transaction, ensuring atomicity with your business data.
    /// </para>
    /// </remarks>
    Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        string topic,
        Message<TKey, TValue> message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously enqueues a message to the outbox for later production to Kafka.
    /// </summary>
    /// <param name="topic">The target topic.</param>
    /// <param name="message">The message to produce.</param>
    /// <param name="deliveryHandler">
    /// Optional callback invoked when the transaction commits.
    /// <b>Note:</b> This callback requires the <c>ITransactionContext.OnCommitted</c> feature
    /// which is not yet available in the Transactional library. Currently, the callback is
    /// invoked immediately after the outbox write succeeds.
    /// </param>
    /// <remarks>
    /// <para>
    /// This method serializes the message and enqueues it to the outbox. The optional
    /// <paramref name="deliveryHandler"/> is intended to fire when the database transaction
    /// commits, but this behavior depends on a future enhancement to the Transactional library.
    /// </para>
    /// <para>
    /// <b>Current behavior:</b> The callback fires after the outbox write succeeds,
    /// regardless of transaction commit status.
    /// </para>
    /// <para>
    /// <b>Future behavior:</b> The callback will fire only after
    /// <c>ITransactionContext.CommitAsync</c> succeeds.
    /// </para>
    /// </remarks>
    void Produce(
        string topic,
        Message<TKey, TValue> message,
        Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null);
}
