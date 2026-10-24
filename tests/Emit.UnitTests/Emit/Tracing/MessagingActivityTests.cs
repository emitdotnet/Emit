namespace Emit.UnitTests.Tracing;

using System.Diagnostics;
using global::Emit.Tracing;
using Xunit;

public sealed class MessagingActivityTests : IDisposable
{
    private static readonly ActivityContext Remote = new(
        ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded, isRemote: true);

    // A source of this test only, so the listener records nothing from tests running alongside
    private readonly ActivitySource source = new($"test-{Guid.NewGuid():N}");
    private readonly ActivityListener listener;

    public MessagingActivityTests()
    {
        listener = new ActivityListener
        {
            ShouldListenTo = s => s == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
    }

    [Fact]
    public void GivenDestinationAndMessage_WhenStarted_ThenNamedAfterOperationAndDestinationWithItsAttributesSetOnce()
    {
        // Act
        using var activity = MessagingActivity.Start(
            source, new EmitTracingOptions(), ActivityNames.Send, ActivityKind.Client, new Uri("kafka://broker:9092/orders"), "message-1");

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("send orders", activity.DisplayName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("kafka", activity.GetTagItem("messaging.system"));
        Assert.Equal("send", activity.GetTagItem("messaging.operation.name"));
        Assert.Equal("send", activity.GetTagItem("messaging.operation.type"));
        Assert.Equal("orders", activity.GetTagItem("messaging.destination.name"));
        Assert.Equal("broker", activity.GetTagItem("server.address"));
        Assert.Equal(9092, activity.GetTagItem("server.port"));
        Assert.Equal("message-1", activity.GetTagItem("messaging.message.id"));
        Assert.Equal(activity.TagObjects.Count(), activity.TagObjects.Select(tag => tag.Key).Distinct().Count());
    }

    [Theory]
    [InlineData(false, true, true, false, false)]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, false, false, true, true)]
    public void GivenTracingOptions_WhenStarted_ThenASpanStartsOnlyWhenEnabledAndJoiningATraceOrAllowedToStartOne(
        bool enabled,
        bool createRootActivities,
        bool hasParent,
        bool hasLink,
        bool expectedStarted)
    {
        // Arrange
        var options = new EmitTracingOptions { Enabled = enabled, CreateRootActivities = createRootActivities };

        // Act
        using var activity = MessagingActivity.StartActivity(
            source,
            options,
            "span",
            ActivityKind.Internal,
            hasParent ? Remote : default,
            hasLink ? [new ActivityLink(Remote)] : null);

        // Assert
        Assert.Equal(expectedStarted, activity is not null);
    }

    [Fact]
    public void GivenFailure_WhenRecorded_ThenTheSpanIsAnErrorWithTheExceptionTypeAndMessage()
    {
        // Arrange
        using var activity = source.StartActivity("span")!;

        // Act
        MessagingActivity.RecordFailure(activity, new TimeoutException("broker unreachable"), CancellationToken.None);

        // Assert
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("broker unreachable", activity.StatusDescription);
        Assert.Equal(typeof(TimeoutException).FullName, activity.GetTagItem("error.type"));
    }

    [Fact]
    public void GivenCancellationTheCallerAskedFor_WhenRecorded_ThenTheSpanIsNotAnError()
    {
        // Arrange
        using var activity = source.StartActivity("span")!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        MessagingActivity.RecordFailure(activity, new OperationCanceledException(cancellation.Token), cancellation.Token);

        // Assert
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Null(activity.GetTagItem("error.type"));
    }

    [Fact]
    public void GivenTraceContextHeadersInAnotherCase_WhenReadingTheCreationContext_ThenItIsTheRemoteContextTheyCarry()
    {
        // Arrange
        var traceParent = $"00-{Remote.TraceId.ToHexString()}-{Remote.SpanId.ToHexString()}-01";
        List<KeyValuePair<string, string>> headers = [new("TraceParent", traceParent), new("TraceState", "vendor=1")];

        // Act
        var context = MessagingActivity.CreationContext(headers);

        // Assert
        Assert.Equal(Remote.TraceId, context.TraceId);
        Assert.Equal(Remote.SpanId, context.SpanId);
        Assert.Equal("vendor=1", context.TraceState);
        Assert.True(context.IsRemote);
    }

    public void Dispose()
    {
        listener.Dispose();
        source.Dispose();
    }
}
