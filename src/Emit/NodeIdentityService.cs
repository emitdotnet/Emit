namespace Emit;

using Emit.Abstractions;
using Emit.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Default node identity that generates a random <see cref="Guid"/> once at construction and
/// takes the service name from <see cref="LeaderElectionOptions.ServiceName"/>.
/// </summary>
internal sealed class NodeIdentityService(IOptions<LeaderElectionOptions> options) : INodeIdentity
{
    /// <inheritdoc />
    public Guid NodeId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public string ServiceName { get; } = options.Value.ServiceName ?? string.Empty;
}
