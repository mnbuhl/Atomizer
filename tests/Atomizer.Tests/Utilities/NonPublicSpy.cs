using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Atomizer.Tests.Utilities;

/// <summary>
/// Creates strongly-typed delegates that can invoke protected/private instance methods.
/// - Overload resolution via parameter types
/// - Walks base types for protected members
/// - Caches compiled delegates
/// - Falls back to MethodInfo.Invoke when expression compilation isn't permitted
/// </summary>
public static class NonPublicSpy
{
    private static readonly ConcurrentDictionary<CacheKey, Delegate> Cache = new();

    // ----- Func<...> variants -----
    public static Func<TTarget, TResult> CreateFunc<TTarget, TResult>(string methodName) =>
        (Func<TTarget, TResult>)Create(methodName, typeof(TTarget), Type.EmptyTypes, typeof(TResult));

    public static Func<TTarget, T1, TResult> CreateFunc<TTarget, T1, TResult>(string methodName) =>
        (Func<TTarget, T1, TResult>)Create(methodName, typeof(TTarget), [typeof(T1)], typeof(TResult));

    public static Func<TTarget, T1, T2, TResult> CreateFunc<TTarget, T1, T2, TResult>(string methodName) =>
        (Func<TTarget, T1, T2, TResult>)Create(methodName, typeof(TTarget), [typeof(T1), typeof(T2)], typeof(TResult));

    public static Func<TTarget, T1, T2, T3, TResult> CreateFunc<TTarget, T1, T2, T3, TResult>(string methodName) =>
        (Func<TTarget, T1, T2, T3, TResult>)
            Create(methodName, typeof(TTarget), [typeof(T1), typeof(T2), typeof(T3)], typeof(TResult));

    public static Func<TTarget, T1, T2, T3, T4, TResult> CreateFunc<TTarget, T1, T2, T3, T4, TResult>(
        string methodName
    ) =>
        (Func<TTarget, T1, T2, T3, T4, TResult>)
            Create(methodName, typeof(TTarget), [typeof(T1), typeof(T2), typeof(T3), typeof(T4)], typeof(TResult));

    // ----- Action<...> variants (void) -----
    public static Action<TTarget> CreateAction<TTarget>(string methodName) =>
        (Action<TTarget>)Create(methodName, typeof(TTarget), Type.EmptyTypes, typeof(void));

    public static Action<TTarget, T1> CreateAction<TTarget, T1>(string methodName) =>
        (Action<TTarget, T1>)Create(methodName, typeof(TTarget), [typeof(T1)], typeof(void));

    public static Action<TTarget, T1, T2> CreateAction<TTarget, T1, T2>(string methodName) =>
        (Action<TTarget, T1, T2>)Create(methodName, typeof(TTarget), [typeof(T1), typeof(T2)], typeof(void));

    public static Action<TTarget, T1, T2, T3> CreateAction<TTarget, T1, T2, T3>(string methodName) =>
        (Action<TTarget, T1, T2, T3>)
            Create(methodName, typeof(TTarget), [typeof(T1), typeof(T2), typeof(T3)], typeof(void));

    public static Action<TTarget, T1, T2, T3, T4> CreateAction<TTarget, T1, T2, T3, T4>(string methodName) =>
        (Action<TTarget, T1, T2, T3, T4>)
            Create(methodName, typeof(TTarget), [typeof(T1), typeof(T2), typeof(T3), typeof(T4)], typeof(void));

    // ----- Core -----
    private static Delegate Create(string methodName, Type targetType, Type[] paramTypes, Type returnType)
    {
        var key = new CacheKey(targetType, methodName, paramTypes, returnType);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var method =
            FindMethod(targetType, methodName, paramTypes)
            ?? throw new MissingMethodException(
                $"Method '{methodName}({string.Join(", ", paramTypes.Select(t => t.Name))})' not found on {targetType.FullName} or its base types."
            );

        // Parameters: first is the instance, then method parameters
        var parameters = new[] { Expression.Parameter(targetType, "instance") }
            .Concat(paramTypes.Select((t, i) => Expression.Parameter(t, "p" + i)))
            .ToArray();

        // Convert instance to declaring type if needed (handles base protected methods)
        var instanceExpr = method.DeclaringType!.IsAssignableFrom(targetType)
            ? Expression.Convert(parameters[0], method.DeclaringType)
            : throw new InvalidOperationException($"Cannot convert {targetType} to {method.DeclaringType}");

        var call = Expression.Call(instanceExpr, method, parameters.Skip(1));

        LambdaExpression lambda;
        try
        {
            lambda =
                returnType == typeof(void)
                    ? Expression.Lambda(BuildActionDelegateType(targetType, paramTypes), call, parameters)
                    : Expression.Lambda(BuildFuncDelegateType(targetType, paramTypes, returnType), call, parameters);

            var compiled = lambda.Compile(); // Fast path: compiled, strongly-typed delegate
            Cache[key] = compiled;
            return compiled;
        }
        catch (Exception)
        {
            // Conservative fallback: route through MethodInfo.Invoke (slower, but reliable)
            var body = BuildInvokeFallback(method, parameters, returnType);
            lambda =
                returnType == typeof(void)
                    ? Expression.Lambda(BuildActionDelegateType(targetType, paramTypes), body, parameters)
                    : Expression.Lambda(BuildFuncDelegateType(targetType, paramTypes, returnType), body, parameters);

            var compiled = lambda.Compile();
            Cache[key] = compiled;
            return compiled;
        }
    }

    private static MethodInfo? FindMethod(Type type, string name, Type[] paramTypes)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        for (var t = type; t != null; t = t.BaseType)
        {
            var candidate = t.GetMethod(name, flags, binder: null, types: paramTypes, modifiers: null);
            if (candidate != null)
                return candidate;
        }
        return null;
    }

    private static Type BuildFuncDelegateType(Type targetType, Type[] paramTypes, Type returnType)
    {
        var typeArgs = new[] { targetType }.Concat(paramTypes).Concat(new[] { returnType }).ToArray();
        return typeArgs.Length switch
        {
            2 => typeof(Func<,>).MakeGenericType(typeArgs),
            3 => typeof(Func<,,>).MakeGenericType(typeArgs),
            4 => typeof(Func<,,,>).MakeGenericType(typeArgs),
            5 => typeof(Func<,,,,>).MakeGenericType(typeArgs),
            6 => typeof(Func<,,,,,>).MakeGenericType(typeArgs),
            _ => throw new NotSupportedException("Supports up to 4 method parameters."),
        };
    }

    private static Type BuildActionDelegateType(Type targetType, Type[] paramTypes)
    {
        var typeArgs = new[] { targetType }.Concat(paramTypes).ToArray();
        return typeArgs.Length switch
        {
            1 => typeof(Action<>).MakeGenericType(typeArgs),
            2 => typeof(Action<,>).MakeGenericType(typeArgs),
            3 => typeof(Action<,,>).MakeGenericType(typeArgs),
            4 => typeof(Action<,,,>).MakeGenericType(typeArgs),
            5 => typeof(Action<,,,,>).MakeGenericType(typeArgs),
            _ => throw new NotSupportedException("Supports up to 4 method parameters."),
        };
    }

    private static Expression BuildInvokeFallback(MethodInfo method, ParameterExpression[] parameters, Type returnType)
    {
        // instance + object[] args
        var argsArray = Expression.NewArrayInit(
            typeof(object),
            parameters.Skip(1).Select(p => Expression.Convert(p, typeof(object)))
        );

        var call = Expression.Call(
            Expression.Constant(method),
            typeof(MethodBase).GetMethod(nameof(MethodBase.Invoke), [typeof(object), typeof(object[])])!,
            Expression.Convert(parameters[0], typeof(object)),
            argsArray
        );

        return returnType == typeof(void)
            ? Expression.Block(call, Expression.Empty())
            : Expression.Convert(call, returnType);
    }

    private readonly record struct CacheKey(Type Target, string Name, Type[] Params, Type Return)
    {
        public bool Equals(CacheKey other) =>
            Target == other.Target
            && Name == other.Name
            && Return == other.Return
            && Params.Length == other.Params.Length
            && Params.SequenceEqual(other.Params);

        public override int GetHashCode()
        {
            var h = new HashCode();
            h.Add(Target);
            h.Add(Name);
            h.Add(Return);
            foreach (var p in Params)
                h.Add(p);
            return h.ToHashCode();
        }
    }
}
