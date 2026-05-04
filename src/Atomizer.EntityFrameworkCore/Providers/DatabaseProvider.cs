namespace Atomizer.EntityFrameworkCore.Providers;

/// <summary>
/// Identifies the relational database provider used by the EF Core context.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>PostgreSQL via Npgsql.</summary>
    PostgreSql,

    /// <summary>MySQL or MariaDB via Pomelo.</summary>
    MySql,

    /// <summary>Microsoft SQL Server.</summary>
    SqlServer,
}
