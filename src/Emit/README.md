# Emit

A transactional outbox library for .NET. Emit solves the classic dual-write problem: your business data and your outgoing messages commit together or not at all, so you never end up in the sad state where the database saved but the message didn't (or worse, the other way around).

## How it works

Emit supports two operating modes. In **outbox mode**, messages are serialized and written to your database within your transaction. A background worker then picks them up, delivers them to the provider, and deletes them on success — retrying automatically on failure. In **direct mode**, messages go straight to the provider without persistence, which is useful when you care about resilience but not strict transactional guarantees.

Groups of messages are always delivered in order. Different groups are processed in parallel. The library is provider-agnostic: persistence backends and message providers are plugged in separately, and Emit orchestrates everything in between.
