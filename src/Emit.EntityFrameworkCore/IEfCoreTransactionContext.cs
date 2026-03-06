namespace Emit.EntityFrameworkCore;

using System.Data.Common;

/// <summary>
/// Represents an EF Core transaction context.
/// </summary>
public interface IEfCoreTransactionContext : Abstractions.ITransactionContext
{
    /// <summary>
    /// Gets the underlying database transaction.
    /// </summary>
    DbTransaction Transaction { get; }
}
