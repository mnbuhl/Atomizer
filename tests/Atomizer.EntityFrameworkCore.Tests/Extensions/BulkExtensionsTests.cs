using Atomizer.EntityFrameworkCore.Extensions;
using Atomizer.Tests.Utilities;
using AwesomeAssertions;

namespace Atomizer.EntityFrameworkCore.Tests.Extensions;

/// <summary>
/// Tests for <see cref="BulkExtensions"/>
/// <remarks>EF changed the namespace of these methods between v8 and v9, so we need to ensure we can still reflect them correctly.</remarks>
/// </summary>
public class BulkExtensionsTests
{
#if NET9_0_OR_GREATER
    [Fact]
    public void BulkExtensions_CanReflect_BulkMethodsNamespace()
    {
        // Arrange & Act
        var type = NonPublicSpy.GetFieldValue<Type>(typeof(BulkExtensions), "ExtType");

        // Assert
        type.FullName.Should().Be("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
        type.Assembly.GetName().Name.Should().Be("Microsoft.EntityFrameworkCore");
        type.Assembly.GetName().Version!.Major.Should().BeGreaterThanOrEqualTo(9);
    }
#endif
#if NET8_0
    [Fact]
    public void BulkExtensions_CanReflect_BulkMethodsNamespace()
    {
        // Arrange & Act
        var type = NonPublicSpy.GetFieldValue<Type>(typeof(BulkExtensions), "ExtType");

        // Assert
        type.FullName.Should().Be("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions");
        type.Assembly.GetName().Name.Should().Be("Microsoft.EntityFrameworkCore.Relational");
        type.Assembly.GetName().Version!.Major.Should().BeGreaterThanOrEqualTo(7);
    }
#endif
}
