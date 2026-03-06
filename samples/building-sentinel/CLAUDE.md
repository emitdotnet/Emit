# building-sentinel/

Smart building access and security hub sample. All business logic is shared; the startup project selects the persistence backend.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Feature walkthrough, running instructions, and what to watch in each observability tool | Run the sample or understand what each Emit feature demonstrates |
| `building-sentinel.slnx` | Solution file for the sample | Open the sample in an IDE |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `BuildingSentinel.Common/` | Shared domain model, consumers, handlers, endpoints, repositories, and Kafka/mediator configuration | Understand application logic or modify shared behavior |
| `BuildingSentinel.MongoDB/` | MongoDB startup project: Program.cs, MongoDB repository implementations, and transaction factory | Run the MongoDB variant or implement MongoDB-specific persistence |
| `BuildingSentinel.PostgreSQL/` | PostgreSQL startup project: Program.cs, EF Core repository implementations, transaction factory, and DbContext | Run the PostgreSQL variant or implement EF Core-specific persistence |
