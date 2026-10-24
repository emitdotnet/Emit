namespace Emit.Kafka.Tests.Tracing;

using System.Diagnostics;
using global::Emit.Kafka.Tracing;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaSendActivityTests : IDisposable
{
    // A source of this test only, so the listener records nothing from tests running alongside
    private readonly ActivitySource source = new($"test-{Guid.NewGuid():N}");
    private readonly ActivityListener listener;

    public KafkaSendActivityTests()
    {
        listener = new ActivityListener
        {
            ShouldListenTo = s => s == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
    }

    [Fact]
    public void GivenKafkaException_WhenTheSendFails_ThenTheErrorTypeIsItsErrorCodeLikeAFailedDeliveryReport()
    {
        // Arrange
        using var activity = source.StartActivity("send")!;
        var exception = new ConfluentKafka.KafkaException(new ConfluentKafka.Error(ConfluentKafka.ErrorCode.Local_MsgTimedOut, "Message timed out"));

        // Act
        KafkaSendActivity.Fail(activity, exception);

        // Assert
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(nameof(ConfluentKafka.ErrorCode.Local_MsgTimedOut), activity.GetTagItem("error.type"));
        Assert.Equal("Message timed out", activity.StatusDescription);
    }

    [Fact]
    public void GivenOtherException_WhenTheSendFails_ThenTheErrorTypeIsItsType()
    {
        // Arrange
        using var activity = source.StartActivity("send")!;

        // Act
        KafkaSendActivity.Fail(activity, new InvalidOperationException("Serializer failed"));

        // Assert
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.GetTagItem("error.type"));
    }

    public void Dispose()
    {
        listener.Dispose();
        source.Dispose();
    }
}
