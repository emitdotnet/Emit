namespace Emit;

using Emit.Abstractions;

/// <summary>
/// Default node identity that generates a random <see cref="Guid"/> once at construction.
/// </summary>
internal sealed class NodeIdentityService : INodeIdentity
{
    /// <inheritdoc />
    public Guid NodeId { get; } = Guid.NewGuid();
}
