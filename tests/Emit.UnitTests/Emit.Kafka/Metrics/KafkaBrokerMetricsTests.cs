namespace Emit.Kafka.Tests.Metrics;

using System.Diagnostics.Metrics;
using global::Emit.Abstractions.Metrics;
using global::Emit.Kafka.Metrics;
using Xunit;

[Collection("MetricsTests")]
public sealed class KafkaBrokerMetricsTests : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly List<(double Value, KeyValuePair<string, object?>[] Tags)> doubleMeasurements = [];
    private readonly List<(long Value, KeyValuePair<string, object?>[] Tags)> longMeasurements = [];
    private readonly List<(int Value, KeyValuePair<string, object?>[] Tags)> intMeasurements = [];

    public KafkaBrokerMetricsTests()
    {
        listener.InstrumentPublished = (instrument, listenerInstance) =>
        {
            if (instrument.Meter.Name == MeterNames.EmitKafkaBroker)
            {
                listenerInstance.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            doubleMeasurements.Add((value, tags.ToArray()));
        });

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            longMeasurements.Add((value, tags.ToArray()));
        });

        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
        {
            intMeasurements.Add((value, tags.ToArray()));
        });

        listener.Start();
    }

    public void Dispose()
    {
        listener.Dispose();
    }

    [Fact]
    public void GivenValidStatistics_WhenHandleStatistics_ThenBrokerMetricsAreParsedCorrectly()
    {
        // Arrange
        var json = """
        {
          "msg_cnt": 42,
          "msg_size": 8192,
          "brokers": {
            "broker1": {
              "nodeid": 1,
              "state": "UP",
              "rtt": { "p50": 1500, "p95": 3000, "p99": 5000 },
              "throttle": { "avg": 100 },
              "outbuf_msg_cnt": 5,
              "waitresp_msg_cnt": 3,
              "req_timeouts": 2,
              "txerrs": 1,
              "txbytes": 1024000,
              "txretries": 10,
              "int_latency": { "p50": 200, "p95": 500, "p99": 1000 },
              "connects": 15,
              "disconnects": 3
            }
          }
        }
        """;

        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.HandleStatistics(json);
        listener.RecordObservableInstruments();

        // Assert
        // RTT converted from microseconds to seconds
        Assert.Contains(doubleMeasurements, m =>
            m.Value == 1500.0 / 1_000_000.0 &&
            HasTag(m.Tags, "broker_id", 1) &&
            HasTag(m.Tags, "percentile", "p50"));
        Assert.Contains(doubleMeasurements, m =>
            m.Value == 3000.0 / 1_000_000.0 &&
            HasTag(m.Tags, "broker_id", 1) &&
            HasTag(m.Tags, "percentile", "p95"));
        Assert.Contains(doubleMeasurements, m =>
            m.Value == 5000.0 / 1_000_000.0 &&
            HasTag(m.Tags, "broker_id", 1) &&
            HasTag(m.Tags, "percentile", "p99"));

        // Throttle converted from milliseconds to seconds
        Assert.Contains(doubleMeasurements, m =>
            m.Value == 100.0 / 1_000.0 &&
            HasTag(m.Tags, "broker_id", 1));

        // Message counts
        Assert.Contains(intMeasurements, m =>
            m.Value == 5 &&
            HasTag(m.Tags, "broker_id", 1));
        Assert.Contains(intMeasurements, m =>
            m.Value == 3 &&
            HasTag(m.Tags, "broker_id", 1));

        // Counters
        Assert.Contains(longMeasurements, m =>
            m.Value == 2 &&
            HasTag(m.Tags, "broker_id", 1));
        Assert.Contains(longMeasurements, m =>
            m.Value == 1 &&
            HasTag(m.Tags, "broker_id", 1));

        // Connection state (UP = 1)
        Assert.Contains(intMeasurements, m =>
            m.Value == 1 &&
            HasTag(m.Tags, "broker_id", 1) &&
            HasTag(m.Tags, "state", "UP"));

        // Internal latency (in microseconds, not converted)
        Assert.Contains(doubleMeasurements, m =>
            m.Value == 200.0 &&
            HasTag(m.Tags, "broker_id", 1) &&
            HasTag(m.Tags, "percentile", "p50"));
        Assert.Contains(doubleMeasurements, m =>
            m.Value == 500.0 &&
            HasTag(m.Tags, "broker_id", 1) &&
            HasTag(m.Tags, "percentile", "p95"));
        Assert.Contains(doubleMeasurements, m =>
            m.Value == 1000.0 &&
            HasTag(m.Tags, "broker_id", 1) &&
            HasTag(m.Tags, "percentile", "p99"));

        // Tx bytes and retries
        Assert.Contains(longMeasurements, m =>
            m.Value == 1024000 &&
            HasTag(m.Tags, "broker_id", 1));
        Assert.Contains(longMeasurements, m =>
            m.Value == 10 &&
            HasTag(m.Tags, "broker_id", 1));

        // Connects and disconnects
        Assert.Contains(longMeasurements, m =>
            m.Value == 15 &&
            HasTag(m.Tags, "broker_id", 1));
        Assert.Contains(longMeasurements, m =>
            m.Value == 3 &&
            HasTag(m.Tags, "broker_id", 1));
    }

    [Fact]
    public void GivenValidStatistics_WhenHandleStatistics_ThenClientMetricsAreParsedCorrectly()
    {
        // Arrange
        var json = """
        {
          "msg_cnt": 42,
          "msg_size": 8192
        }
        """;

        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.HandleStatistics(json);
        listener.RecordObservableInstruments();

        // Assert
        Assert.Contains(longMeasurements, m => m.Value == 42);
        Assert.Contains(longMeasurements, m => m.Value == 8192);
    }

    [Fact]
    public void GivenValidStatistics_WhenHandleStatistics_ThenConsumerLagIsParsedCorrectly()
    {
        // Arrange
        var json = """
        {
          "topics": {
            "test-topic": {
              "partitions": {
                "0": { "consumer_lag": 100 },
                "1": { "consumer_lag": 50 },
                "-1": { "consumer_lag": 150 }
              }
            }
          }
        }
        """;

        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.HandleStatistics(json);
        listener.RecordObservableInstruments();

        // Assert
        Assert.Contains(longMeasurements, m =>
            m.Value == 100 &&
            HasTag(m.Tags, "topic", "test-topic") &&
            HasTag(m.Tags, "partition", 0));
        Assert.Contains(longMeasurements, m =>
            m.Value == 50 &&
            HasTag(m.Tags, "topic", "test-topic") &&
            HasTag(m.Tags, "partition", 1));
        // Partition -1 should be skipped
        Assert.DoesNotContain(longMeasurements, m => HasTag(m.Tags, "partition", -1));
    }

    [Fact]
    public void GivenValidStatistics_WhenHandleStatistics_ThenConsumerGroupStateIsParsedCorrectly()
    {
        // Arrange
        var json = """
        {
          "cgrp": {
            "state": "stable",
            "rebalance_cnt": 3
          }
        }
        """;

        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.HandleStatistics(json);
        listener.RecordObservableInstruments();

        // Assert
        Assert.Contains(intMeasurements, m =>
            m.Value == 4 && // stable = 4
            HasTag(m.Tags, "state", "stable"));
        Assert.Contains(longMeasurements, m => m.Value == 3);
    }

    [Fact]
    public void GivenBootstrapBroker_WhenHandleStatistics_ThenBrokerIsSkipped()
    {
        // Arrange
        var json = """
        {
          "brokers": {
            "bootstrap": {
              "nodeid": -1,
              "state": "INIT",
              "outbuf_msg_cnt": 999
            },
            "broker1": {
              "nodeid": 1,
              "state": "UP",
              "outbuf_msg_cnt": 5
            }
          }
        }
        """;

        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.HandleStatistics(json);
        listener.RecordObservableInstruments();

        // Assert
        // Bootstrap broker (nodeid -1) should be skipped
        Assert.DoesNotContain(intMeasurements, m =>
            m.Value == 999 &&
            HasTag(m.Tags, "broker_id", -1));
        // Regular broker should be captured
        Assert.Contains(intMeasurements, m =>
            m.Value == 5 &&
            HasTag(m.Tags, "broker_id", 1));
    }

    [Fact]
    public void GivenMalformedJson_WhenHandleStatistics_ThenNoExceptionIsThrown()
    {
        // Arrange
        var json = "{ this is not valid json }";
        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        var exception = Record.Exception(() => metrics.HandleStatistics(json));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void GivenMissingProperties_WhenHandleStatistics_ThenNoExceptionIsThrown()
    {
        // Arrange
        var json = """
        {
          "some_other_field": 123
        }
        """;
        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        var exception = Record.Exception(() => metrics.HandleStatistics(json));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void GivenPartialBrokerData_WhenHandleStatistics_ThenDefaultValuesAreUsed()
    {
        // Arrange
        var json = """
        {
          "brokers": {
            "partial": {
              "nodeid": 2,
              "state": "DOWN"
            }
          }
        }
        """;

        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.HandleStatistics(json);
        listener.RecordObservableInstruments();

        // Assert
        // Missing RTT should default to 0
        Assert.Contains(doubleMeasurements, m =>
            m.Value == 0.0 &&
            HasTag(m.Tags, "broker_id", 2) &&
            HasTag(m.Tags, "percentile", "p50"));

        // Connection state should map DOWN to 0
        Assert.Contains(intMeasurements, m =>
            m.Value == 0 &&
            HasTag(m.Tags, "broker_id", 2) &&
            HasTag(m.Tags, "state", "DOWN"));
    }

    [Theory]
    [InlineData("UP", 1)]
    [InlineData("DOWN", 0)]
    [InlineData("INIT", 2)]
    [InlineData("CONNECT", 3)]
    [InlineData("CONNECTING", 3)]
    [InlineData("AUTH", 4)]
    [InlineData("UNKNOWN", -1)]
    [InlineData("", -1)]
    public void GivenVariousConnectionStates_WhenHandleStatistics_ThenStatesAreMappedCorrectly(string state, int expectedValue)
    {
        // Arrange
        var json = $$"""
        {
          "brokers": {
            "broker1": {
              "nodeid": 1,
              "state": "{{state}}"
            }
          }
        }
        """;

        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.HandleStatistics(json);
        listener.RecordObservableInstruments();

        // Assert
        Assert.Contains(intMeasurements, m =>
            m.Value == expectedValue &&
            HasTag(m.Tags, "broker_id", 1));
    }

    [Theory]
    [InlineData("up", 1)]
    [InlineData("preparingrebalance", 2)]
    [InlineData("preparing_rebalance", 2)]
    [InlineData("completingrebalance", 3)]
    [InlineData("completing_rebalance", 3)]
    [InlineData("stable", 4)]
    [InlineData("dead", 5)]
    [InlineData("empty", 6)]
    [InlineData("unknown", 0)]
    [InlineData("", 0)]
    public void GivenVariousConsumerGroupStates_WhenHandleStatistics_ThenStatesAreMappedCorrectly(string state, int expectedValue)
    {
        // Arrange
        var json = $$"""
        {
          "cgrp": {
            "state": "{{state}}",
            "rebalance_cnt": 0
          }
        }
        """;

        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.HandleStatistics(json);
        listener.RecordObservableInstruments();

        // Assert
        Assert.Contains(intMeasurements, m => m.Value == expectedValue);
    }

    [Fact]
    public void GivenNegativeConsumerLag_WhenHandleStatistics_ThenLagIsExcluded()
    {
        // Arrange
        var json = """
        {
          "topics": {
            "test-topic": {
              "partitions": {
                "0": { "consumer_lag": -5 }
              }
            }
          }
        }
        """;

        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.HandleStatistics(json);
        listener.RecordObservableInstruments();

        // Assert
        // Negative lag should NOT be captured (lag >= 0 check in HandleStatistics)
        Assert.DoesNotContain(longMeasurements, m =>
            HasTag(m.Tags, "topic", "test-topic") &&
            HasTag(m.Tags, "partition", 0));
    }

    [Fact]
    public void GivenMultipleBrokers_WhenHandleStatistics_ThenAllBrokersAreCaptured()
    {
        // Arrange
        var json = """
        {
          "brokers": {
            "broker1": {
              "nodeid": 1,
              "state": "UP",
              "outbuf_msg_cnt": 5
            },
            "broker2": {
              "nodeid": 2,
              "state": "DOWN",
              "outbuf_msg_cnt": 10
            },
            "broker3": {
              "nodeid": 3,
              "state": "UP",
              "outbuf_msg_cnt": 15
            }
          }
        }
        """;

        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.HandleStatistics(json);
        listener.RecordObservableInstruments();

        // Assert
        Assert.Contains(intMeasurements, m =>
            m.Value == 5 &&
            HasTag(m.Tags, "broker_id", 1));
        Assert.Contains(intMeasurements, m =>
            m.Value == 10 &&
            HasTag(m.Tags, "broker_id", 2));
        Assert.Contains(intMeasurements, m =>
            m.Value == 15 &&
            HasTag(m.Tags, "broker_id", 3));
    }

    [Fact]
    public void GivenSubsequentStatisticsCallsWithUpdatedValues_WhenHandleStatistics_ThenMetricsAreUpdated()
    {
        // Arrange
        var json1 = """
        {
          "msg_cnt": 10
        }
        """;
        var json2 = """
        {
          "msg_cnt": 20
        }
        """;

        var metrics = new KafkaBrokerMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.HandleStatistics(json1);
        listener.RecordObservableInstruments();
        longMeasurements.Clear();

        metrics.HandleStatistics(json2);
        listener.RecordObservableInstruments();

        // Assert
        Assert.Contains(longMeasurements, m => m.Value == 20);
    }

    private static bool HasTag(KeyValuePair<string, object?>[] tags, string key, object expectedValue)
    {
        return tags.Any(t => t.Key == key && Equals(t.Value, expectedValue));
    }
}
