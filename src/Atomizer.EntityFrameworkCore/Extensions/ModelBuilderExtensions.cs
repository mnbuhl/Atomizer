using Atomizer.EntityFrameworkCore.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore;

/// <summary>
/// Extension methods for registering Atomizer entity configurations with a <see cref="ModelBuilder"/>.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Registers all Atomizer entity type configurations (jobs, job errors, and schedules) with the model builder.
    /// </summary>
    /// <param name="builder">The <see cref="ModelBuilder"/> to configure.</param>
    /// <param name="schema">The database schema to use for Atomizer tables. Defaults to <c>"Atomizer"</c>.</param>
    /// <returns>The same <see cref="ModelBuilder"/> instance for chaining.</returns>
    public static ModelBuilder AddAtomizerEntities(this ModelBuilder builder, string? schema = "Atomizer")
    {
        builder.ApplyConfiguration(new AtomizerJobEntityConfiguration(schema));
        builder.ApplyConfiguration(new AtomizerJobErrorEntityConfiguration(schema));
        builder.ApplyConfiguration(new AtomizerScheduleEntityConfiguration(schema));
        return builder;
    }
}
