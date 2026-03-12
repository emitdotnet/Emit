namespace Emit.Pipeline.Modules;

using Emit.Abstractions.ErrorHandling;

/// <summary>
/// Holds the error policy configuration. Always present on every transport builder —
/// error handling is universal.
/// </summary>
public sealed class ErrorModule
{
    private Action<ErrorPolicyBuilder>? configure;

    /// <summary>
    /// Configures the error policy. Can be called at most once.
    /// </summary>
    /// <param name="configure">Action to configure the error policy builder.</param>
    /// <exception cref="InvalidOperationException">Error policy has already been configured.</exception>
    public void Configure(Action<ErrorPolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (this.configure is not null)
        {
            throw new InvalidOperationException("OnError has already been configured.");
        }

        this.configure = configure;
    }

    /// <summary>
    /// Builds the error policy. Returns <c>null</c> when not configured
    /// (<c>ConsumeErrorMiddleware</c> will default to discard-with-warning behavior).
    /// </summary>
    internal ErrorPolicy? BuildPolicy()
    {
        if (configure is null)
        {
            return null;
        }

        var builder = new ErrorPolicyBuilder();
        configure(builder);
        return builder.Build();
    }
}
