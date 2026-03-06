// Initialise a single-node replica set so that multi-document transactions are available.
// This script runs once on first startup (when /data/db is empty).
// On subsequent startups the replica set configuration is already persisted on disk.
try {
  rs.initiate({
    _id: "rs0",
    members: [{ _id: 0, host: "localhost:27017" }]
  });
} catch (e) {
  // Already initialised — safe to ignore.
}
