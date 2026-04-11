namespace Emit.IntegrationTests.Integration;

/// <summary>
/// Toggle that controls whether <see cref="ToggleableConsumer"/> or
/// <see cref="ToggleableBatchConsumer"/> throws. Register as a singleton
/// and flip <see cref="ShouldThrow"/> during the test to simulate failures.
/// </summary>
public sealed class ConsumerToggle
{
    /// <summary>
    /// When <see langword="true"/>, the consumer throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public volatile bool ShouldThrow;
}
