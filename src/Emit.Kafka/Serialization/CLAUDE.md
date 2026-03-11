# Serialization/

This directory previously contained `KafkaPayload.cs` (MessagePack-serialized outbox payload).
It was deleted as part of the outbox schema redesign — message data is now stored directly
in `OutboxEntry.Body`, `OutboxEntry.Headers`, and `OutboxEntry.Properties`.
