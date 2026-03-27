# Inventory Snapshot

A warehouse management system running multiple app instances. Every 30 seconds, exactly one instance computes a point-in-time inventory snapshot, aggregating stock levels across all products and warehouses, and writes it to the database. Every other instance detects contention and skips that cycle.

There's no API. Two background workers run side by side: a stock simulator that continuously adjusts inventory levels, and the snapshot worker that races for the distributed lock. The interesting thing to watch is the lock itself.

## What Emit features you'll see

- **Distributed lock** — a single non-blocking acquisition attempt per cycle. Either you win the lock and run the snapshot, or you don't and you skip. No queuing, no waiting.
- **Lock TTL** — the lock auto-expires after 30 seconds even if the holder crashes mid-snapshot. The next cycle picks it up cleanly.
- **Dual persistence** — pick MongoDB or PostgreSQL at startup. The lock and snapshot logic are identical; only the repository implementations differ.
- **OpenTelemetry** — Emit's built-in lock metrics (`emit.lock.*`) are wired up out of the box and visible in Grafana as soon as the worker starts.

## Running it

[Start the infrastructure](../README.md#starting-infrastructure) first, then pick a persistence backend:

```bash
# MongoDB
dotnet run --project InventorySnapshot.MongoDB

# PostgreSQL
dotnet run --project InventorySnapshot.PostgreSQL
```

The stock simulator fires immediately; the first snapshot runs within 30 seconds.

To simulate lock contention, open a second terminal and start another instance on a different port:

```bash
Urls=http://localhost:5002 dotnet run --project InventorySnapshot.MongoDB
```

## What to watch for

**In the console**, you'll see the two workers interleaved:

```
[Simulator] Delivery    — BOLT-M8    +34 units
[Simulator] Shipment    — GLOVE-L    -12 units
[Simulator] Discrepancy — PUMP-2HP   -3 units
[Snapshot]  Lock acquired — computing inventory snapshot...
[Snapshot]  #1 — 10 products, 3,987 units across 4 warehouse(s) in 4.2ms
```

With two instances running, one will log `Lock acquired` and the other:

```
[Snapshot] Lock contention — another instance is already running the snapshot. Skipping this cycle.
```

**In the [observability tools](../README.md#observability-tools):**

- **`emit_lock_acquire_duration_seconds`** — how long acquisition takes (near-zero when uncontested)
- **`emit_lock_acquire_retries`** — retry count per acquisition attempt
- **`emit_lock_held_duration_seconds`** — how long locks are held before release
- **`emit_lock_renewal_completed_total`** — count of lock renewal attempts
- **Mongo Express / pgAdmin** — browse the `products` and `snapshots` collections to see stock levels change and snapshot records accumulate
