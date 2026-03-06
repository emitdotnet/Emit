namespace Emit.Mediator;

using Emit.Abstractions;

/// <summary>
/// Simple in-process response storage for the mediator pattern.
/// Stores the response as <c>object?</c> and returns it typed via <see cref="GetResponse{T}"/>.
/// </summary>
internal sealed class MediatorResponseFeature : IResponseFeature
{
    private object? response;

    /// <inheritdoc />
    public bool HasResponded { get; private set; }

    /// <inheritdoc />
    public Task RespondAsync<TResponse>(TResponse response)
    {
        if (HasResponded)
        {
            throw new InvalidOperationException("A response has already been set for this request.");
        }

        this.response = response;
        HasResponded = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the stored response cast to the expected type.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <returns>The typed response value.</returns>
    /// <exception cref="InvalidOperationException">No response has been set.</exception>
    internal TResponse GetResponse<TResponse>()
    {
        if (!HasResponded)
        {
            throw new InvalidOperationException(
                "No response has been set. The handler must produce a response.");
        }

        return (TResponse)response!;
    }
}
