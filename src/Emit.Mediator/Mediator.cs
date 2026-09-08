namespace Emit.Mediator;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Emit.Abstractions;

/// <summary>
/// Scoped mediator implementation. Receives the caller's <see cref="IServiceProvider"/>
/// so that handlers share the same DI scope for transactional consistency.
/// </summary>
internal sealed class Mediator(IServiceProvider services, MediatorConfiguration configuration, TimeProvider timeProvider) : IMediator
{
    /// <inheritdoc />
    public async Task SendAsync(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dispatch = GetDispatch(request.GetType());
        await dispatch(request, services, timeProvider, cancellationToken, null).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var responseFeature = new MediatorResponseFeature();
        var dispatch = GetDispatch(request.GetType());

        await dispatch(
            request,
            services,
            timeProvider,
            cancellationToken,
            features => features.Set<IResponseFeature>(responseFeature)).ConfigureAwait(false);

        return responseFeature.GetResponse<TResponse>();
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStreamAsync<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        // Validated here rather than inside the iterator, so a bad call fails at the call site
        // the way SendAsync does instead of on the first enumeration.
        ArgumentNullException.ThrowIfNull(request);
        var dispatch = GetDispatch(request.GetType());

        return StreamCoreAsync<TResponse>(dispatch, request, cancellationToken);
    }

    private async IAsyncEnumerable<TResponse> StreamCoreAsync<TResponse>(
        MediatorDispatch dispatch,
        object request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Two rendezvous channels give demand-driven lockstep: the handler advances only once
        // the consumer has asked for an item. That keeps the handler parked at one of its own
        // yield points whenever the consumer is between items, so ending the enumeration early
        // stops it at a point it chose rather than interrupting work already in progress.
        var demand = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var items = Channel.CreateBounded<TResponse>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var sink = new ChannelStreamSink<TResponse>(demand.Reader, items.Writer);

        // Started here and observed below. It is never awaited before the consumer reads,
        // because the handler's enumeration is the work the pipeline wraps.
        var pipelineTask = RunPipelineAsync(
            dispatch, request, services, timeProvider, cancellationToken, sink, items.Writer);

        try
        {
            while (true)
            {
                demand.Writer.TryWrite(true);

                if (!await items.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                if (items.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            // Completing demand tells the handler it will not be advanced again. Awaiting the
            // pipeline then lets a failure after an early stop, a commit that throws for
            // instance, reach the caller instead of vanishing into a channel nobody reads.
            demand.Writer.TryComplete();
            await pipelineTask.ConfigureAwait(false);
        }
    }

    /// <remarks>
    /// No check is needed for dispatching through the wrong entry point. A request declares
    /// one shape or the other, declaring both is rejected at registration, and each entry
    /// point only accepts its own marker, so the mismatch cannot be expressed.
    /// </remarks>
    private MediatorDispatch GetDispatch(Type requestType)
    {
        if (!configuration.Dispatchers.TryGetValue(requestType, out var dispatch))
        {
            throw new InvalidOperationException(
                $"No handler is registered for request type '{requestType.Name}'.");
        }

        return dispatch;
    }

    private static async Task RunPipelineAsync<TResponse>(
        MediatorDispatch dispatch,
        object request,
        IServiceProvider services,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        IStreamSink<TResponse> sink,
        ChannelWriter<TResponse> writer)
    {
        try
        {
            await dispatch(
                request,
                services,
                timeProvider,
                cancellationToken,
                features => features.Set(sink)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Completing with the failure surfaces it to a consumer that is still reading, at
            // the point in the sequence where it happened. Rethrowing surfaces it to one that
            // has stopped reading, through the await in the iterator's finally.
            writer.TryComplete(ex);
            throw;
        }

        writer.TryComplete();
    }
}
