# Emit.Mediator

Structured request/response dispatching for Emit consumers. Instead of handling raw message context in a single catch-all method, you implement small focused handler classes and the mediator routes each message type to the right one automatically.

A composable middleware pipeline wraps every handler invocation, so things like validation, authorization, and logging get applied consistently without you wiring them up everywhere.
