namespace Emit;

using Emit.Abstractions;

/// <summary>
/// Default implementation of <see cref="IRetryAttemptFeature"/>.
/// </summary>
public sealed class RetryAttemptFeature(int attempt) : IRetryAttemptFeature
{
    /// <inheritdoc />
    public int Attempt => attempt;
}
