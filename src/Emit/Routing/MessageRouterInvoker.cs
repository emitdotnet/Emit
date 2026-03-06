namespace Emit.Routing;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.Logging;

/// <summary>
/// Terminal handler invoker that evaluates a route selector and dispatches
/// to the matching sub-pipeline. Participates in fan-out like any other
/// <see cref="IHandlerInvoker{TContext}"/>. Unmatched messages are discarded
/// inline when the pre-evaluated unmatched action is <see cref="ErrorAction.DiscardAction"/>;
/// otherwise <see cref="UnmatchedRouteException"/> is thrown for the error policy to handle.
/// </summary>
internal sealed class MessageRouterInvoker<TMessage>(
    string identifier,
    Func<InboundContext<TMessage>, object?> routeSelector,
    Dictionary<object, (Type ConsumerType, MessageDelegate<InboundContext<TMessage>> Pipeline)> routedPipelines,
    ILogger logger,
    ErrorAction? unmatchedAction = null) : IHandlerInvoker<InboundContext<TMessage>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(InboundContext<TMessage> context)
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

            throw new UnmatchedRouteException(routeKey);
        }

        // Update consumer identity with the resolved route details
        context.Features.Set<IConsumerIdentityFeature>(
            new ConsumerIdentityFeature(identifier, ConsumerKind.Router, match.ConsumerType, routeKey));

        logger.LogDebug(
            "Router '{Identifier}' matched route key '{RouteKey}' to {ConsumerType}",
            identifier, routeKey, match.ConsumerType.Name);

        await match.Pipeline(context).ConfigureAwait(false);
    }
}
