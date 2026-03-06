# Building Sentinel

A smart building access hub for Acme Corp HQ. Badge readers, motion sensors, and door controllers fire events at an API. Those events hit the transactional outbox, flow through Kafka, and get processed by two independent consumer groups. One group watches for suspicious badge activity and raises alarms. The other tracks every device's heartbeat.

You don't need to do anything to see it work. A simulator fires realistic events automatically the moment you start the app.

## What Emit features you'll see

- **Transactional outbox** — the event is saved to the database and enqueued to Kafka in one atomic transaction. Kill the app mid-flight; nothing is lost.
- **Mediator** — the API endpoint dispatches a command and stays out of everything else.
- **Kafka router consumer** (`building.classifier`) — routes on `eventType`. Only `access.denied` events are handled; everything else is discarded. This is the point of the router.
- **Kafka simple consumer** (`building.watchdog`) — handles every single event, upserts a heartbeat per device. Total contrast to the router above.
- **OpenTelemetry** — Prometheus metrics + Tempo traces, explored directly in Grafana.
- **Dual persistence** — pick MongoDB or PostgreSQL at startup. All business logic is shared.

## Running it

**1. Start the infrastructure** (from the `samples/` directory):

```bash
docker compose up -d
```

Give MongoDB ~20 seconds to elect its replica-set primary. Yes, it needs a replica set. Yes, it's a single node. Transactions require it.

**2. Start the app:**

```bash
# MongoDB
dotnet run --project BuildingSentinel.MongoDB

# PostgreSQL
dotnet run --project BuildingSentinel.PostgreSQL
```

That's it. The simulator kicks in 5 seconds after startup.

## What to watch for

**In the console** — you'll see a stream of events like:

```
[Simulator] Normal access — badge emp-017 granted at Floor 5 - East Wing
[Simulator] Suspect attempt — badge badge-suspect-01 denied at Server Room (Main) — total suspect denials: 2
[RUSH] Morning rush simulation started
```

Keep an eye out for the alarm line — it's the payoff:

```
ALARM: device badge-suspect-01 at Server Room (Main) has 3 consecutive access denials
```

That's `AccessDeniedConsumer` inside the `building.classifier` group firing after the suspect badge crosses the threshold. Every ~10 seconds the suspect tries again, so it happens pretty fast.

**Every 20 events**, a morning rush plays out — 8 rapid lobby events at 150 ms each. Watch the throughput spike in Grafana.

**In the tools** (see the [root README](../README.md) for URLs):

- **Grafana** — watch the outbox throughput spike during morning rushes
- **Grafana → Drilldown → Traces** — search for `BuildingSentinel` and watch a single HTTP request fan out into a DB write, an outbox enqueue, and a Kafka consume — all in one trace
- **Kafka UI** — inspect `building.events` and watch `building.classifier` / `building.watchdog` consumer lag
- **Mongo Express / pgAdmin** — browse the `building_events`, `access_denial_alerts`, and `device_heartbeats` collections

## Stopping

```bash
docker compose down        # keep your data
docker compose down -v     # nuke everything
```
