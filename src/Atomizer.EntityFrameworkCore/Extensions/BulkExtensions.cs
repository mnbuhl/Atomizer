using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Atomizer.EntityFrameworkCore.Extensions;

internal static class BulkExtensions
{
    private enum Operation
    {
        Update,
        UpdateAsync,
        Delete,
        DeleteAsync,
    }

    private static readonly Type ExtType;

    // Open generic method defs (found once)
    private static MethodInfo UpdateOpen => FindGeneric(ExtType, "ExecuteUpdate", 2);
    private static MethodInfo UpdateAsyncOpen => FindGeneric(ExtType, "ExecuteUpdateAsync", 3);
    private static MethodInfo DeleteOpen => FindGeneric(ExtType, "ExecuteDelete", 1);
    private static MethodInfo DeleteAsyncOpen => FindGeneric(ExtType, "ExecuteDeleteAsync", 2);

    // Cache closed methods per entity type
    private static readonly ConcurrentDictionary<(Operation, Type), MethodInfo> Cache = new();

    static BulkExtensions()
    {
        var efCoreVersion =
            typeof(DbContext).Assembly.GetName().Version
            ?? throw new InvalidOperationException("Could not find EF Core dependency.");

        if (efCoreVersion.Major >= 9)
        {
            ExtType = Type.GetType(
                "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions, Microsoft.EntityFrameworkCore"
            )!;
        }
        else
        {
            ExtType = Type.GetType(
                "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions, Microsoft.EntityFrameworkCore.Relational"
            )!;
        }
    }

    public static int ExecuteUpdateCompat<T>(
        this IQueryable<T> source,
        Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> set
    )
    {
        var mi = Cache.GetOrAdd((Operation.Update, typeof(T)), UpdateOpen.MakeGenericMethod(typeof(T)));
        return (int)mi.Invoke(null, new object[] { source, set })!;
    }

    public static Task<int> ExecuteUpdateCompatAsync<T>(
        this IQueryable<T> source,
        Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> set,
        CancellationToken ct = default
    )
    {
        var mi = Cache.GetOrAdd((Operation.UpdateAsync, typeof(T)), UpdateAsyncOpen.MakeGenericMethod(typeof(T)));
        return (Task<int>)mi.Invoke(null, new object[] { source, set, ct })!;
    }

    public static int ExecuteDeleteCompat<T>(this IQueryable<T> source)
    {
        var mi = Cache.GetOrAdd((Operation.Delete, typeof(T)), DeleteOpen.MakeGenericMethod(typeof(T)));
        return (int)mi.Invoke(null, new object[] { source })!;
    }

    public static Task<int> ExecuteDeleteCompatAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
    {
        var mi = Cache.GetOrAdd((Operation.DeleteAsync, typeof(T)), DeleteAsyncOpen.MakeGenericMethod(typeof(T)));
        return (Task<int>)mi.Invoke(null, new object[] { source, ct })!;
    }

    private static MethodInfo FindGeneric(Type declaringType, string name, int paramCount) =>
        declaringType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == name && m.IsGenericMethodDefinition && m.GetParameters().Length == paramCount);
}
