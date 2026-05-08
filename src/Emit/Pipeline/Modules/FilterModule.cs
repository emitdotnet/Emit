namespace Emit.Pipeline.Modules;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Holds filter configuration for a consumer group. Filters gate whether each message
/// continues through the pipeline. When a filter returns <c>false</c>, the message is
/// silently dropped — no dead-lettering, no exception.
/// </summary>
/// <typeparam name="TValue">The message type to filter.</typeparam>
public sealed class FilterModule<TValue>
{
    private readonly List<FilterModuleEntry<TValue>> entries = [];

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
        entries.Add(FilterModuleEntry<TValue>.FromPredicate(predicate));
    }

    /// <summary>
    /// Registers a class-based filter resolved from DI for each message.
    /// </summary>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    public void AddFilterType<TFilter>()
        where TFilter : class, IConsumerFilter<TValue>
    {
        entries.Add(FilterModuleEntry<TValue>.FromType(typeof(TFilter)));
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

    /// <summary>
    /// Returns all filter entries in registration order.
    /// </summary>
    internal IReadOnlyList<FilterModuleEntry<TValue>> GetEntries() => entries;

    /// <summary>
    /// Evaluates all registered filters against the given context. Returns <c>false</c>
    /// if any filter rejects the message.
    /// </summary>
    internal async ValueTask<bool> EvaluateAsync(
        ConsumeContext<TValue> context,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            bool passes;

            if (entry.Predicate is not null)
            {
                passes = await entry.Predicate(context, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var filter = (IConsumerFilter<TValue>)ActivatorUtilities.GetServiceOrCreateInstance(
                    context.Services, entry.FilterType!);
                passes = await filter.ShouldConsumeAsync(context, cancellationToken).ConfigureAwait(false);
            }

            if (!passes)
                return false;
        }

        return true;
    }
}

/// <summary>
/// Represents a single filter registration: either a predicate or a class-based filter type.
/// </summary>
internal sealed class FilterModuleEntry<TValue>
{
    /// <summary>The async predicate, or <c>null</c> if this is a type-based entry.</summary>
    public Func<ConsumeContext<TValue>, CancellationToken, ValueTask<bool>>? Predicate { get; private init; }

    /// <summary>The filter type, or <c>null</c> if this is a predicate entry.</summary>
    public Type? FilterType { get; private init; }

    internal static FilterModuleEntry<TValue> FromPredicate(
        Func<ConsumeContext<TValue>, CancellationToken, ValueTask<bool>> predicate)
        => new() { Predicate = predicate };

    internal static FilterModuleEntry<TValue> FromType(Type filterType)
        => new() { FilterType = filterType };
}
