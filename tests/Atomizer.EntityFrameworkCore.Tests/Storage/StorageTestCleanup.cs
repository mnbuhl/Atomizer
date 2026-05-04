using Atomizer.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.Storage;

internal static class StorageTestCleanup
{
    public static async Task ClearAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        dbContext.Set<AtomizerJobErrorEntity>().RemoveRange(dbContext.Set<AtomizerJobErrorEntity>());
        dbContext.Set<AtomizerJobEntity>().RemoveRange(dbContext.Set<AtomizerJobEntity>());
        dbContext.Set<AtomizerScheduleEntity>().RemoveRange(dbContext.Set<AtomizerScheduleEntity>());
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
