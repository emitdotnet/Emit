namespace Emit.MongoDB;

using global::MongoDB.Driver;

/// <summary>
/// Represents a MongoDB transaction context.
/// </summary>
public interface IMongoTransactionContext : Abstractions.ITransactionContext
{
    /// <summary>
    /// Gets the MongoDB client session handle.
    /// </summary>
    IClientSessionHandle Session { get; }
}
