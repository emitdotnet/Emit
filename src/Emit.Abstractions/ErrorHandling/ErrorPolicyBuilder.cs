namespace Emit.Abstractions.ErrorHandling;

/// <summary>
/// Builds an error policy by registering exception clauses with optional predicates and actions.
/// </summary>
public sealed class ErrorPolicyBuilder
{
    private readonly List<ErrorClause> clauses = [];
    private ErrorAction? defaultAction;

    /// <summary>
    /// Registers a clause that matches exceptions of type <typeparamref name="TException"/>
    /// and applies the configured action.
    /// </summary>
    /// <typeparam name="TException">The exception type to match (exact or inherited).</typeparam>
    /// <param name="configure">Configures the action to take when matched.</param>
    /// <returns>This builder for chaining.</returns>
    public ErrorPolicyBuilder When<TException>(Action<ErrorPolicyActionBuilder> configure) where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(configure);

        clauses.Add(new ErrorClause
        {
            ExceptionType = typeof(TException),
            Action = BuildAction(configure),
        });

        return this;
    }

    /// <summary>
    /// Registers a clause that matches exceptions of type <typeparamref name="TException"/>
    /// filtered by <paramref name="predicate"/>, and applies the configured action.
    /// </summary>
    /// <typeparam name="TException">The exception type to match (exact or inherited).</typeparam>
    /// <param name="predicate">Predicate to further filter matching exceptions.</param>
    /// <param name="configure">Configures the action to take when matched.</param>
    /// <returns>This builder for chaining.</returns>
    public ErrorPolicyBuilder When<TException>(
        Func<TException, bool> predicate,
        Action<ErrorPolicyActionBuilder> configure) where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(configure);

        clauses.Add(new ErrorClause
        {
            ExceptionType = typeof(TException),
            Predicate = ex => predicate((TException)ex),
            Action = BuildAction(configure),
        });

        return this;
    }

    /// <summary>
    /// Sets the default action applied when no other clause matches or when a clause falls through.
    /// </summary>
    /// <param name="configure">Configures the default error action.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">Default has already been configured.</exception>
    public ErrorPolicyBuilder Default(Action<ErrorPolicyActionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (defaultAction is not null)
        {
            throw new InvalidOperationException($"{nameof(Default)} error action has already been configured.");
        }

        defaultAction = BuildAction(configure);
        return this;
    }

    /// <summary>
    /// Builds the error policy. Validates that a Default action has been configured.
    /// </summary>
    /// <returns>The built error policy.</returns>
    /// <exception cref="InvalidOperationException">Default action was not configured.</exception>
    public ErrorPolicy Build()
    {
        if (defaultAction is null)
        {
            throw new InvalidOperationException(
                $"{nameof(Default)} error action is required when configuring error handling. " +
                $"Call .{nameof(Default)}(...) on the error policy builder.");
        }

        return new ErrorPolicy(clauses.ToList(), defaultAction);
    }

    private static ErrorAction BuildAction(Action<ErrorPolicyActionBuilder> configure)
    {
        var builder = new ErrorPolicyActionBuilder();
        configure(builder);
        return builder.Build();
    }
}
