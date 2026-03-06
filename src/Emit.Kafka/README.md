# Emit.Kafka

Kafka provider for Emit. Connects the outbox pipeline to Kafka so your messages get delivered reliably, in order, exactly once per group — even across restarts, crashes, and the usual distributed systems chaos.

In outbox mode, producers write serialized messages into your database transaction. The background worker handles delivery to Kafka and cleans up on success. In direct mode, messages go straight to Kafka without persistence, for cases where fire-and-forget with some resilience is good enough.

Each producer is configured at registration time with a topic and serializers for both key and value. Sync and async serializers are supported, so Schema Registry-backed formats like Avro, Protobuf, and JSON Schema work out of the box via the companion serializer packages.
