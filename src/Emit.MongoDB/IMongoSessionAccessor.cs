namespace Emit.MongoDB;

using global::MongoDB.Driver;

/// <summary>
/// Provides type-safe access to the active MongoDB session for the current scope.
/// Inject this interface into repositories or services that need to enlist their
/// MongoDB operations in the ambient transaction started by <see cref="IMongoUnitOfWorkTransaction"/>.
/// </summary>
public interface IMongoSessionAccessor
{
    /// <summary>
    /// Gets the active MongoDB client session handle, or <see langword="null"/> if no
    /// transaction is currently in progress.
    /// </summary>
    IClientSessionHandle? Session { get; }
}
