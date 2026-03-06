namespace Emit.Abstractions.ErrorHandling;

/// <summary>
/// A single clause in an error policy matching an exception type with an optional predicate and action.
/// </summary>
public sealed class ErrorClause
{
    /// <summary>The exception type to match.</summary>
    public required Type ExceptionType { get; init; }

    /// <summary>Optional predicate to filter exceptions of the matching type.</summary>
    public Func<Exception, bool>? Predicate { get; init; }

    /// <summary>The action to take when matched.</summary>
    public required ErrorAction Action { get; init; }
}
