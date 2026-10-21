namespace Emit.IntegrationTests.Integration;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Shared test utilities for integration tests.
/// </summary>
internal static class TestHelpers
{
    private static readonly TimeSpan DefaultPollTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Returns a service name no other test uses, so tests sharing a database do not see each other's nodes,
    /// leases, daemons or outbox entries.
    /// </summary>
    public static string NewServiceName() => $"svc-{Guid.NewGuid():N}";

    /// <summary>
    /// Names the host's service and shortens leader election, so leadership and daemons move within
    /// seconds. Database calls keep a timeout that a containerized database meets under load.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="serviceName">The service the host belongs to.</param>
    public static void ConfigureFastCoordination(EmitBuilder emit, string serviceName)
    {
        emit.ConfigureLeaderElection(options =>
        {
            options.ServiceName = serviceName;
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(3500);
            options.QueryTimeout = TimeSpan.FromMilliseconds(500);
            options.LeaseDuration = TimeSpan.FromSeconds(7);
            options.NodeRegistrationTtl = TimeSpan.FromSeconds(13);
        });
        emit.ConfigureDaemons(daemons => daemons.DrainTimeout = TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Produces a message through the host's real outbox pipeline, inside a transaction begun with
    /// <see cref="IUnitOfWork"/> and committed.
    /// </summary>
    /// <typeparam name="TKey">The key type of the topic's producer.</typeparam>
    /// <param name="services">The host's service provider. A scope is created for the transaction.</param>
    /// <param name="key">The message key.</param>
    /// <param name="value">The message value.</param>
    /// <param name="flushBeforeCommit">Runs before the commit, for providers that must save tracked changes.</param>
    public static async Task ProduceTransactionallyAsync<TKey>(
        IServiceProvider services,
        TKey key,
        string value,
        Func<IServiceProvider, Task> flushBeforeCommit)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var producer = sp.GetRequiredService<IEventProducer<TKey, string>>();

        await using var transaction = await unitOfWork.BeginAsync();
        await producer.ProduceAsync(new EventMessage<TKey, string>(key, value));
        await flushBeforeCommit(sp);
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Polls a condition until it returns <c>true</c> or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="failMessage">The message for the <see cref="TimeoutException"/> if the condition is not met.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 30 seconds.</param>
    /// <param name="pollInterval">Interval between polls. Defaults to 200ms.</param>
    /// <exception cref="TimeoutException">The condition was not met within the timeout.</exception>
    public static Task WaitUntilAsync(
        Func<bool> condition,
        string failMessage,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null) =>
        WaitUntilAsync(() => Task.FromResult(condition()), failMessage, timeout, pollInterval);

    /// <summary>
    /// Polls an asynchronous condition, such as one that performs an operation and checks its outcome,
    /// until it returns <c>true</c> or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="failMessage">The message for the <see cref="TimeoutException"/> if the condition is not met.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 30 seconds.</param>
    /// <param name="pollInterval">Interval between polls. Defaults to 200ms.</param>
    /// <exception cref="TimeoutException">The condition was not met within the timeout.</exception>
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        string failMessage,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultPollTimeout);
        var interval = pollInterval ?? DefaultPollInterval;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(interval);
        }

        throw new TimeoutException(failMessage);
    }
}
