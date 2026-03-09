namespace Emit.UnitTests;

using global::Emit.Abstractions;

/// <summary>
/// Concrete <see cref="TransportContext"/> for use in unit tests where no real transport is involved.
/// </summary>
internal sealed class TestTransportContext : TransportContext
{
    /// <summary>
    /// Creates a minimal transport context suitable for unit tests.
    /// </summary>
    internal static TestTransportContext Create(IServiceProvider? services = null) => new()
    {
        MessageId = "test-msg",
        Timestamp = DateTimeOffset.UtcNow,
        CancellationToken = CancellationToken.None,
        Services = services!,
        RawKey = null,
        RawValue = null,
        Headers = [],
        ProviderId = "test",
    };
}
