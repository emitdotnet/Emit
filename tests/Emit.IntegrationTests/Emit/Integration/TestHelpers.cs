namespace Emit.IntegrationTests.Integration;

/// <summary>
/// Shared test utilities for integration tests.
/// </summary>
internal static class TestHelpers
{
    private static readonly TimeSpan DefaultPollTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Polls a condition until it returns <c>true</c> or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="failMessage">The message for the <see cref="TimeoutException"/> if the condition is not met.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 30 seconds.</param>
    /// <param name="pollInterval">Interval between polls. Defaults to 200ms.</param>
    /// <exception cref="TimeoutException">The condition was not met within the timeout.</exception>
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        string failMessage,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultPollTimeout);
        var interval = pollInterval ?? DefaultPollInterval;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(interval);
        }

        throw new TimeoutException(failMessage);
    }
}
