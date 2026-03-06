namespace Emit.Abstractions.Tests;

using Xunit;

public sealed class MessageValidationResultTests
{
    [Fact]
    public void GivenSuccessSingleton_WhenAccessed_ThenIsValidIsTrueAndErrorsEmpty()
    {
        // Act
        var result = MessageValidationResult.Success;

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void GivenSuccessSingleton_WhenAccessedMultipleTimes_ThenReturnsSameInstance()
    {
        // Act
        var a = MessageValidationResult.Success;
        var b = MessageValidationResult.Success;

        // Assert
        Assert.Same(a, b);
    }

    [Fact]
    public void GivenSingleError_WhenFailCalled_ThenIsValidIsFalseAndContainsError()
    {
        // Arrange
        var error = "Field is required";

        // Act
        var result = MessageValidationResult.Fail(error);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(error, result.Errors[0]);
    }

    [Fact]
    public void GivenMultipleErrors_WhenFailCalled_ThenContainsAllErrors()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2", "Error 3" };

        // Act
        var result = MessageValidationResult.Fail(errors);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
        Assert.Equal(errors, result.Errors);
    }

    [Fact]
    public void GivenEmptyErrorList_WhenFailCalled_ThenThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => MessageValidationResult.Fail(Array.Empty<string>()));
    }

    [Fact]
    public void GivenNullError_WhenFailCalled_ThenThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => MessageValidationResult.Fail((string)null!));
    }

    [Fact]
    public void GivenNullErrors_WhenFailCalled_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => MessageValidationResult.Fail((IEnumerable<string>)null!));
    }
}
