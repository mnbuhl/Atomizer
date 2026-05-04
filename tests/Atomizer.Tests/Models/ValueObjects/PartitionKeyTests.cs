using Atomizer.Exceptions;

namespace Atomizer.Tests.Models.ValueObjects;

/// <summary>
/// Unit tests for <see cref="PartitionKey"/>.
/// </summary>
public class PartitionKeyTests
{
    [Fact]
    public void Constructor_WithValidKey_ShouldSucceed()
    {
        // Arrange & Act
        var pk = new PartitionKey("orders");

        // Assert
        pk.Key.Should().Be("orders");
    }

    [Fact]
    public void Constructor_WithEmptyString_ShouldThrowInvalidPartitionKeyException()
    {
        // Arrange & Act
        Action act = () => new PartitionKey("");

        // Assert
        act.Should()
            .Throw<InvalidPartitionKeyException>()
            .And.ParamName.Should()
            .Be("key");
    }

    [Fact]
    public void Constructor_WithWhitespaceOnly_ShouldThrowInvalidPartitionKeyException()
    {
        // Arrange & Act
        Action act = () => new PartitionKey("   ");

        // Assert
        act.Should()
            .Throw<InvalidPartitionKeyException>()
            .And.ParamName.Should()
            .Be("key");
    }

    [Fact]
    public void Constructor_WithNull_ShouldThrowInvalidPartitionKeyException()
    {
        // Arrange & Act
        Action act = () => new PartitionKey(null!);

        // Assert
        act.Should()
            .Throw<InvalidPartitionKeyException>()
            .And.ParamName.Should()
            .Be("key");
    }

    [Fact]
    public void Constructor_WithKeyExceeding255Chars_ShouldThrowInvalidPartitionKeyException()
    {
        // Arrange
        var longKey = new string('x', 256);

        // Act
        Action act = () => new PartitionKey(longKey);

        // Assert
        act.Should()
            .Throw<InvalidPartitionKeyException>()
            .And.ParamName.Should()
            .Be("key");
    }

    [Fact]
    public void Constructor_WithExactly255Chars_ShouldSucceed()
    {
        // Arrange
        var key = new string('x', 255);

        // Act
        var pk = new PartitionKey(key);

        // Assert
        pk.Key.Should().Be(key);
    }

    [Fact]
    public void ExplicitConversionFromString_ShouldCreatePartitionKey()
    {
        // Arrange & Act
        var pk = (PartitionKey)"orders";

        // Assert
        pk.Key.Should().Be("orders");
    }

    [Fact]
    public void ImplicitConversionToString_ShouldReturnKeyString()
    {
        // Arrange
        var pk = new PartitionKey("orders");

        // Act
        string value = pk;

        // Assert
        value.Should().Be("orders");
    }

    [Fact]
    public void ToString_ShouldReturnKeyString()
    {
        // Arrange
        var pk = new PartitionKey("orders");

        // Act & Assert
        pk.ToString().Should().Be("orders");
    }

    [Fact]
    public void Equality_WithSameKey_ShouldBeEqual()
    {
        // Arrange
        var pk1 = new PartitionKey("orders");
        var pk2 = new PartitionKey("orders");

        // Assert
        pk1.Should().Be(pk2);
        (pk1 == pk2).Should().BeTrue();
    }

    [Fact]
    public void Equality_WithDifferentKey_ShouldNotBeEqual()
    {
        // Arrange
        var pk1 = new PartitionKey("orders");
        var pk2 = new PartitionKey("payments");

        // Assert
        pk1.Should().NotBe(pk2);
        (pk1 != pk2).Should().BeTrue();
    }
}
