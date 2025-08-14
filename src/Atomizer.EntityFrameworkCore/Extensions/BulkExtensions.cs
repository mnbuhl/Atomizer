using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query;

namespace Atomizer.EntityFrameworkCore.Extensions;

public static class BulkExtensions
{
    static readonly Type? Ef9Plus = Type.GetType(
        "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions, Microsoft.EntityFrameworkCore"
    );
    static readonly Type? Ef7And8 = Type.GetType(
        "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions, Microsoft.EntityFrameworkCore.Relational"
    );

    private static MethodInfo? _executeUpdateCompat;

    public static Task<int> ExecuteUpdateCompatAsync<T>(
        this IQueryable<T> source,
        Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> set,
        CancellationToken ct = default
    )
    {
        if (_executeUpdateCompat == null)
        {
            var ext = Ef9Plus ?? Ef7And8 ?? throw new InvalidOperationException("EF 7+ not detected.");
            var mi = ext.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m =>
                    m.Name == "ExecuteUpdateAsync" && m.IsGenericMethodDefinition && m.GetParameters().Length == 3
                )
                .MakeGenericMethod(typeof(T));

            _executeUpdateCompat = mi;
        }

        return (Task<int>)_executeUpdateCompat.Invoke(null, new object[] { source, set, ct })!;
    }
}
