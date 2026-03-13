namespace Emit.MongoDB;

using global::MongoDB.Driver;

/// <summary>
/// Scoped mutable holder for the active MongoDB session. Set by
/// <see cref="MongoUnitOfWork"/> when a transaction begins and cleared on disposal.
/// </summary>
internal sealed class MongoSessionHolder : IMongoSessionAccessor
{
    /// <inheritdoc/>
    public IClientSessionHandle? Session { get; set; }
}
