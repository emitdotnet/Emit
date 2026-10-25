namespace Emit.Abstractions.Metrics;

/// <summary>
/// A meter an Emit package records its own metrics on, registered in the service collection so that
/// instrumentation subscribes to it without knowing the package.
/// </summary>
/// <param name="Name">The meter name.</param>
public sealed record PackageMeter(string Name);
