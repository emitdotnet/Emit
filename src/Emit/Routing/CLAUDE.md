# Routing/

Content-based message routing: maps route keys to typed consumer handlers within a single consumer group.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MessageRouterBuilder.cs` | Fluent builder for configuring content-based message routes mapping route keys to typed consumer handlers | Declare message routes or add new routing capabilities |
| `MessageRouterInvoker.cs` | Terminal invoker evaluating the route selector and dispatching to the matched sub-pipeline | Debug router dispatch behavior or understand route evaluation at runtime |
| `RouterRegistration.cs` | Immutable build-time descriptor for a router with route selector, route entries, and invoker factory | Understand how router configuration flows from build time to service resolution |
| `RouteEntry.cs` | Record holding a single route's key, consumer type, and optional per-route pipeline | Understand the data model captured when a route is registered |
| `RouteHandlerConfigurator.cs` | IInboundConfigurable scoped to per-route callbacks, exposing Use and Filter only | Understand configuration available inside a per-route configure callback |
| `ErrorPolicyBuilderExtensions.cs` | WhenRouteUnmatched convenience extension on ErrorPolicyBuilder for handling UnmatchedRouteException | Configure error handling for unmatched router messages |
| `UnmatchedRouteException.cs` | Exception thrown when no route matches a message; carries the unmatched route key | Handle or inspect unmatched route failures in error policies |
