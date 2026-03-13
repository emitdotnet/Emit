namespace Emit.Abstractions.Tests;

using System.Reflection;
using global::Emit.Abstractions;
using Xunit;

public sealed class TransactionalAttributeTests
{
    [Transactional]
    private sealed class DecoratedClass;

    private sealed class UndecoratedClass;

    [Fact]
    public void GivenTransactionalAttribute_WhenAppliedToClass_ThenAttributeIsDetectable()
    {
        // Arrange & Act
        var attribute = typeof(DecoratedClass).GetCustomAttribute<TransactionalAttribute>();

        // Assert
        Assert.NotNull(attribute);
    }

    [Fact]
    public void GivenClassWithoutTransactionalAttribute_WhenChecked_ThenAttributeNotDetectable()
    {
        // Arrange & Act
        var attribute = typeof(UndecoratedClass).GetCustomAttribute<TransactionalAttribute>();

        // Assert
        Assert.Null(attribute);
    }

    [Fact]
    public void GivenTransactionalAttribute_WhenCheckedForAllowMultiple_ThenReturnsFalse()
    {
        // Arrange & Act
        var usage = typeof(TransactionalAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        Assert.NotNull(usage);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void GivenTransactionalAttribute_WhenCheckedForInherited_ThenReturnsFalse()
    {
        // Arrange & Act
        var usage = typeof(TransactionalAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        Assert.NotNull(usage);
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void GivenTransactionalAttribute_WhenCheckedForValidTargets_ThenOnlyClassIsValid()
    {
        // Arrange & Act
        var usage = typeof(TransactionalAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
    }
}
