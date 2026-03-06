# Emit.EntityFrameworkCore

Entity Framework Core persistence for Emit. Stores outbox entries in your existing database — you bring the DbContext, Emit handles the rest. Your DBA only has one schema to worry about.

Distributed locking is implemented using advisory database locks, so there's nothing extra to provision. PostgreSQL is supported out of the box via Npgsql.
