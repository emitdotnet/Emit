namespace Emit;

using Emit.Abstractions;

/// <summary>
/// Default implementation of <see cref="IConsumerIdentityFeature"/>.
/// </summary>
public sealed class ConsumerIdentityFeature(
    string identifier,
    ConsumerKind kind,
    Type? consumerType = null,
    object? routeKey = null) : IConsumerIdentityFeature
{
    /// <inheritdoc />
    public string Identifier => identifier;

    /// <inheritdoc />
    public ConsumerKind Kind => kind;

    /// <inheritdoc />
    public Type? ConsumerType => consumerType;

    /// <inheritdoc />
    public object? RouteKey => routeKey;
}
