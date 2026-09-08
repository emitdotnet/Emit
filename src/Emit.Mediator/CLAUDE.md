# Emit.Mediator/

In-process mediator for request/response dispatching with middleware pipeline support.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Mediator package description and usage overview | Understand what this package provides |
| `Emit.Mediator.csproj` | Mediator project file with Emit.Abstractions dependency | Configure mediator dependencies |
| `IMediator.cs` | Interface for dispatching requests: SendAsync (void), SendAsync&lt;TResponse&gt;, and CreateStreamAsync&lt;TResponse&gt; | Understand mediator dispatch API |
| `IRequest.cs` | Marker interfaces IRequest (void) and IRequest&lt;TResponse&gt; for request types | Define new request types |
| `IStreamRequest.cs` | Marker interface for requests that produce a sequence of responses | Define a streaming request type |
| `IStreamRequestHandler.cs` | Handler interface returning IAsyncEnumerable&lt;TResponse&gt; | Implement a streaming handler |
| `IStreamSink.cs` | Internal demand-driven sink carrying stream items out of the pipeline | Understand how items leave the pipeline |
| `ChannelStreamSink.cs` | Sink implementation pairing a demand channel with an item channel for lockstep | Understand stream back-pressure and early stop |
| `MediatorStreamHandlerInvoker.cs` | Terminal advancing a stream handler one item per request from the consumer | Understand stream handler invocation |
| `IRequestHandler.cs` | Handler interfaces: IRequestHandler&lt;TRequest&gt; (void) and IRequestHandler&lt;TRequest, TResponse&gt; | Implement request handlers |
| `Mediator.cs` | Scoped implementation dispatching requests to pre-built typed pipelines keyed by request type | Understand mediator dispatch internals |
| `MediatorConfiguration.cs` | Singleton holding pre-built dispatch delegates composed at container build time | Understand pipeline composition |
| `MediatorContext.cs` | Pipeline context for mediator request/response pipelines carrying the typed request through the middleware chain | Understand mediator context model or implement mediator middleware |
| `MediatorHandlerInvoker.cs` | Terminal adapter resolving typed handler, invoking it, and writing response to IResponseFeature | Understand handler invocation |
| `MediatorVoidHandlerInvoker.cs` | Terminal adapter for void request handlers without response | Understand void handler invocation |
| `MediatorResponseFeature.cs` | IResponseFeature implementation storing typed responses in mediator patterns | Understand response flow |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `DependencyInjection/` | AddMediator extension, MediatorBuilder, and per-handler builder configuration | Register mediator services or extend mediator DI API |
| `Metrics/` | Mediator-specific metrics and metrics middleware | Monitor mediator performance or customize mediator instrumentation |
| `Observability/` | Mediator observer interface and observer middleware | Implement mediator monitoring or custom instrumentation |
