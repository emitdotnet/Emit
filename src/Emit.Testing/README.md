# Emit.Testing

Test helpers that make verifying message delivery less painful. Instead of asserting against a live broker, you wire in a sink that captures messages in memory as they're consumed and assert against it directly in your test. No Kafka, no containers, no waiting.

This is a development dependency — it won't show up in your application's dependency graph.
