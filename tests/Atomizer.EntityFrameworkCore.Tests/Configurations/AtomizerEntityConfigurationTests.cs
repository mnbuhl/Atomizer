using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Atomizer.EntityFrameworkCore.Tests.Configurations;

public sealed class AtomizerEntityConfigurationTests
{
    [Fact]
    public void AddAtomizerEntities_WhenConfiguringJobEntity_ShouldCreateQueryIndexes()
    {
        using var dbContext = CreateDbContext();

        AssertIndex<AtomizerJobEntity>(
            dbContext,
            "IX_AtomizerJobs_QueueKey_Status_ScheduledAt_Id",
            [
                nameof(AtomizerJobEntity.QueueKey),
                nameof(AtomizerJobEntity.Status),
                nameof(AtomizerJobEntity.ScheduledAt),
                nameof(AtomizerJobEntity.Id),
            ]
        );
        AssertIndex<AtomizerJobEntity>(
            dbContext,
            "IX_AtomizerJobs_QueueKey_PartitionKey_SequenceNumber",
            [
                nameof(AtomizerJobEntity.QueueKey),
                nameof(AtomizerJobEntity.PartitionKey),
                nameof(AtomizerJobEntity.SequenceNumber),
            ]
        );
        AssertIndex<AtomizerJobEntity>(
            dbContext,
            "IX_AtomizerJobs_QueueKey_Status_Attempts_PartitionKey",
            [
                nameof(AtomizerJobEntity.QueueKey),
                nameof(AtomizerJobEntity.Status),
                nameof(AtomizerJobEntity.Attempts),
                nameof(AtomizerJobEntity.PartitionKey),
            ]
        );
        AssertIndex<AtomizerJobEntity>(
            dbContext,
            "IX_AtomizerJobs_Status_LeaseToken",
            [nameof(AtomizerJobEntity.Status), nameof(AtomizerJobEntity.LeaseToken)]
        );
        AssertIndex<AtomizerJobEntity>(
            dbContext,
            "IX_AtomizerJobs_IdempotencyKey",
            [nameof(AtomizerJobEntity.IdempotencyKey)]
        );
    }

    [Fact]
    public void AddAtomizerEntities_WhenConfiguringScheduleEntity_ShouldCreateQueryIndexes()
    {
        using var dbContext = CreateDbContext();

        AssertIndex<AtomizerScheduleEntity>(
            dbContext,
            "IX_AtomizerSchedules_Enabled_NextRunAt_Id",
            [
                nameof(AtomizerScheduleEntity.Enabled),
                nameof(AtomizerScheduleEntity.NextRunAt),
                nameof(AtomizerScheduleEntity.Id),
            ]
        );
        AssertIndex<AtomizerScheduleEntity>(
            dbContext,
            "IX_AtomizerSchedules_JobKey",
            [nameof(AtomizerScheduleEntity.JobKey)],
            isUnique: true
        );
    }

    [Fact]
    public void AddAtomizerEntities_WhenConfiguringActiveServerEntity_ShouldCreateQueryIndexes()
    {
        using var dbContext = CreateDbContext();

        AssertIndex<AtomizerActiveServerEntity>(
            dbContext,
            "IX_AtomizerActiveServers_LastHeartbeatAt_InstanceId",
            [nameof(AtomizerActiveServerEntity.LastHeartbeatAt), nameof(AtomizerActiveServerEntity.InstanceId)]
        );
    }

    private static IndexModelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<IndexModelDbContext>().UseSqlite("Data Source=:memory:").Options;

        return new IndexModelDbContext(options);
    }

    private static void AssertIndex<TEntity>(
        DbContext dbContext,
        string indexName,
        string[] propertyNames,
        bool isUnique = false
    )
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        entityType.Should().NotBeNull();

        var index = entityType!.GetIndexes().SingleOrDefault(index => index.GetDatabaseName() == indexName);
        index.Should().NotBeNull();
        index!.Properties.Select(property => property.Name).Should().Equal(propertyNames);
        index.IsUnique.Should().Be(isUnique);
    }

    private sealed class IndexModelDbContext(DbContextOptions<IndexModelDbContext> options) : TestDbContext(options);
}
