using System.Collections.Concurrent;
using System.Reflection;

namespace Atomizer.Tests.Utilities;

/// <summary>
/// Accessor for non-public members (instance & static).
/// - Open-instance delegates for instance methods (first arg is TTarget).
/// - Static delegates for static methods (no TTarget arg).
/// - Field / Property / Constant readers (instance & static).
///
/// Notes:
/// - For instance members, you can optionally pass a runtime Type to resolve against a derived type,
///   while keeping the delegate's first parameter as a compile-time TTarget (must be assignable from runtime type).
/// - For static members, use the Type-based overloads.
/// </summary>
public static class NonPublicSpy
{
    private static readonly ConcurrentDictionary<(Type DeclType, string Key), MethodInfo> MethodCache = new();
    private static readonly ConcurrentDictionary<(Type DeclType, string Key), FieldInfo> FieldCache = new();
    private static readonly ConcurrentDictionary<(Type DeclType, string Key), PropertyInfo> PropertyCache = new();

    // =========================
    // Instance methods (open)
    // =========================

    public static Func<TTarget, TResult> CreateFunc<TTarget, TResult>(string methodName, Type? runtimeType = null) =>
        CreateOpenInstanceDelegate<TTarget, Func<TTarget, TResult>>(
            runtimeType ?? typeof(TTarget),
            methodName,
            Type.EmptyTypes,
            typeof(TResult)
        );

    public static Func<TTarget, T1, TResult> CreateFunc<TTarget, T1, TResult>(
        string methodName,
        Type? runtimeType = null
    ) =>
        CreateOpenInstanceDelegate<TTarget, Func<TTarget, T1, TResult>>(
            runtimeType ?? typeof(TTarget),
            methodName,
            new[] { typeof(T1) },
            typeof(TResult)
        );

    public static Func<TTarget, T1, T2, TResult> CreateFunc<TTarget, T1, T2, TResult>(
        string methodName,
        Type? runtimeType = null
    ) =>
        CreateOpenInstanceDelegate<TTarget, Func<TTarget, T1, T2, TResult>>(
            runtimeType ?? typeof(TTarget),
            methodName,
            new[] { typeof(T1), typeof(T2) },
            typeof(TResult)
        );

    public static Func<TTarget, T1, T2, T3, TResult> CreateFunc<TTarget, T1, T2, T3, TResult>(
        string methodName,
        Type? runtimeType = null
    ) =>
        CreateOpenInstanceDelegate<TTarget, Func<TTarget, T1, T2, T3, TResult>>(
            runtimeType ?? typeof(TTarget),
            methodName,
            new[] { typeof(T1), typeof(T2), typeof(T3) },
            typeof(TResult)
        );

    public static Func<TTarget, T1, T2, T3, T4, TResult> CreateFunc<TTarget, T1, T2, T3, T4, TResult>(
        string methodName,
        Type? runtimeType = null
    ) =>
        CreateOpenInstanceDelegate<TTarget, Func<TTarget, T1, T2, T3, T4, TResult>>(
            runtimeType ?? typeof(TTarget),
            methodName,
            new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) },
            typeof(TResult)
        );

    public static Action<TTarget> CreateAction<TTarget>(string methodName, Type? runtimeType = null) =>
        CreateOpenInstanceDelegate<TTarget, Action<TTarget>>(
            runtimeType ?? typeof(TTarget),
            methodName,
            Type.EmptyTypes,
            typeof(void)
        );

    public static Action<TTarget, T1> CreateAction<TTarget, T1>(string methodName, Type? runtimeType = null) =>
        CreateOpenInstanceDelegate<TTarget, Action<TTarget, T1>>(
            runtimeType ?? typeof(TTarget),
            methodName,
            new[] { typeof(T1) },
            typeof(void)
        );

    public static Action<TTarget, T1, T2> CreateAction<TTarget, T1, T2>(string methodName, Type? runtimeType = null) =>
        CreateOpenInstanceDelegate<TTarget, Action<TTarget, T1, T2>>(
            runtimeType ?? typeof(TTarget),
            methodName,
            new[] { typeof(T1), typeof(T2) },
            typeof(void)
        );

    public static Action<TTarget, T1, T2, T3> CreateAction<TTarget, T1, T2, T3>(
        string methodName,
        Type? runtimeType = null
    ) =>
        CreateOpenInstanceDelegate<TTarget, Action<TTarget, T1, T2, T3>>(
            runtimeType ?? typeof(TTarget),
            methodName,
            new[] { typeof(T1), typeof(T2), typeof(T3) },
            typeof(void)
        );

    public static Action<TTarget, T1, T2, T3, T4> CreateAction<TTarget, T1, T2, T3, T4>(
        string methodName,
        Type? runtimeType = null
    ) =>
        CreateOpenInstanceDelegate<TTarget, Action<TTarget, T1, T2, T3, T4>>(
            runtimeType ?? typeof(TTarget),
            methodName,
            new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) },
            typeof(void)
        );

    // =========================
    // Static methods (Type-based)
    // =========================

    public static Func<TResult> CreateStaticFunc<TResult>(Type targetType, string methodName) =>
        CreateStaticDelegate<Func<TResult>>(targetType, methodName, Type.EmptyTypes, typeof(TResult));

    public static Func<T1, TResult> CreateStaticFunc<T1, TResult>(Type targetType, string methodName) =>
        CreateStaticDelegate<Func<T1, TResult>>(targetType, methodName, new[] { typeof(T1) }, typeof(TResult));

    public static Func<T1, T2, TResult> CreateStaticFunc<T1, T2, TResult>(Type targetType, string methodName) =>
        CreateStaticDelegate<Func<T1, T2, TResult>>(
            targetType,
            methodName,
            new[] { typeof(T1), typeof(T2) },
            typeof(TResult)
        );

    public static Func<T1, T2, T3, TResult> CreateStaticFunc<T1, T2, T3, TResult>(Type targetType, string methodName) =>
        CreateStaticDelegate<Func<T1, T2, T3, TResult>>(
            targetType,
            methodName,
            new[] { typeof(T1), typeof(T2), typeof(T3) },
            typeof(TResult)
        );

    public static Func<T1, T2, T3, T4, TResult> CreateStaticFunc<T1, T2, T3, T4, TResult>(
        Type targetType,
        string methodName
    ) =>
        CreateStaticDelegate<Func<T1, T2, T3, T4, TResult>>(
            targetType,
            methodName,
            new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) },
            typeof(TResult)
        );

    public static Action CreateStaticAction(Type targetType, string methodName) =>
        CreateStaticDelegate<Action>(targetType, methodName, Type.EmptyTypes, typeof(void));

    public static Action<T1> CreateStaticAction<T1>(Type targetType, string methodName) =>
        CreateStaticDelegate<Action<T1>>(targetType, methodName, new[] { typeof(T1) }, typeof(void));

    public static Action<T1, T2> CreateStaticAction<T1, T2>(Type targetType, string methodName) =>
        CreateStaticDelegate<Action<T1, T2>>(targetType, methodName, new[] { typeof(T1), typeof(T2) }, typeof(void));

    public static Action<T1, T2, T3> CreateStaticAction<T1, T2, T3>(Type targetType, string methodName) =>
        CreateStaticDelegate<Action<T1, T2, T3>>(
            targetType,
            methodName,
            new[] { typeof(T1), typeof(T2), typeof(T3) },
            typeof(void)
        );

    public static Action<T1, T2, T3, T4> CreateStaticAction<T1, T2, T3, T4>(Type targetType, string methodName) =>
        CreateStaticDelegate<Action<T1, T2, T3, T4>>(
            targetType,
            methodName,
            new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) },
            typeof(void)
        );

    // =========================
    // Field / Property / Constant
    // =========================

    // Instance-first with optional runtime type
    public static object? GetFieldValue<TTarget>(
        string fieldName,
        TTarget? instance = default,
        Type? runtimeType = null
    )
    {
        var t = runtimeType ?? typeof(TTarget);
        var fi = GetFieldInfo(t, fieldName, instanceProvided: instance is not null);
        return fi.GetValue(instance);
    }

    public static T GetFieldValue<TTarget, T>(string fieldName, TTarget? instance = default, Type? runtimeType = null)
    {
        var v = GetFieldValue(fieldName, instance, runtimeType);
        return v is null ? default! : (T)v;
    }

    // Static (Type-based)
    public static object? GetFieldValue(Type targetType, string fieldName)
    {
        var fi = GetFieldInfo(targetType, fieldName, instanceProvided: false);
        return fi.GetValue(null);
    }

    public static T GetFieldValue<T>(Type targetType, string fieldName)
    {
        var v = GetFieldValue(targetType, fieldName);
        return v is null ? default! : (T)v;
    }

    // Properties
    public static object? GetPropertyValue<TTarget>(
        string propertyName,
        TTarget? instance = default,
        Type? runtimeType = null
    )
    {
        var t = runtimeType ?? typeof(TTarget);
        var pi = GetPropertyInfo(t, propertyName, instanceProvided: instance is not null);
        var getter =
            pi.GetGetMethod(nonPublic: true)
            ?? throw new MissingMethodException(
                $"Property '{propertyName}' on {pi.DeclaringType?.FullName} has no getter."
            );
        return getter.Invoke(instance, Array.Empty<object>());
    }

    public static T GetPropertyValue<TTarget, T>(
        string propertyName,
        TTarget? instance = default,
        Type? runtimeType = null
    )
    {
        var v = GetPropertyValue(propertyName, instance, runtimeType);
        return v is null ? default! : (T)v;
    }

    public static object? GetPropertyValue(Type targetType, string propertyName, object? instance = null)
    {
        var pi = GetPropertyInfo(targetType, propertyName, instanceProvided: instance is not null);
        var getter =
            pi.GetGetMethod(nonPublic: true)
            ?? throw new MissingMethodException(
                $"Property '{propertyName}' on {pi.DeclaringType?.FullName} has no getter."
            );
        return getter.Invoke(instance, Array.Empty<object>());
    }

    public static T GetPropertyValue<T>(Type targetType, string propertyName, object? instance = null)
    {
        var v = GetPropertyValue(targetType, propertyName, instance);
        return v is null ? default! : (T)v;
    }

    // Constants
    public static T GetConstant<T>(Type targetType, string constantName)
    {
        var fi = GetFieldInfo(targetType, constantName, instanceProvided: false);
        if (!(fi.IsLiteral && fi is { IsInitOnly: false, IsStatic: true }))
            throw new InvalidOperationException(
                $"Field '{constantName}' on {fi.DeclaringType?.FullName} is not a constant."
            );
        var v = fi.GetRawConstantValue();
        return v is null ? default! : (T)v;
    }

    public static T GetConstant<TTarget, T>(string constantName)
    {
        var fi = GetFieldInfo(typeof(TTarget), constantName, instanceProvided: false);
        if (!(fi.IsLiteral && fi is { IsInitOnly: false, IsStatic: true }))
            throw new InvalidOperationException(
                $"Field '{constantName}' on {fi.DeclaringType?.FullName} is not a constant."
            );
        var v = fi.GetRawConstantValue();
        return v is null ? default! : (T)v;
    }

    // =========================
    // Core delegate builders
    // =========================

    private static TDelegate CreateOpenInstanceDelegate<TTarget, TDelegate>(
        Type resolveType,
        string methodName,
        Type[] paramTypes,
        Type returnType
    )
        where TDelegate : Delegate
    {
        if (!typeof(TTarget).IsAssignableFrom(resolveType))
        {
            throw new InvalidOperationException(
                $"Delegate first parameter type ({typeof(TTarget).FullName}) must be assignable from resolveType ({resolveType.FullName})."
            );
        }

        var mi = ResolveMethod(resolveType, methodName, isStatic: false, paramTypes, returnType);
        try
        {
            return (TDelegate)mi.CreateDelegate(typeof(TDelegate));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Failed to create open-instance delegate for '{Format(mi)}'. "
                    + $"Ensure delegate signature is Func/Action with first arg assignable to {mi.DeclaringType?.FullName}.",
                ex
            );
        }
    }

    private static TDelegate CreateStaticDelegate<TDelegate>(
        Type targetType,
        string methodName,
        Type[] paramTypes,
        Type returnType
    )
        where TDelegate : Delegate
    {
        var mi = ResolveMethod(targetType, methodName, isStatic: true, paramTypes, returnType);
        try
        {
            return (TDelegate)mi.CreateDelegate(typeof(TDelegate));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Failed to create static delegate for '{Format(mi)}'. Ensure delegate signature matches the method.",
                ex
            );
        }
    }

    private static MethodInfo ResolveMethod(
        Type startType,
        string name,
        bool isStatic,
        Type[] paramTypes,
        Type returnType
    )
    {
        var key =
            $"{name}|static={isStatic}|ret={returnType.FullName}|params={string.Join(",", paramTypes.Select(p => p.FullName))}";
        if (MethodCache.TryGetValue((startType, key), out var cached))
            return cached;

        var flags =
            (isStatic ? BindingFlags.Static : BindingFlags.Instance)
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        MethodInfo? found = null;

        for (var cur = startType; cur is not null; cur = cur.BaseType!)
        {
            var candidates = cur.GetMethods(flags);
            foreach (var m in candidates)
            {
                if (m.Name != name || m.IsStatic != isStatic)
                    continue;

                var ps = m.GetParameters().Select(p => p.ParameterType).ToArray();
                if (TypesEqual(ps, paramTypes) && m.ReturnType == returnType)
                {
                    found = m;
                    break;
                }
            }
            if (found is not null)
                break;
        }

        if (found is null)
        {
            var sig = $"{returnType.Name} {name}({string.Join(", ", paramTypes.Select(t => t.Name))})";
            throw new MissingMethodException(
                $"{startType.FullName} does not contain {(isStatic ? "static" : "instance")} non-public/public method matching: {sig}."
            );
        }

        MethodCache[(startType, key)] = found;
        return found;
    }

    private static FieldInfo GetFieldInfo(Type startType, string name, bool instanceProvided)
    {
        var key = $"{name}|field|inst={instanceProvided}";
        if (FieldCache.TryGetValue((startType, key), out var cached))
            return cached;

        FieldInfo? found;

        // If instance provided, prefer instance field first; otherwise prefer static first.
        found = instanceProvided ? FindField(true) ?? FindField(false) : FindField(false) ?? FindField(true);

        if (found is null)
            throw new MissingFieldException(
                $"Field '{name}' not found on {startType.FullName} (searched instance/static, non-public/public)."
            );

        FieldCache[(startType, key)] = found;
        return found;

        FieldInfo? FindField(bool instance)
        {
            var flags =
                (instance ? BindingFlags.Instance : BindingFlags.Static)
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;

            for (var cur = startType; cur is not null; cur = cur.BaseType!)
            {
                var fi = cur.GetField(name, flags);
                if (fi != null)
                    return fi;
            }
            return null;
        }
    }

    private static PropertyInfo GetPropertyInfo(Type startType, string name, bool instanceProvided)
    {
        var key = $"{name}|prop|inst={instanceProvided}";
        if (PropertyCache.TryGetValue((startType, key), out var cached))
            return cached;

        PropertyInfo? found;

        found = instanceProvided ? FindProp(true) ?? FindProp(false) : FindProp(false) ?? FindProp(true);

        if (found is null)
            throw new MissingMemberException(
                $"Property '{name}' not found on {startType.FullName} (searched instance/static, non-public/public)."
            );

        PropertyCache[(startType, key)] = found;
        return found;

        PropertyInfo? FindProp(bool instance)
        {
            var flags =
                (instance ? BindingFlags.Instance : BindingFlags.Static)
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;

            for (var cur = startType; cur is not null; cur = cur.BaseType!)
            {
                var pi = cur.GetProperty(name, flags);
                if (pi != null)
                    return pi;
            }
            return null;
        }
    }

    private static bool TypesEqual(Type[] a, Type[] b) =>
        a.Length == b.Length && a.Zip(b, (x, y) => x == y).All(eq => eq);

    private static string Format(MethodInfo mi)
    {
        var ps = string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name));
        return $"{mi.DeclaringType?.FullName}.{mi.Name}({ps}) : {mi.ReturnType.Name}";
    }
}
