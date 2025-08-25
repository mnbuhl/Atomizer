using Atomizer.EntityFrameworkCore.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore;

public static class ModelBuilderExtensions
{
    public static ModelBuilder AddAtomizerEntities(this ModelBuilder builder, string? schema = "Atomizer")
    {
        builder.ApplyConfiguration(new AtomizerJobEntityConfiguration(schema));
        builder.ApplyConfiguration(new AtomizerJobErrorEntityConfiguration(schema));
        builder.ApplyConfiguration(new AtomizerScheduleEntityConfiguration(schema));
        return builder;
    }
}
