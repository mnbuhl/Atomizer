using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Atomizer.EntityFrameworkCore.Providers;

public class EntityMap
{
    public string Table { get; }
    public Dictionary<string, string> Col { get; }

    private EntityMap(string table, Dictionary<string, string> col)
    {
        Table = table;
        Col = col;
    }

    public static EntityMap Build(IModel model, Type clrType, DatabaseProvider provider)
    {
        var entityType =
            model.FindEntityType(clrType)
            ?? throw new InvalidOperationException($"Entity type {clrType.Name} not found in the model.");

        var table = entityType.GetTableName()!;
        var schema = entityType.GetSchema();

        var soi = StoreObjectIdentifier.Table(table, schema);

        Func<string, string> escape = provider switch
        {
            DatabaseProvider.SqlServer => name => $"[{name}]",
            DatabaseProvider.PostgreSql => name => $"\"{name}\"",
            DatabaseProvider.MySql => name => $"`{name}`",
            DatabaseProvider.Oracle => name => $"\"{name}\"",
            DatabaseProvider.Unknown => name => name,
            _ => throw new NotSupportedException($"Database provider {provider} is not supported."),
        };

        string Column(string name)
        {
            var property =
                entityType.FindProperty(name)
                ?? throw new InvalidOperationException($"Property {name} not found in entity type {clrType.Name}.");

            var columnName =
                property.GetColumnName(soi)
                ?? throw new InvalidOperationException($"Column name for property {name} not found.");

            return escape(columnName);
        }

        var fullTableName = schema != null ? $"{escape(schema)}.{escape(table)}" : escape(table);
        var columns = entityType.GetProperties().ToDictionary(p => p.Name, p => Column(p.Name));

        return new EntityMap(fullTableName, columns);
    }
}
