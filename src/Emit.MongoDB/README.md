# Emit.MongoDB

MongoDB persistence backend for Emit. Stores outbox entries in your existing MongoDB database, with atomic sequence generation and optional sharding support for when you need to scale horizontally without losing ordering guarantees.

You bring your own client and database — Emit doesn't manage the connection. Collections and indexes are created automatically on startup, so there's no migration step. The outbox collection is designed to be used as a shard key on group key, which keeps related entries co-located and avoids scatter-gather queries at scale.

Integrates with the [Transactional](https://github.com/ChuckNovice/transactional) library for MongoDB multi-document transactions, so your business writes and outbox writes commit atomically.
