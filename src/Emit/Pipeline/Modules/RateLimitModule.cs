namespace Emit.Pipeline.Modules;

using System.Threading.RateLimiting;
using Emit.RateLimiting;

/// <summary>
/// Holds rate limit configuration. Delegates to the existing <see cref="RateLimitBuilder"/>.
/// </summary>
public sealed class RateLimitModule
{
    private Action<RateLimitBuilder>? configure;

    /// <summary>
    /// Gets whether rate limiting has been configured.
    /// </summary>
    public bool IsConfigured => configure is not null;

    /// <summary>
    /// Configures rate limiting.
    /// </summary>
    /// <param name="configure">Action to configure the rate limit builder.</param>
    /// <exception cref="InvalidOperationException">Rate limiting has already been configured.</exception>
    public void Configure(Action<RateLimitBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (IsConfigured)
        {
            throw new InvalidOperationException("Rate limiting has already been configured.");
        }

        this.configure = configure;
    }

    /// <summary>
    /// Builds the rate limiter. <paramref name="queueLimit"/> is typically
    /// <c>workerCount * bufferSize</c>.
    /// </summary>
    /// <param name="queueLimit">Maximum number of requests that can be queued.</param>
    /// <returns>The configured rate limiter, or <c>null</c> if not configured.</returns>
    internal RateLimiter? BuildRateLimiter(int queueLimit)
    {
        if (configure is null)
        {
            return null;
        }

        var builder = new RateLimitBuilder();
        configure(builder);
        return builder.Build(queueLimit);
    }
}
