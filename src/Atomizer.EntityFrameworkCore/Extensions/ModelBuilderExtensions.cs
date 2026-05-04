using Atomizer.EntityFrameworkCore.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore;

/// <summary>
/// Extension methods for configuring the EF Core model with Atomizer entity type configurations.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies Atomizer entity type configurations for jobs, job errors, and schedules.
    /// </summary>
    /// <param name="builder">The model builder to configure.</param>
    /// <param name="schema">The database schema to use for Atomizer tables. Defaults to <c>"Atomizer"</c>.</param>
    /// <returns>The <see cref="ModelBuilder"/> for chaining.</returns>
    public static ModelBuilder AddAtomizerEntities(this ModelBuilder builder, string? schema = "Atomizer")
    {
        builder.ApplyConfiguration(new AtomizerJobEntityConfiguration(schema));
        builder.ApplyConfiguration(new AtomizerJobErrorEntityConfiguration(schema));
        builder.ApplyConfiguration(new AtomizerScheduleEntityConfiguration(schema));
        builder.ApplyConfiguration(new AtomizerActiveServerEntityConfiguration(schema));
        return builder;
    }
}
