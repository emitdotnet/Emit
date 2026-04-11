# Pipeline/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MessagePipelineBuilder.cs` | Concrete IMessagePipelineBuilder implementation registering middleware and building composed pipeline chains | Understand pipeline construction or modify builder behavior |
| `PipelineConfigurableExtensions.cs` | UseInbound, UseOutbound, and Use&lt;TMessage&gt; extension methods for middleware registration on pipeline-configurable builders | Add middleware via pipeline configuration API |
| `InboundFilterExtensions.cs` | Extension methods for registering consumer filters on inbound pipelines | Register consumer filters or understand filter registration |
| `ConsumerFilterMiddleware.cs` | Middleware invoking registered consumer filters in the inbound pipeline | Understand consumer filter invocation or debug filter execution |
| `BatchConsumerAdapter.cs` | Terminal adapter resolving an IBatchConsumer from the service provider and invoking it | Understand batch handler resolution or debug batch consumer invocation |
| `ConsumerPipelineComposer.cs` | Composes a fully-layered inbound middleware pipeline for a single consumer or router entry | Understand pipeline composition order or debug middleware layering |
| `ConsumerPipelineEntry.cs` | Pairs consumer identity metadata with its composed pipeline delegate | Understand how consumer workers resolve and invoke pipelines |
| `HandlerInvoker.cs` | Terminal adapter resolving a consumer handler from the service provider and invoking it | Understand handler resolution or debug consumer invocation |
| `EmptyPipeline.cs` | No-op terminal pipeline that completes immediately; used as the default terminal when no further processing is needed | Understand the default terminal pipeline or debug no-op pipeline behavior |
| `MiddlewarePipeline.cs` | Bridges IMiddleware into IMiddlewarePipeline by capturing the next pipeline reference | Understand how middleware instances are chained into the pipeline |
| `OutboxTerminalBuilder.cs` | Builds a terminal delegate that enqueues messages to the transactional outbox | Understand outbox enqueue flow or debug transaction/trace propagation |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Modules/` | Deferred pipeline feature modules for circuit breaker, error, rate limit, retry, and validation configuration | Understand how pipeline features are configured at build time or add new pipeline modules |
