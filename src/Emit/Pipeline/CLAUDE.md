# Pipeline/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MessagePipelineBuilder.cs` | Middleware levels: registration checked against the level's context, and composition of several levels into one pipeline, closing generic type definitions with the message type | Understand pipeline construction or modify builder behavior |
| `ProducerPipelineComposer.cs` | Composes a producer pipeline: built-in tracing, metrics and observers around the user middleware levels | Understand producer pipeline composition order |
| `InboundFilterExtensions.cs` | Extension methods for registering consumer filters on inbound pipelines | Register consumer filters or understand filter registration |
| `BatchConsumerAdapter.cs` | Terminal adapter resolving an IBatchConsumer from the service provider and invoking it | Understand batch handler resolution or debug batch consumer invocation |
| `ConsumerPipelineComposer.cs` | Composes a fully-layered inbound middleware pipeline for a single consumer or router entry | Understand pipeline composition order or debug middleware layering |
| `ConsumerPipelineEntry.cs` | Pairs consumer identity metadata with its composed pipeline delegate | Understand how consumer workers resolve and invoke pipelines |
| `HandlerInvoker.cs` | Terminal adapter resolving a consumer handler from the service provider and invoking it | Understand handler resolution or debug consumer invocation |
| `EmptyPipeline.cs` | No-op terminal pipeline that completes immediately; used as the default terminal when no further processing is needed | Understand the default terminal pipeline or debug no-op pipeline behavior |
| `IMiddlewarePipeline.cs` | A composed pipeline processing a context: what middleware and terminals are chained into | Understand the pipeline node abstraction or implement a terminal |
| `MiddlewarePipeline.cs` | Bridges IMiddleware into IMiddlewarePipeline by capturing the next pipeline reference | Understand how middleware instances are chained into the pipeline |
| `OutboxTerminalBuilder.cs` | Builds a terminal delegate that enqueues messages to the transactional outbox | Understand outbox enqueue flow or debug transaction/trace propagation |
| `BatchPerItemAdapter.cs` | Lifts item-level middleware (filters, validation) onto a batch: runs it per item and narrows the batch to the items that pass | Understand per-item filtering and validation of batches |
| `NullPayloadFilterExtensions.cs` | `SkipNullPayloads()`: drops null-payload messages, such as tombstones, before the handler sees them | Skip tombstones on a consumer group |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Modules/` | The filter and validation middleware of consumer pipelines | Understand consumer filtering or validation |
