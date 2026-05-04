using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Storage;
using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Storage;

public sealed class EntityFrameworkCoreStorageProviderTests
{
    [Fact]
    public void Constructor_WhenProviderIsUnsupported_ShouldFailFast()
    {
        var options = new DbContextOptionsBuilder<UnsupportedProviderDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var dbContext = new UnsupportedProviderDbContext(options);

        var act = () =>
            new EntityFrameworkCoreStorage<UnsupportedProviderDbContext>(
                dbContext,
                new EntityFrameworkCoreJobStorageOptions(),
                Substitute.For<ILogger<EntityFrameworkCoreStorage<UnsupportedProviderDbContext>>>(),
                Substitute.For<IAtomizerClock>()
            );

        act.Should()
            .Throw<NotSupportedException>()
            .WithMessage("*Microsoft.EntityFrameworkCore.Sqlite*SQL Server*PostgreSQL*MySQL*");
    }

    public sealed class UnsupportedProviderDbContext(DbContextOptions<UnsupportedProviderDbContext> options)
        : TestDbContext(options);
}
