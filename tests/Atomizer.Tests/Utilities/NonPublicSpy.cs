using System.Collections.Concurrent;
using System.Reflection;

namespace Atomizer.Tests.Utilities
{
    /// <summary>
    /// Accessor for non-public members of <typeparamref name="TTarget"/>.
    /// - Open-instance delegates for instance methods (first arg is TTarget).
    /// - Static delegates for static methods (no TTarget arg).
    /// - Field / Property / Constant readers (instance & static).
    /// </summary>
    public static class NonPublicSpy<TTarget>
    {
        private static readonly Type TargetType = typeof(TTarget);

        private static readonly ConcurrentDictionary<string, MethodInfo> MethodCache = new();
        private static readonly ConcurrentDictionary<string, FieldInfo> FieldCache = new();
        private static readonly ConcurrentDictionary<string, PropertyInfo> PropertyCache = new();

        // -------------------------
        // Instance methods (open)
        // -------------------------

        public static Func<TTarget, TResult> CreateFunc<TResult>(string methodName) =>
            CreateOpenInstanceDelegate<Func<TTarget, TResult>>(methodName, Type.EmptyTypes, typeof(TResult));

        public static Func<TTarget, T1, TResult> CreateFunc<T1, TResult>(string methodName) =>
            CreateOpenInstanceDelegate<Func<TTarget, T1, TResult>>(methodName, new[] { typeof(T1) }, typeof(TResult));

        public static Func<TTarget, T1, T2, TResult> CreateFunc<T1, T2, TResult>(string methodName) =>
            CreateOpenInstanceDelegate<Func<TTarget, T1, T2, TResult>>(
                methodName,
                new[] { typeof(T1), typeof(T2) },
                typeof(TResult)
            );

        public static Func<TTarget, T1, T2, T3, TResult> CreateFunc<T1, T2, T3, TResult>(string methodName) =>
            CreateOpenInstanceDelegate<Func<TTarget, T1, T2, T3, TResult>>(
                methodName,
                new[] { typeof(T1), typeof(T2), typeof(T3) },
                typeof(TResult)
            );

        public static Func<TTarget, T1, T2, T3, T4, TResult> CreateFunc<T1, T2, T3, T4, TResult>(string methodName) =>
            CreateOpenInstanceDelegate<Func<TTarget, T1, T2, T3, T4, TResult>>(
                methodName,
                new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) },
                typeof(TResult)
            );

        public static Action<TTarget> CreateAction(string methodName) =>
            CreateOpenInstanceDelegate<Action<TTarget>>(methodName, Type.EmptyTypes, typeof(void));

        public static Action<TTarget, T1> CreateAction<T1>(string methodName) =>
            CreateOpenInstanceDelegate<Action<TTarget, T1>>(methodName, new[] { typeof(T1) }, typeof(void));

        public static Action<TTarget, T1, T2> CreateAction<T1, T2>(string methodName) =>
            CreateOpenInstanceDelegate<Action<TTarget, T1, T2>>(
                methodName,
                new[] { typeof(T1), typeof(T2) },
                typeof(void)
            );

        public static Action<TTarget, T1, T2, T3> CreateAction<T1, T2, T3>(string methodName) =>
            CreateOpenInstanceDelegate<Action<TTarget, T1, T2, T3>>(
                methodName,
                new[] { typeof(T1), typeof(T2), typeof(T3) },
                typeof(void)
            );

        public static Action<TTarget, T1, T2, T3, T4> CreateAction<T1, T2, T3, T4>(string methodName) =>
            CreateOpenInstanceDelegate<Action<TTarget, T1, T2, T3, T4>>(
                methodName,
                new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) },
                typeof(void)
            );

        // -------------------------
        // Static methods
        // -------------------------

        public static Func<TResult> CreateStaticFunc<TResult>(string methodName) =>
            CreateStaticDelegate<Func<TResult>>(methodName, Type.EmptyTypes, typeof(TResult));

        public static Func<T1, TResult> CreateStaticFunc<T1, TResult>(string methodName) =>
            CreateStaticDelegate<Func<T1, TResult>>(methodName, [typeof(T1)], typeof(TResult));

        public static Func<T1, T2, TResult> CreateStaticFunc<T1, T2, TResult>(string methodName) =>
            CreateStaticDelegate<Func<T1, T2, TResult>>(methodName, [typeof(T1), typeof(T2)], typeof(TResult));

        public static Func<T1, T2, T3, TResult> CreateStaticFunc<T1, T2, T3, TResult>(string methodName) =>
            CreateStaticDelegate<Func<T1, T2, T3, TResult>>(
                methodName,
                [typeof(T1), typeof(T2), typeof(T3)],
                typeof(TResult)
            );

        public static Func<T1, T2, T3, T4, TResult> CreateStaticFunc<T1, T2, T3, T4, TResult>(string methodName) =>
            CreateStaticDelegate<Func<T1, T2, T3, T4, TResult>>(
                methodName,
                new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) },
                typeof(TResult)
            );

        public static Action CreateStaticAction(string methodName) =>
            CreateStaticDelegate<Action>(methodName, Type.EmptyTypes, typeof(void));

        public static Action<T1> CreateStaticAction<T1>(string methodName) =>
            CreateStaticDelegate<Action<T1>>(methodName, new[] { typeof(T1) }, typeof(void));

        public static Action<T1, T2> CreateStaticAction<T1, T2>(string methodName) =>
            CreateStaticDelegate<Action<T1, T2>>(methodName, new[] { typeof(T1), typeof(T2) }, typeof(void));

        public static Action<T1, T2, T3> CreateStaticAction<T1, T2, T3>(string methodName) =>
            CreateStaticDelegate<Action<T1, T2, T3>>(
                methodName,
                new[] { typeof(T1), typeof(T2), typeof(T3) },
                typeof(void)
            );

        public static Action<T1, T2, T3, T4> CreateStaticAction<T1, T2, T3, T4>(string methodName) =>
            CreateStaticDelegate<Action<T1, T2, T3, T4>>(
                methodName,
                new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) },
                typeof(void)
            );

        // -------------------------
        // Field / Property / Constant
        // -------------------------

        public static object? GetFieldValue(string fieldName, TTarget? instance = default) =>
            GetFieldInfo(fieldName, instanceProvided: instance is not null).GetValue(instance);

        public static T GetFieldValue<T>(string fieldName, TTarget? instance = default)
        {
            var v = GetFieldValue(fieldName, instance);
            return v is null ? default! : (T)v;
        }

        public static object? GetPropertyValue(string propertyName, TTarget? instance = default)
        {
            var pi = GetPropertyInfo(propertyName, instanceProvided: instance is not null);
            var getter =
                pi.GetGetMethod(nonPublic: true)
                ?? throw new MissingMethodException(
                    $"Property '{propertyName}' on {pi.DeclaringType?.FullName} has no getter."
                );
            return getter.Invoke(instance, Array.Empty<object>());
        }

        public static T GetPropertyValue<T>(string propertyName, TTarget? instance = default)
        {
            var v = GetPropertyValue(propertyName, instance);
            return v is null ? default! : (T)v;
        }

        public static T GetConstant<T>(string constantName)
        {
            var fi = GetFieldInfo(constantName, instanceProvided: false);
            if (!(fi.IsLiteral && !fi.IsInitOnly && fi.IsStatic))
                throw new InvalidOperationException(
                    $"Field '{constantName}' on {fi.DeclaringType?.FullName} is not a constant."
                );
            var v = fi.GetRawConstantValue();
            return v is null ? default! : (T)v;
        }

        // -------------------------
        // Core delegate builders
        // -------------------------

        private static TDelegate CreateOpenInstanceDelegate<TDelegate>(
            string methodName,
            Type[] paramTypes,
            Type returnType
        )
            where TDelegate : Delegate
        {
            var mi = ResolveMethod(methodName, isStatic: false, paramTypes, returnType);
            try
            {
                // Open instance: first parameter of the delegate is TTarget (or compatible),
                // and the remainder must match the method params.
                return (TDelegate)mi.CreateDelegate(typeof(TDelegate));
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to create open-instance delegate for '{Format(mi)}'. Ensure delegate signature is Func/Action with first arg {TargetType.FullName}.",
                    ex
                );
            }
        }

        private static TDelegate CreateStaticDelegate<TDelegate>(string methodName, Type[] paramTypes, Type returnType)
            where TDelegate : Delegate
        {
            var mi = ResolveMethod(methodName, isStatic: true, paramTypes, returnType);
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

        private static MethodInfo ResolveMethod(string name, bool isStatic, Type[] paramTypes, Type returnType)
        {
            var key =
                $"{name}|static={isStatic}|ret={returnType.FullName}|params={string.Join(",", paramTypes.Select(p => p.FullName))}";
            if (MethodCache.TryGetValue(key, out var cached))
                return cached;

            var flags =
                (isStatic ? BindingFlags.Static : BindingFlags.Instance)
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;

            MethodInfo? found = null;

            for (var cur = TargetType; cur is not null; cur = cur.BaseType)
            {
                var candidates = cur.GetMethods(flags).Where(m => m.Name == name).ToArray();

                foreach (var m in candidates)
                {
                    if ((m.IsStatic != isStatic))
                        continue;
                    var ps = m.GetParameters().Select(p => p.ParameterType).ToArray();
                    if (TypesEqual(ps, paramTypes) && m.ReturnType == returnType)
                    {
                        found = m;
                        break;
                    }
                }
                if (found != null)
                    break;
            }

            if (found == null)
            {
                var sig = $"{returnType.Name} {name}({string.Join(", ", paramTypes.Select(t => t.Name))})";
                throw new MissingMethodException(
                    $"{TargetType.FullName} does not contain {(isStatic ? "static" : "instance")} non-public/public method matching: {sig}."
                );
            }

            MethodCache[key] = found;
            return found;
        }

        private static FieldInfo GetFieldInfo(string name, bool instanceProvided)
        {
            var key = $"{name}|field|inst={instanceProvided}";
            if (FieldCache.TryGetValue(key, out var cached))
                return cached;

            FieldInfo? found = null;

            // If instance provided, prefer instance field first; otherwise prefer static first.
            if (instanceProvided)
            {
                found = FindField(true) ?? FindField(false);
            }
            else
            {
                found = FindField(false) ?? FindField(true);
            }

            if (found is null)
                throw new MissingFieldException(
                    $"Field '{name}' not found on {TargetType.FullName} (searched instance/static, non-public/public)."
                );

            FieldCache[key] = found;
            return found;

            FieldInfo? FindField(bool instance)
            {
                var flags =
                    (instance ? BindingFlags.Instance : BindingFlags.Static)
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly;

                for (var cur = TargetType; cur is not null; cur = cur.BaseType)
                {
                    var fi = cur.GetField(name, flags);
                    if (fi != null)
                        return fi;
                }
                return null;
            }
        }

        private static PropertyInfo GetPropertyInfo(string name, bool instanceProvided)
        {
            var key = $"{name}|prop|inst={instanceProvided}";
            if (PropertyCache.TryGetValue(key, out var cached))
                return cached;

            PropertyInfo? found = null;

            if (instanceProvided)
            {
                found = FindProp(true) ?? FindProp(false);
            }
            else
            {
                found = FindProp(false) ?? FindProp(true);
            }

            if (found is null)
                throw new MissingMemberException(
                    $"Property '{name}' not found on {TargetType.FullName} (searched instance/static, non-public/public)."
                );

            PropertyCache[key] = found;
            return found;

            PropertyInfo? FindProp(bool instance)
            {
                var flags =
                    (instance ? BindingFlags.Instance : BindingFlags.Static)
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly;

                for (var cur = TargetType; cur is not null; cur = cur.BaseType)
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
}
