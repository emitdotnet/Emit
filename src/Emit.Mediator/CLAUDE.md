# Emit.Mediator/

In-process mediator for request/response dispatching with middleware pipeline support.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `Emit.Mediator.csproj` | Mediator project file with Emit.Abstractions dependency | Configure mediator dependencies |
| `IMediator.cs` | Interface for dispatching requests with SendAsync (void) and SendAsync&lt;TResponse&gt; | Understand mediator dispatch API |
| `IRequest.cs` | Marker interfaces IRequest (void) and IRequest&lt;TResponse&gt; for request types | Define new request types |
| `IRequestHandler.cs` | Handler interfaces: IRequestHandler&lt;TRequest&gt; (void) and IRequestHandler&lt;TRequest, TResponse&gt; | Implement request handlers |
| `Mediator.cs` | Scoped implementation dispatching requests to pre-built typed pipelines keyed by request type | Understand mediator dispatch internals |
| `MediatorConfiguration.cs` | Singleton holding pre-built dispatch delegates composed at container build time | Understand pipeline composition |
| `MediatorHandlerInvoker.cs` | Terminal adapter resolving typed handler, invoking it, and writing response to IResponseFeature | Understand handler invocation |
| `MediatorVoidHandlerInvoker.cs` | Terminal adapter for void request handlers without response | Understand void handler invocation |
| `MediatorResponseFeature.cs` | IResponseFeature implementation storing typed responses in mediator patterns | Understand response flow |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `DependencyInjection/` | AddMediator extension, MediatorBuilder, and per-handler builder configuration | Register mediator services or extend mediator DI API |
| `Metrics/` | Mediator-specific metrics and metrics middleware | Monitor mediator performance or customize mediator instrumentation |
| `Observability/` | Mediator observer interface and observer middleware | Implement mediator monitoring or custom instrumentation |
