namespace Emit.Abstractions.ErrorHandling;

/// <summary>
/// An immutable error policy built from <see cref="ErrorPolicyBuilder"/>.
/// Evaluates exceptions against ordered clauses to determine the appropriate action.
/// </summary>
public sealed class ErrorPolicy(IReadOnlyList<ErrorClause> clauses, ErrorAction defaultAction)
{
    /// <summary>
    /// The ordered clauses in this policy.
    /// </summary>
    public IReadOnlyList<ErrorClause> Clauses => clauses;

    /// <summary>
    /// The default action applied when no clause matches.
    /// </summary>
    public ErrorAction DefaultAction => defaultAction;

    /// <summary>
    /// Evaluates the given exception against the policy clauses and returns the matching action.
    /// Clauses are evaluated in registration order, then Default.
    /// </summary>
    /// <param name="exception">The exception to evaluate.</param>
    /// <returns>The error action to take.</returns>
    public ErrorAction Evaluate(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        foreach (var clause in clauses)
        {
            if (!clause.ExceptionType.IsAssignableFrom(exception.GetType()))
            {
                continue;
            }

            if (clause.Predicate is not null && !clause.Predicate(exception))
            {
                continue;
            }

            return clause.Action;
        }

        return defaultAction;
    }
}
