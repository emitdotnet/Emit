namespace Emit.MongoDB;

using Emit.Abstractions;
using global::MongoDB.Driver;

/// <summary>
/// A MongoDB-specific unit of work transaction that exposes the underlying
/// <see cref="IClientSessionHandle"/> for use with MongoDB operations that
/// require an active session (e.g., collection reads and writes within the transaction).
/// </summary>
public interface IMongoUnitOfWorkTransaction : IUnitOfWorkTransaction
{
    /// <summary>
    /// Gets the MongoDB client session handle for the active transaction.
    /// </summary>
    IClientSessionHandle Session { get; }
}
