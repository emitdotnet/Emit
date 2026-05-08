namespace Emit.Pipeline.Modules;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Holds filter configuration for a consumer group and acts as the inbound middleware that
/// evaluates registered filters in order. When any filter returns <see langword="false"/>,
/// the message is silently dropped: no dead-lettering, no exception. Implements
/// <see cref="IMiddleware{TContext}"/> directly so it can be inserted into the pipeline
/// without an intermediate adapter.
/// </summary>
/// <typeparam name="TValue">The message type to filter.</typeparam>
public sealed class FilterMiddleware<TValue> : IMiddleware<ConsumeContext<TValue>>
{
    private readonly List<FilterEntry<TValue>> entries = [];

    /// <summary>
    /// Gets whether any filters have been registered.
    /// </summary>
    public bool HasEntries => entries.Count > 0;

    /// <summary>
    /// Registers an async predicate filter. When the predicate returns <c>false</c>,
    /// the message is dropped silently.
    /// </summary>
    /// <param name="predicate">The async predicate.</param>
    public void AddPredicate(Func<ConsumeContext<TValue>, CancellationToken, ValueTask<bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        entries.Add(new FilterEntry<TValue>(predicate, FilterType: null));
    }

    /// <summary>
    /// Registers a class-based filter resolved from DI for each message.
    /// </summary>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    public void AddFilterType<TFilter>()
        where TFilter : class, IConsumerFilter<TValue>
    {
        entries.Add(new FilterEntry<TValue>(Predicate: null, typeof(TFilter)));
    }

    /// <summary>
    /// Registers the class-based filter types in the service collection.
    /// </summary>
    public void RegisterServices(IServiceCollection services)
    {
        foreach (var entry in entries)
        {
            if (entry.FilterType is not null)
            {
                services.TryAddTransient(entry.FilterType);
            }
        }
    }

    /// <inheritdoc />
    public async Task InvokeAsync(
        ConsumeContext<TValue> context,
        IMiddlewarePipeline<ConsumeContext<TValue>> next)
    {
        foreach (var entry in entries)
        {
            var passes = entry.Predicate is not null
                ? await entry.Predicate(context, context.CancellationToken).ConfigureAwait(false)
                : await ((IConsumerFilter<TValue>)ActivatorUtilities.GetServiceOrCreateInstance(
                        context.Services, entry.FilterType!))
                    .ShouldConsumeAsync(context, context.CancellationToken).ConfigureAwait(false);

            if (!passes)
                return;
        }

        await next.InvokeAsync(context).ConfigureAwait(false);
    }
}

/// <summary>
/// A single filter registration: either a predicate or a class-based filter type. Exactly
/// one of <paramref name="Predicate"/> and <paramref name="FilterType"/> is non-null.
/// </summary>
internal sealed record FilterEntry<TValue>(
    Func<ConsumeContext<TValue>, CancellationToken, ValueTask<bool>>? Predicate,
    Type? FilterType);
