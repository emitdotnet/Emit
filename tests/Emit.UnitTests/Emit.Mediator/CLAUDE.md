# Emit.Mediator/

Unit tests for the Emit.Mediator library: builder, DI registration, handler invoker, middleware, response feature, and end-to-end mediator dispatch.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MediatorBuilderTests.cs` | Unit tests for MediatorBuilder configuration and handler registration | Debugging mediator builder test failures |
| `MediatorEmitBuilderExtensionsTests.cs` | Unit tests for AddMediator DI extension method | Debugging mediator DI registration test failures |
| `MediatorHandlerInvokerTests.cs` | Unit tests for handler invoker dispatch and exception propagation | Debugging handler invoker test failures |
| `MediatorMiddlewareTests.cs` | Unit tests for mediator middleware pipeline execution order | Debugging mediator middleware test failures |
| `MediatorResponseFeatureTests.cs` | Unit tests for mediator response feature construction and access | Debugging mediator response feature test failures |
| `MediatorTests.cs` | Unit tests for IMediator.SendAsync dispatch and response | Debugging mediator send test failures |
| `MediatorDispatchShapeTests.cs` | Behavior every dispatch shape (void, response, stream) shares, run against each shape | Debugging mediator dispatch test failures |
| `MediatorStreamTests.cs` | Stream handlers running inside the middleware pipeline for the whole life of the stream | Debugging mediator stream test failures |
| `MediatorUnitOfWorkCallbackTests.cs` | Unit of work callbacks registered by mediator handlers through the real pipeline | Debugging mediator callback test failures |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Metrics/` | Tests for mediator metrics middleware | Debugging mediator metrics test failures |
| `Observability/` | Tests for mediator observer middleware | Debugging mediator observer test failures |
