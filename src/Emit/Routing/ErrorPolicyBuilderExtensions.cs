namespace Emit.Routing;

using Emit.Abstractions.ErrorHandling;

/// <summary>
/// Extension methods for <see cref="ErrorPolicyBuilder"/> that provide convenience
/// methods for handling routing-related exceptions.
/// </summary>
public static class ErrorPolicyBuilderExtensions
{
    /// <summary>
    /// Registers a clause that matches <see cref="UnmatchedRouteException"/> and applies the
    /// configured action. This is a convenience method equivalent to
    /// <c>When&lt;UnmatchedRouteException&gt;(configure)</c>.
    /// </summary>
    /// <param name="builder">The error policy builder.</param>
    /// <param name="configure">Configures the action to take when a route is unmatched.</param>
    /// <returns>The builder for chaining.</returns>
    public static ErrorPolicyBuilder WhenRouteUnmatched(
        this ErrorPolicyBuilder builder,
        Action<ErrorPolicyActionBuilder> configure)
    {
        return builder.When<UnmatchedRouteException>(configure);
    }
}
