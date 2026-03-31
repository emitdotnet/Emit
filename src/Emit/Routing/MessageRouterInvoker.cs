namespace Emit.Routing;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Tracing;
using Microsoft.Extensions.Logging;

/// <summary>
/// Terminal handler invoker that evaluates a route selector and dispatches
/// to the matching sub-pipeline. Participates in fan-out like any other
/// <see cref="IMiddlewarePipeline{TContext}"/>. Unmatched messages are discarded
/// inline when the pre-evaluated unmatched action is <see cref="ErrorAction.DiscardAction"/>;
/// otherwise <see cref="UnmatchedRouteException"/> is thrown for the error policy to handle.
/// </summary>
internal sealed class MessageRouterInvoker<TMessage>(
    string identifier,
    Func<ConsumeContext<TMessage>, object?> routeSelector,
    Dictionary<object, (Type ConsumerType, IMiddlewarePipeline<ConsumeContext<TMessage>> Pipeline)> routedPipelines,
    ILogger logger,
    ErrorAction? unmatchedAction = null) : IMiddlewarePipeline<ConsumeContext<TMessage>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TMessage> context)
    {
        var routeKey = routeSelector(context);

        if (routeKey is null || !routedPipelines.TryGetValue(routeKey, out var match))
        {
            if (unmatchedAction is ErrorAction.DiscardAction)
            {
                logger.LogDebug(
                    "Router '{Identifier}' discarded unmatched message with route key '{RouteKey}'",
                    identifier, routeKey);
                return;
            }

            // No unmatched action was configured by the user — propagate to the error policy
            // so ConsumeErrorMiddleware can dead-letter or discard based on the configured policy.
            throw new UnmatchedRouteException(routeKey);
        }

        // Tag the current Activity with route details (identity is baked into tracing middleware)
        if (Activity.Current is { } activity)
        {
            activity.SetTag(ActivityTagNames.RouteKey, routeKey.ToString());
        }

        logger.LogDebug(
            "Router '{Identifier}' matched route key '{RouteKey}' to {ConsumerType}",
            identifier, routeKey, match.ConsumerType.Name);

        await match.Pipeline.InvokeAsync(context).ConfigureAwait(false);
    }
}
