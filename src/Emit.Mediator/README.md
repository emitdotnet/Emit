# Emit.Mediator

Structured request/response dispatching for Emit consumers. Instead of handling raw message context in a single catch-all method, you implement small focused handler classes and the mediator routes each message type to the right one automatically.

A composable middleware pipeline wraps every handler invocation, so things like validation, authorization, and logging get applied consistently without you wiring them up everywhere.

## Streaming handlers

A handler can produce a sequence instead of a single response by implementing
`IStreamRequestHandler<TRequest, TResponse>`, dispatched with `CreateStreamAsync`. The handler
runs inside the middleware pipeline for the whole life of the stream, so middleware, observers
and `[Transactional]` wrap every item rather than just the creation of the sequence.

See the Mediator documentation page for usage.

## Decisions

**One pipeline, not two.** The obvious way to add streaming is a parallel pipeline whose
middleware returns `IAsyncEnumerable`. That is what MediatR does, and the consequence there is
that ordinary pipeline behaviors do not run for streams. Since the reason streaming was requested
was to stop cross-cutting behavior diverging between streaming and non-streaming flows, a second
pipeline would have reproduced the problem it was meant to solve. Instead the terminal enumerates
the handler inside the existing `Task`-based pipeline and hands items out through a sink on the
context features, so there is one middleware ecosystem.

**The pipeline must never finish before the handler.** For a stream handler the enumeration is
the work, so the pipeline task completes when the sequence ends and not when it is created.
Returning the handler's `IAsyncEnumerable` directly would complete the pipeline immediately, which
would commit transactions before any item existed and record near-zero durations. If you are
tempted to simplify the channel bridge away, this is why it exists.

**Demand-driven lockstep.** The handler is advanced only when the consumer asks for an item, so it
is always parked at one of its own yield points while the consumer is between items. This is what
lets a consumer stop early without interrupting work in progress: the handler stops at a boundary
it chose, and the request can commit safely. An earlier design let the handler run ahead into a
buffer, which meant a consumer stopping early had to either wait for whatever the handler was in
the middle of, potentially forever, or interrupt it and commit partial state. Neither is
acceptable, and the run-ahead bought nothing that a plain `IAsyncEnumerable` does not already
forgo. If a buffer is ever reintroduced for overlap, that choice comes back with it.

**Failures after an early stop are rethrown from disposal.** A consumer that stopped reading is no
longer watching the sequence, so a failure occurring after the handler finished, a commit that
throws for instance, cannot reach it through the items. The pipeline task is therefore awaited
when the enumerator is disposed and its failure propagates from there. Without this, a failed
commit after an early stop is silent and the caller sees success.

