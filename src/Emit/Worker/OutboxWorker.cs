namespace Emit.Worker;

using System.Collections.Concurrent;
using Emit.Abstractions;
using Emit.Configuration;
using Emit.Models;
using Emit.Resilience;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that processes outbox entries with global lease coordination.
/// </summary>
/// <remarks>
/// <para>
/// The worker implements the following processing model:
/// <list type="number">
/// <item><description>Acquires a global lease to ensure single-worker processing.</description></item>
/// <item><description>Polls for group heads to evaluate which groups are eligible for processing.</description></item>
/// <item><description>Fetches batches of entries from eligible groups.</description></item>
/// <item><description>Processes groups in parallel, entries within each group sequentially.</description></item>
/// <item><description>Updates entry status immediately after processing each entry.</description></item>
/// <item><description>Renews the lease periodically to maintain processing rights.</description></item>
/// </list>
/// </para>
/// <para>
/// Circuit breakers prevent repeated retries of failing groups, with cooldown periods.
/// </para>
/// </remarks>
internal sealed class OutboxWorker : BackgroundService
{
    private readonly IOutboxRepository outboxRepository;
    private readonly ILeaseRepository leaseRepository;
    private readonly IEnumerable<IOutboxProvider> providers;
    private readonly ResiliencePolicy resiliencePolicy;
    private readonly EmitOptions emitOptions;
    private readonly WorkerOptions workerOptions;
    private readonly ILogger<OutboxWorker> logger;

    private readonly ConcurrentDictionary<string, CircuitBreakerState> circuitBreakers = new();
    private readonly string workerId;
    private readonly Random jitterRandom = new();

    private CancellationTokenSource? renewalFailureCts;
    private DateTime leaseExpiresAt = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxWorker"/> class.
    /// </summary>
    /// <param name="outboxRepository">The outbox repository.</param>
    /// <param name="leaseRepository">The lease repository.</param>
    /// <param name="providers">The outbox providers.</param>
    /// <param name="resiliencePolicy">The resilience policy.</param>
    /// <param name="emitOptions">The emit options.</param>
    /// <param name="workerOptions">The worker options.</param>
    /// <param name="logger">The logger.</param>
    public OutboxWorker(
        IOutboxRepository outboxRepository,
        ILeaseRepository leaseRepository,
        IEnumerable<IOutboxProvider> providers,
        ResiliencePolicy resiliencePolicy,
        IOptions<EmitOptions> emitOptions,
        IOptions<WorkerOptions> workerOptions,
        ILogger<OutboxWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(outboxRepository);
        ArgumentNullException.ThrowIfNull(leaseRepository);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(resiliencePolicy);
        ArgumentNullException.ThrowIfNull(emitOptions);
        ArgumentNullException.ThrowIfNull(workerOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.outboxRepository = outboxRepository;
        this.leaseRepository = leaseRepository;
        this.providers = providers;
        this.resiliencePolicy = resiliencePolicy;
        this.emitOptions = emitOptions.Value;
        this.workerOptions = workerOptions.Value;
        this.logger = logger;

        workerId = this.workerOptions.WorkerId ?? Guid.NewGuid().ToString();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Outbox worker starting: workerId={WorkerId}, leaseDuration={LeaseDuration}, renewalInterval={RenewalInterval}",
            workerId, workerOptions.LeaseDuration, workerOptions.LeaseRenewalInterval);

        await leaseRepository.EnsureLeaseExistsAsync(stoppingToken).ConfigureAwait(false);

        renewalFailureCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        try
        {
            // Lease acquisition loop
            while (!stoppingToken.IsCancellationRequested)
            {
                var acquired = await TryAcquireLeaseAsync(stoppingToken).ConfigureAwait(false);
                if (!acquired)
                {
                    continue;
                }

                // Start renewal timer
                _ = RunRenewalTimerAsync(renewalFailureCts.Token);

                // Main processing loop
                await ProcessingLoopAsync(renewalFailureCts.Token).ConfigureAwait(false);

                // If we exit the processing loop, the lease was lost or we're shutting down
                break;
            }
        }
        finally
        {
            // Graceful shutdown - release lease
            await ReleaseLeaseAsync().ConfigureAwait(false);
            logger.LogInformation("Outbox worker stopped: workerId={WorkerId}", workerId);
        }
    }

    private async Task<bool> TryAcquireLeaseAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Worker {WorkerId} attempting to acquire lease", workerId);

        var result = await leaseRepository.TryAcquireOrRenewLeaseAsync(
            workerId,
            workerOptions.LeaseDuration,
            cancellationToken).ConfigureAwait(false);

        if (result.Acquired)
        {
            leaseExpiresAt = result.LeaseUntil;
            logger.LogInformation(
                "Worker {WorkerId} acquired lease until {LeaseUntil}",
                workerId, result.LeaseUntil);
            return true;
        }

        // Calculate smart sleep time
        var sleepDuration = CalculateSleepDuration(result.LeaseUntil);

        logger.LogDebug(
            "Worker {WorkerId} failed to acquire lease, held by {CurrentHolder} until {LeaseUntil}, sleeping {SleepDuration}",
            workerId, result.CurrentHolderId, result.LeaseUntil, sleepDuration);

        try
        {
            await Task.Delay(sleepDuration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }

        return false;
    }

    private TimeSpan CalculateSleepDuration(DateTime leaseUntil)
    {
        var timeUntilExpiry = leaseUntil - DateTime.UtcNow;
        if (timeUntilExpiry <= TimeSpan.Zero)
        {
            // Lease already expired, retry immediately with small jitter
            return TimeSpan.FromMilliseconds(jitterRandom.Next(100, 500));
        }

        // Add jitter (0-5 seconds) to prevent thundering herd
        var jitter = TimeSpan.FromSeconds(jitterRandom.NextDouble() * 5);
        return timeUntilExpiry + jitter;
    }

    private async Task RunRenewalTimerAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Renewal timer started for worker {WorkerId}", workerId);

        using var timer = new PeriodicTimer(workerOptions.LeaseRenewalInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var remainingTime = leaseExpiresAt - DateTime.UtcNow;

                // Warn if approaching deadline
                if (remainingTime < workerOptions.LeaseDuration * 0.2)
                {
                    logger.LogWarning(
                        "Worker {WorkerId} lease renewal approaching deadline, {RemainingSeconds}s remaining",
                        workerId, remainingTime.TotalSeconds);
                }

                logger.LogDebug("Worker {WorkerId} attempting lease renewal", workerId);

                var result = await leaseRepository.TryAcquireOrRenewLeaseAsync(
                    workerId,
                    workerOptions.LeaseDuration,
                    cancellationToken).ConfigureAwait(false);

                if (result.Acquired)
                {
                    leaseExpiresAt = result.LeaseUntil;
                    logger.LogDebug(
                        "Worker {WorkerId} renewed lease until {LeaseUntil}",
                        workerId, result.LeaseUntil);
                }
                else
                {
                    logger.LogError(
                        "Worker {WorkerId} failed to renew lease, cancelling processing",
                        workerId);

                    // Fire cancellation to stop all processing tasks
                    await renewalFailureCts!.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Renewal timer stopped for worker {WorkerId}", workerId);
        }
    }

    private async Task ProcessingLoopAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Worker {WorkerId} starting processing loop", workerId);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var processedFull = await ProcessBatchAsync(cancellationToken).ConfigureAwait(false);

                // If batch was full, continue immediately; otherwise sleep
                if (!processedFull)
                {
                    await Task.Delay(emitOptions.PollingInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker {WorkerId} encountered error in processing loop", workerId);
                await Task.Delay(emitOptions.PollingInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        logger.LogDebug("Worker {WorkerId} exiting processing loop", workerId);
    }

    private async Task<bool> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        // Step 1: Get group heads
        logger.LogTrace("Worker {WorkerId} fetching group heads", workerId);
        var groupHeads = await outboxRepository.GetGroupHeadsAsync(
            emitOptions.MaxGroupsPerCycle,
            cancellationToken).ConfigureAwait(false);

        if (groupHeads.Count == 0)
        {
            logger.LogTrace("Worker {WorkerId} found no group heads", workerId);
            return false;
        }

        logger.LogDebug("Worker {WorkerId} found {GroupCount} group heads", workerId, groupHeads.Count);

        // Step 2: Evaluate eligibility for each group head
        var eligibleGroups = EvaluateEligibleGroups(groupHeads);

        if (eligibleGroups.Count == 0)
        {
            logger.LogDebug("Worker {WorkerId} found no eligible groups", workerId);
            return false;
        }

        logger.LogDebug(
            "Worker {WorkerId} has {EligibleCount} eligible groups out of {TotalCount}",
            workerId, eligibleGroups.Count, groupHeads.Count);

        // Step 3: Fetch batch from eligible groups
        logger.LogTrace("Worker {WorkerId} fetching batch from eligible groups", workerId);
        var entries = await outboxRepository.GetBatchAsync(
            eligibleGroups,
            emitOptions.BatchSize,
            cancellationToken).ConfigureAwait(false);

        if (entries.Count == 0)
        {
            logger.LogDebug("Worker {WorkerId} batch returned no entries", workerId);
            return false;
        }

        logger.LogInformation(
            "Worker {WorkerId} processing batch of {EntryCount} entries from {GroupCount} groups",
            workerId, entries.Count, eligibleGroups.Count);

        // Step 4: Partition by GroupKey and process
        var partitionedEntries = entries.GroupBy(e => e.GroupKey);
        var tasks = partitionedEntries.Select(
            group => ProcessGroupAsync(group.Key, group.OrderBy(e => e.Sequence).ToList(), cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return entries.Count >= emitOptions.BatchSize;
    }

    private List<string> EvaluateEligibleGroups(IReadOnlyList<OutboxEntry> groupHeads)
    {
        var eligibleGroups = new List<string>();

        foreach (var head in groupHeads)
        {
            // Check circuit breaker
            if (!IsGroupEligibleByCircuitBreaker(head.GroupKey))
            {
                logger.LogTrace(
                    "Worker {WorkerId} skipping group {GroupKey} due to open circuit breaker",
                    workerId, head.GroupKey);
                continue;
            }

            // Check status and backoff
            if (head.Status == OutboxStatus.Pending)
            {
                logger.LogTrace(
                    "Worker {WorkerId} group {GroupKey} eligible (pending)",
                    workerId, head.GroupKey);
                eligibleGroups.Add(head.GroupKey);
            }
            else if (head.Status == OutboxStatus.Failed)
            {
                if (IsBackoffElapsed(head))
                {
                    logger.LogTrace(
                        "Worker {WorkerId} group {GroupKey} eligible (failed, backoff elapsed)",
                        workerId, head.GroupKey);
                    eligibleGroups.Add(head.GroupKey);
                }
                else
                {
                    logger.LogTrace(
                        "Worker {WorkerId} group {GroupKey} not eligible (backoff not elapsed)",
                        workerId, head.GroupKey);
                }
            }
        }

        return eligibleGroups;
    }

    private bool IsGroupEligibleByCircuitBreaker(string groupKey)
    {
        if (!circuitBreakers.TryGetValue(groupKey, out var state))
        {
            return true;
        }

        switch (state.State)
        {
            case CircuitState.Closed:
                return true;

            case CircuitState.Open:
                // Check if cooldown has elapsed
                if (state.OpenedAt.HasValue &&
                    DateTime.UtcNow - state.OpenedAt.Value >= resiliencePolicy.CircuitBreakerCooldown)
                {
                    state.State = CircuitState.HalfOpen;
                    logger.LogTrace(
                        "Worker {WorkerId} circuit breaker for group {GroupKey} transitioning to HalfOpen",
                        workerId, groupKey);
                    return true;
                }
                return false;

            case CircuitState.HalfOpen:
                return true;

            default:
                return true;
        }
    }

    private bool IsBackoffElapsed(OutboxEntry entry)
    {
        if (!entry.LastAttemptedAt.HasValue)
        {
            return true;
        }

        var backoff = resiliencePolicy.CalculateBackoffDelay(Math.Max(1, entry.RetryCount));
        var timeSinceLastAttempt = DateTime.UtcNow - entry.LastAttemptedAt.Value;

        logger.LogTrace(
            "Worker {WorkerId} backoff check for entry {EntryId}: retryCount={RetryCount}, backoff={Backoff}, elapsed={Elapsed}",
            workerId, entry.Id, entry.RetryCount, backoff, timeSinceLastAttempt);

        return timeSinceLastAttempt >= backoff;
    }

    private async Task ProcessGroupAsync(
        string groupKey,
        List<OutboxEntry> entries,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Worker {WorkerId} processing group {GroupKey} with {EntryCount} entries",
            workerId, groupKey, entries.Count);

        var allSucceeded = true;

        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var success = await ProcessEntryAsync(entry, cancellationToken).ConfigureAwait(false);

            if (!success)
            {
                allSucceeded = false;
                OnGroupFailure(groupKey);
                logger.LogWarning(
                    "Worker {WorkerId} stopping group {GroupKey} processing due to failure at sequence {Sequence}",
                    workerId, groupKey, entry.Sequence);
                break;
            }
        }

        if (allSucceeded && entries.Count > 0)
        {
            OnGroupSuccess(groupKey);
        }
    }

    private async Task<bool> ProcessEntryAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Worker {WorkerId} processing entry {EntryId} from group {GroupKey} sequence {Sequence}",
            workerId, entry.Id, entry.GroupKey, entry.Sequence);

        try
        {
            // Resolve provider
            var provider = providers.FirstOrDefault(p => p.ProviderId == entry.ProviderId);
            if (provider is null)
            {
                throw new InvalidOperationException(
                    $"No provider found for ProviderId '{entry.ProviderId}'. " +
                    $"Ensure the provider is registered in dependency injection.");
            }

            // Process the entry
            await provider.ProcessAsync(entry, cancellationToken).ConfigureAwait(false);

            // Mark as completed
            await outboxRepository.UpdateStatusAsync(
                entry.Id!,
                OutboxStatus.Completed,
                completedAt: DateTime.UtcNow,
                lastAttemptedAt: DateTime.UtcNow,
                retryCount: null,
                latestError: null,
                attempt: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            logger.LogDebug(
                "Worker {WorkerId} completed entry {EntryId} from group {GroupKey}",
                workerId, entry.Id, entry.GroupKey);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Worker {WorkerId} failed to process entry {EntryId} from group {GroupKey}: {Error}",
                workerId, entry.Id, entry.GroupKey, ex.Message);

            // Mark as failed
            var attempt = OutboxAttempt.FromException("ProcessingFailed", ex);
            await outboxRepository.UpdateStatusAsync(
                entry.Id!,
                OutboxStatus.Failed,
                completedAt: null,
                lastAttemptedAt: DateTime.UtcNow,
                retryCount: entry.RetryCount + 1,
                latestError: ex.Message,
                attempt: attempt,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return false;
        }
    }

    private void OnGroupFailure(string groupKey)
    {
        var state = circuitBreakers.GetOrAdd(groupKey, _ => new CircuitBreakerState());

        state.ConsecutiveFailures++;

        logger.LogTrace(
            "Worker {WorkerId} circuit breaker for group {GroupKey}: {Failures} consecutive failures",
            workerId, groupKey, state.ConsecutiveFailures);

        if (state.State == CircuitState.HalfOpen)
        {
            // Failed in half-open state, back to open
            state.State = CircuitState.Open;
            state.OpenedAt = DateTime.UtcNow;
            logger.LogWarning(
                "Worker {WorkerId} circuit breaker for group {GroupKey} re-opened after HalfOpen failure",
                workerId, groupKey);
        }
        else if (state.ConsecutiveFailures >= resiliencePolicy.CircuitBreakerFailureThreshold)
        {
            state.State = CircuitState.Open;
            state.OpenedAt = DateTime.UtcNow;
            logger.LogWarning(
                "Worker {WorkerId} circuit breaker for group {GroupKey} opened after {Failures} failures",
                workerId, groupKey, state.ConsecutiveFailures);
        }
    }

    private void OnGroupSuccess(string groupKey)
    {
        if (circuitBreakers.TryGetValue(groupKey, out var state))
        {
            if (state.State == CircuitState.HalfOpen)
            {
                logger.LogTrace(
                    "Worker {WorkerId} circuit breaker for group {GroupKey} closing after successful HalfOpen attempt",
                    workerId, groupKey);
            }

            state.State = CircuitState.Closed;
            state.ConsecutiveFailures = 0;
            state.OpenedAt = null;
        }
    }

    private async Task ReleaseLeaseAsync()
    {
        try
        {
            var released = await leaseRepository.ReleaseLeaseAsync(workerId).ConfigureAwait(false);
            if (released)
            {
                logger.LogInformation("Worker {WorkerId} released lease", workerId);
            }
            else
            {
                logger.LogDebug("Worker {WorkerId} had no lease to release", workerId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Worker {WorkerId} failed to release lease", workerId);
        }
    }
}
