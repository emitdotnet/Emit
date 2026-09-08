namespace Emit.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Resolves transactional outbox registration state from a built service provider.
/// </summary>
public static class OutboxRegistrationExtensions
{
    /// <summary>
    /// Gets a value indicating whether a persistence provider enabled the transactional outbox.
    /// </summary>
    /// <param name="services">The service provider to query.</param>
    /// <returns><see langword="true"/> when the outbox is enabled; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Integrations must resolve this from a built provider rather than inspecting builder state
    /// while services are still being registered. Whether the outbox is enabled is only final once
    /// every integration has been configured, so a value sampled mid-registration silently depends
    /// on the order the registration calls happened to be made in.
    /// </remarks>
    public static bool IsOutboxEnabled(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetService<OutboxRegistrationMarker>() is not null;
    }
}
