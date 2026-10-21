namespace Emit.Configuration;

using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

/// <summary>
/// Fills <see cref="LeaderElectionOptions.ServiceName"/> when it is not configured, from the host's
/// application name, falling back to the name of the entry assembly.
/// </summary>
internal sealed class LeaderElectionOptionsDefaults(IHostEnvironment? hostEnvironment) : IPostConfigureOptions<LeaderElectionOptions>
{
    /// <inheritdoc/>
    public void PostConfigure(string? name, LeaderElectionOptions options)
    {
        if (options.ServiceName is not null)
        {
            return;
        }

        var applicationName = hostEnvironment?.ApplicationName;
        options.ServiceName = string.IsNullOrWhiteSpace(applicationName)
            ? Assembly.GetEntryAssembly()?.GetName().Name
            : applicationName;
    }
}
