<p align="center">
  <img src="assets/logo_full_transparent.png" alt="Emit" width="256" />
</p>

<p align="center">
  <a href="https://github.com/emitdotnet/Emit/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/emitdotnet/Emit/ci.yml?branch=main&style=flat-square&label=build" alt="Build Status" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/emitdotnet/Emit?style=flat-square" alt="License" /></a>
</p>

---

> **Alpha** — Emit is under active development. APIs will change between releases. Pin your package versions and do not use in production yet. A stable `1.0.0` release is the goal, but we are not there yet.

A .NET transactional outbox library that actually cares about message ordering.

Emit sits between your application and your message broker, making sure every message gets delivered exactly once and in the right order. If your app crashes mid-write, Emit picks up where you left off. No lost messages, no duplicates, no existential dread.

It supports MongoDB and PostgreSQL (via EF Core) for persistence, Kafka for message delivery, and comes with distributed locking, dead letter queues, and OpenTelemetry baked in. Basically, the boring infrastructure stuff you'd rather not build yourself.

## Documentation

The full documentation lives at **[emitdotnet.github.io/Emit](https://emitdotnet.github.io/Emit)**.

## Samples

Want to see it in action? The [`samples/`](samples/) directory has working examples you can run locally. Check out the [samples README](samples/README.md) for the full list and instructions.

## License

Licensed under [Apache 2.0](LICENSE).
