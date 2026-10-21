namespace Emit.IntegrationTests.InMemory;

using Emit.Abstractions.Daemon;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.UnitTests.Coordination;
using Xunit;

/// <summary>
/// Runs the daemon assignment compliance suite against the in-memory store used by the unit test
/// simulations, so its semantics cannot drift from the database providers.
/// </summary>
[Trait("Category", "Integration")]
public class InMemoryDaemonAssignmentCompliance : DaemonAssignmentCompliance
{
    private readonly InMemoryCoordinationStore store = new(TimeProvider.System);

    /// <inheritdoc/>
    protected override IDaemonAssignmentPersistence Persistence => store;

    /// <inheritdoc/>
    public override Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public override Task DisposeAsync() => Task.CompletedTask;
}
