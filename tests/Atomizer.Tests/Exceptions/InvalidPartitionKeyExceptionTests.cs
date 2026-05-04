using Atomizer.Exceptions;

namespace Atomizer.Tests.Exceptions;

/// <summary>
/// Unit tests for <see cref="InvalidPartitionKeyException"/>.
/// </summary>
public class InvalidPartitionKeyExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_ShouldSetMessage()
    {
        // Arrange & Act
        var ex = new InvalidPartitionKeyException("test message");

        // Assert
        ex.Message.Should().Contain("test message");
        ex.InnerException.Should().BeNull();
        ex.ParamName.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_ShouldSetBoth()
    {
        // Arrange
        var inner = new Exception("inner");

        // Act
        var ex = new InvalidPartitionKeyException("test message", inner);

        // Assert
        ex.Message.Should().Contain("test message");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Constructor_WithMessageAndParamName_ShouldSetBoth()
    {
        // Arrange & Act
        var ex = new InvalidPartitionKeyException("test message", "myParam");

        // Assert
        ex.Message.Should().Contain("test message");
        ex.ParamName.Should().Be("myParam");
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithMessageParamNameAndInnerException_ShouldSetAll()
    {
        // Arrange
        var inner = new Exception("inner");

        // Act
        var ex = new InvalidPartitionKeyException("test message", "myParam", inner);

        // Assert
        ex.Message.Should().Contain("test message");
        ex.ParamName.Should().Be("myParam");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ShouldDerive_FromArgumentException()
    {
        // Arrange & Act
        var ex = new InvalidPartitionKeyException("test");

        // Assert
        ex.Should().BeAssignableTo<ArgumentException>();
    }
}
