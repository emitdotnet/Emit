# Building Sentinel

A smart building access hub for Acme Corp HQ. Badge readers, motion sensors, and door controllers fire events at an API. Those events hit the transactional outbox, flow through Kafka, and get processed by two independent consumer groups. One group watches for suspicious badge activity and raises alarms. The other tracks every device's heartbeat.

You don't need to do anything to see it work. A simulator fires realistic events automatically the moment you start the app.

## What Emit features you'll see

- **Transactional outbox** — the event is saved to the database and enqueued to Kafka in one atomic transaction. Kill the app mid-flight; nothing is lost.
- **Mediator** — the API endpoint dispatches a command and stays out of everything else.
- **Auto-provisioning** — all required Kafka topics are created at startup. No manual topic creation needed.
- **Dead-letter topic** — permanently failed messages are routed to `building.events.dlt` after retries are exhausted.
- **Kafka router consumer** (`building.classifier`) — routes on `eventType`. Only `access.denied` events are handled; everything else is discarded. This is the point of the router.
- **Kafka simple consumer** (`building.watchdog`) — handles every single event, upserts a heartbeat per device. Total contrast to the router above.
- **FluentValidation** — both consumer groups validate incoming messages. The simulator intentionally produces ~5% invalid events to demonstrate validation failures and dead-lettering.
- **Daemon** (`device-offline-detector`): a background job that must run on exactly one instance. It watches the device heartbeats and raises an alert on `building.alerts`, through the outbox, when a device goes silent. Start a second instance and it stays on one of them; stop that one and it moves to the other. Its writes are fenced, so an instance that wakes up from a long pause cannot raise the same alert again.
- **Health checks** — `/health` endpoint reports Kafka and database health.
- **OpenTelemetry** — Prometheus metrics and Tempo traces, explored directly in Grafana.

## Running it

[Start the infrastructure](../README.md#starting-infrastructure) first, then pick a persistence backend:

```bash
# MongoDB
dotnet run --project BuildingSentinel.MongoDB

# PostgreSQL
dotnet run --project BuildingSentinel.PostgreSQL
```

The API is available at `http://localhost:5000` (Scalar docs at `http://localhost:5000/scalar/v1`). The simulator kicks in 5 seconds after startup.

To watch the daemon move between instances, open a second terminal and start another instance on a different port:

```bash
dotnet run --project BuildingSentinel.MongoDB -- --urls http://localhost:5001
```

PostgreSQL creates its tables at startup with `EnsureCreated`, which does not add tables to an existing database. If you ran this sample before the device offline daemon existed, drop the `building_sentinel` database first (or `docker compose down -v`).

## What to watch for

**In the console**, you'll see a stream of events:

```
[Simulator] Normal access — badge emp-017 granted at Floor 5 - East Wing
[Simulator] Suspect attempt — badge badge-suspect-01 denied at Server Room (Main) — total suspect denials: 2
[RUSH] Morning rush simulation started
```

Keep an eye out for the alarm line, it's the payoff:

```
ALARM: device badge-suspect-01 at Server Room (Main) has 3 consecutive access denials
```

That's `AccessDeniedConsumer` inside the `building.classifier` group firing after the suspect badge crosses the threshold. Every ~10 seconds the suspect tries again, so it happens pretty fast.

**After about 30 seconds**, the simulator stops sending events for `basement-utility`. Once the device has been silent for 90 seconds, the instance running the daemon reports it, once:

```
[Daemon] Device offline detector now runs on this instance (generation 1)
[Simulator] Device basement-utility stops reporting; the device offline daemon will notice
[Daemon] OFFLINE: device basement-utility silent since 14:02:11
```

With two instances, only one of them logs the daemon lines. Stop it (Ctrl+C) and the other picks the daemon up within a couple of heartbeats (15 seconds each by default), under a higher generation. Kill it instead (close the terminal) and the move waits for its registration to expire, a minute and a half or so with the defaults. Tune the detector with `DeviceMonitor:SilentFor` and `DeviceMonitor:CheckEvery`, and the simulator with `Simulation:SilentDevice` (empty to keep every device reporting) and `Simulation:SilentAfterTicks`.

**Every 20 events**, a morning rush plays out: 8 rapid lobby events at 150 ms each. Watch the throughput spike in Grafana.

**In the [observability tools](../README.md#observability-tools):**

- **Grafana** — watch the outbox throughput spike during morning rushes
- **Grafana > Drilldown > Traces** — search for `BuildingSentinel` and watch a single HTTP request fan out into a DB write, an outbox enqueue, and a Kafka consume, all in one trace
- **Kafka UI**: inspect `building.events` and watch `building.classifier` / `building.watchdog` consumer lag; device offline alerts land on `building.alerts`
- **Grafana > Explore**: query `emit_daemon_runs` by `daemon` and `emit_node_id` to see which instance runs `device-offline-detector`, and watch it move
- **Mongo Express / pgAdmin**: browse the `building_events`, `access_denial_alerts`, `device_heartbeats`, `device_offline_alerts`, and `daemon_fences` collections
