using IntelliTect.Coalesce.DataAnnotations;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace IntelliTect.Coalesce.Mapping;

internal static class AutoDtoProjectionBuilder
{
    private readonly record struct CacheKey(Type SourceType, Type TargetType);

    private static readonly ConcurrentDictionary<CacheKey, LambdaExpression> ProjectionCache = new();
    private static readonly ConcurrentDictionary<CacheKey, Delegate> ObjectProjectorCache = new();

    public static Expression<Func<TSource, TTarget>> GetProjection<TSource, TTarget>()
    {
        return (Expression<Func<TSource, TTarget>>)ProjectionCache.GetOrAdd(
            new(typeof(TSource), typeof(TTarget)),
            _ => BuildProjectionLambda<TSource, TTarget>());
    }

    public static Expression<Func<TSource, TTarget>> ComposeAutoProjection<TSource, TTarget>(
        IReadOnlyList<Expression<Func<TSource, TTarget>>> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        if (overrides.Count == 0)
        {
            return GetProjection<TSource, TTarget>();
        }

        var ignoredMembers = overrides
            .SelectMany(overrideProjection => GetOverrideBindings(overrideProjection.Body))
            .Select(binding => binding.Member.Name)
            .ToHashSet(StringComparer.Ordinal);

        var baseProjection = BuildProjectionLambda<TSource, TTarget>(ignoredMembers);
        return Compose(baseProjection, overrides);
    }

    public static Expression<Func<TSource, TTarget>> Compose<TSource, TTarget>(
        Expression<Func<TSource, TTarget>> baseProjection,
        IReadOnlyList<Expression<Func<TSource, TTarget>>> overrides)
    {
        ArgumentNullException.ThrowIfNull(baseProjection);
        ArgumentNullException.ThrowIfNull(overrides);

        if (overrides.Count == 0)
        {
            return baseProjection;
        }

        var source = baseProjection.Parameters.Single();
        var (newExpression, baseBindings) = GetComposableProjection(baseProjection.Body);

        var mergedBindings = new List<MemberAssignment>(baseBindings);
        var bindingIndexes = baseBindings
            .Select((binding, index) => (binding.Member.Name, index))
            .ToDictionary(x => x.Name, x => x.index, StringComparer.Ordinal);

        foreach (var overrideProjection in overrides)
        {
            ArgumentNullException.ThrowIfNull(overrideProjection);

            if (overrideProjection.Parameters.Count != 1)
            {
                throw new InvalidOperationException("Projection overrides must declare exactly one source parameter.");
            }

            var reboundBody = new ReplaceParameterVisitor(overrideProjection.Parameters[0], source)
                .Visit(overrideProjection.Body)!;

            foreach (var binding in GetOverrideBindings(reboundBody))
            {
                if (bindingIndexes.TryGetValue(binding.Member.Name, out var existingIndex))
                {
                    mergedBindings[existingIndex] = binding;
                }
                else
                {
                    bindingIndexes[binding.Member.Name] = mergedBindings.Count;
                    mergedBindings.Add(binding);
                }
            }
        }

        var body = mergedBindings.Count == 0
            ? (Expression)newExpression
            : Expression.MemberInit(newExpression, mergedBindings);

        return Expression.Lambda<Func<TSource, TTarget>>(body, source);
    }

    public static void MapToExisting<TSource>(TSource source, object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var targetType = target.GetType();
        var projector = (Func<TSource, object>)ObjectProjectorCache.GetOrAdd(
            new(typeof(TSource), targetType),
            static key =>
            {
                var factoryMethod = typeof(AutoDtoProjectionBuilder)
                    .GetMethod(nameof(CreateObjectProjector), BindingFlags.Static | BindingFlags.NonPublic)!
                    .MakeGenericMethod(key.SourceType, key.TargetType);

                return (Delegate)factoryMethod.Invoke(null, null)!;
            });

        var projected = projector(source);
        foreach (var property in GetWritableProperties(targetType))
        {
            property.SetValue(target, property.GetValue(projected));
        }
    }

    private static Func<TSource, object> CreateObjectProjector<TSource, TTarget>()
    {
        var compiled = GetProjection<TSource, TTarget>().Compile();
        return source => compiled(source)!;
    }

    private static Expression<Func<TSource, TTarget>> BuildProjectionLambda<TSource, TTarget>(
        IReadOnlySet<string>? excludedTargetMembers = null)
    {
        var source = Expression.Parameter(typeof(TSource), "source");
        var body = BuildObjectInitializer(source, typeof(TTarget), excludedTargetMembers);
        return Expression.Lambda<Func<TSource, TTarget>>(body, source);
    }

    private static (NewExpression NewExpression, IReadOnlyList<MemberAssignment> Bindings) GetComposableProjection(
        Expression body)
    {
        body = StripConvert(body);

        return body switch
        {
            MemberInitExpression memberInit => (memberInit.NewExpression, ExtractMemberAssignments(memberInit.Bindings)),
            NewExpression newExpression => (newExpression, Array.Empty<MemberAssignment>()),
            _ => throw new InvalidOperationException(
                $"Projection composition requires a projection that creates a new object, but received '{body.NodeType}'.")
        };
    }

    private static IReadOnlyList<MemberAssignment> GetOverrideBindings(Expression body)
    {
        var (newExpression, bindings) = GetComposableProjection(body);
        if (newExpression.Arguments.Count > 0)
        {
            throw new InvalidOperationException(
                "Projection override expressions must use a parameterless constructor and object initializer syntax.");
        }

        return bindings;
    }

    private static IReadOnlyList<MemberAssignment> ExtractMemberAssignments(
        IReadOnlyCollection<MemberBinding> bindings)
    {
        var assignments = new List<MemberAssignment>(bindings.Count);
        foreach (var binding in bindings)
        {
            if (binding is not MemberAssignment assignment)
            {
                throw new InvalidOperationException(
                    $"Projection composition only supports member assignment bindings, but encountered '{binding.BindingType}'.");
            }

            assignments.Add(assignment);
        }

        return assignments;
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression unary
            && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static Expression BuildObjectInitializer(
        Expression source,
        Type targetType,
        IReadOnlySet<string>? excludedTargetMembers = null)
    {
        if (TryConvertDirectly(source, targetType, out var direct))
        {
            return direct;
        }

        var targetProperties = GetReadableProperties(targetType)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var parameterlessCtor = targetType.GetConstructor(Type.EmptyTypes);
        if (parameterlessCtor is not null)
        {
            var bindings = targetProperties.Values
                .Where(p => p.SetMethod is not null && !(excludedTargetMembers?.Contains(p.Name) ?? false))
                .Select(p => TryBuildPropertyValue(source, p, out var value)
                    ? Expression.Bind(p, value)
                    : null)
                .Where(b => b is not null)
                .Cast<MemberBinding>()
                .ToList();

            return Expression.MemberInit(Expression.New(parameterlessCtor), bindings);
        }

        var ctorCandidate = targetType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .Select(ctor => new
            {
                Ctor = ctor,
                Args = TryBuildConstructorArgs(source, ctor, targetProperties, out var args) ? args : null,
            })
            .FirstOrDefault(c => c.Args is not null);

        if (ctorCandidate is null)
        {
            throw new InvalidOperationException(
                $"Could not build an automatic DTO projection from {source.Type} to {targetType}. " +
                $"Add a public parameterless constructor, or a public constructor whose parameters match mappable properties.");
        }

        var ctorAssigned = ctorCandidate.Ctor.GetParameters()
            .Select(p => p.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var memberInitBindings = targetProperties.Values
            .Where(p => p.SetMethod is not null
                && !ctorAssigned.Contains(p.Name)
                && !(excludedTargetMembers?.Contains(p.Name) ?? false))
            .Select(p => TryBuildPropertyValue(source, p, out var value)
                ? Expression.Bind(p, value)
                : null)
            .Where(b => b is not null)
            .Cast<MemberBinding>()
            .ToList();

        var newExpression = Expression.New(ctorCandidate.Ctor, ctorCandidate.Args!);
        return memberInitBindings.Count == 0
            ? newExpression
            : Expression.MemberInit(newExpression, memberInitBindings);
    }

    private static bool TryBuildConstructorArgs(
        Expression source,
        ConstructorInfo ctor,
        IReadOnlyDictionary<string, PropertyInfo> targetProperties,
        out IReadOnlyList<Expression> args)
    {
        var builtArgs = new List<Expression>();
        foreach (var parameter in ctor.GetParameters())
        {
            if (targetProperties.TryGetValue(parameter.Name!, out var property)
                && TryBuildPropertyValue(source, property, out var propertyValue))
            {
                builtArgs.Add(EnsureType(propertyValue, parameter.ParameterType));
            }
            else if (parameter.HasDefaultValue)
            {
                builtArgs.Add(Expression.Constant(parameter.DefaultValue, parameter.ParameterType));
            }
            else
            {
                args = Array.Empty<Expression>();
                return false;
            }
        }

        args = builtArgs;
        return true;
    }

    private static bool TryBuildPropertyValue(Expression source, PropertyInfo targetProperty, out Expression value)
    {
        var sourceAttribute = targetProperty.GetCustomAttribute<DtoSourceAttribute>();
        var path = sourceAttribute?.Path ?? targetProperty.Name;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return TryBuildValueFromPath(source, segments, targetProperty.PropertyType, sourceAttribute, out value);
    }

    private static bool TryBuildValueFromPath(
        Expression current,
        IReadOnlyList<string> segments,
        Type targetType,
        DtoSourceAttribute? sourceAttribute,
        out Expression value)
    {
        if (segments.Count == 0)
        {
            value = BuildValueConversion(current, targetType);
            return true;
        }

        if (TryGetEnumerableElementType(current.Type, out var currentElementType))
        {
            value = BuildCollectionProjection(current, currentElementType, segments, targetType, sourceAttribute);
            return true;
        }

        var member = FindReadableMember(current.Type, segments[0]);
        if (member is null)
        {
            value = default!;
            return false;
        }

        Expression BuildRemaining(Expression nonNullCurrent)
        {
            var next = Expression.MakeMemberAccess(nonNullCurrent, member);
            return TryBuildValueFromPath(next, segments.Skip(1).ToArray(), targetType, sourceAttribute, out var nested)
                ? nested
                : throw new InvalidOperationException(
                    $"Could not map source path '{string.Join(".", segments)}' from {current.Type} to {targetType}.");
        }

        value = CanBeNull(current.Type) && segments.Count > 1
            ? Expression.Condition(
                Expression.Equal(current, Expression.Constant(null, current.Type)),
                Expression.Default(targetType),
                BuildRemaining(current))
            : BuildRemaining(current);

        return true;
    }

    private static Expression BuildCollectionProjection(
        Expression sourceCollection,
        Type sourceElementType,
        IReadOnlyList<string> remainingSegments,
        Type targetType,
        DtoSourceAttribute? sourceAttribute)
    {
        if (!TryGetEnumerableElementType(targetType, out var targetElementType))
        {
            throw new InvalidOperationException(
                $"Cannot map collection source {sourceCollection.Type} to non-collection target {targetType}.");
        }

        var elementParameter = Expression.Parameter(sourceElementType, "item");

        Expression projectedElements;
        if (remainingSegments.Count == 0)
        {
            projectedElements = sourceCollection;
        }
        else
        {
            var elementProjection = TryBuildValueFromPath(
                elementParameter,
                remainingSegments,
                targetElementType,
                null,
                out var value)
                ? value
                : throw new InvalidOperationException(
                    $"Could not map collection path '{string.Join(".", remainingSegments)}' from {sourceCollection.Type} to {targetType}.");

            if (sourceAttribute?.OrderBy is { Length: > 0 } orderByPath)
            {
                var orderByExpression = TryBuildValueFromPath(
                    elementParameter,
                    orderByPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    typeof(object),
                    null,
                    out var orderValue)
                    ? EnsureType(orderValue, typeof(object))
                    : throw new InvalidOperationException(
                        $"Could not map collection ordering path '{orderByPath}' from {sourceElementType}.");

                var orderByLambda = Expression.Lambda(orderByExpression, elementParameter);
                var orderByMethodName = sourceAttribute.OrderByDirection == DefaultOrderByAttribute.OrderByDirections.Descending
                    ? nameof(Enumerable.OrderByDescending)
                    : nameof(Enumerable.OrderBy);

                sourceCollection = Expression.Call(
                    typeof(Enumerable),
                    orderByMethodName,
                    new[] { sourceElementType, typeof(object) },
                    sourceCollection,
                    orderByLambda);
            }

            var selectLambda = Expression.Lambda(elementProjection, elementParameter);
            projectedElements = Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Select),
                new[] { sourceElementType, targetElementType },
                sourceCollection,
                selectLambda);
        }

        Expression materialized = targetType.IsArray
            ? Expression.Call(typeof(Enumerable), nameof(Enumerable.ToArray), new[] { targetElementType }, projectedElements)
            : Expression.Call(typeof(Enumerable), nameof(Enumerable.ToList), new[] { targetElementType }, projectedElements);

        if (CanBeNull(sourceCollection.Type))
        {
            materialized = Expression.Condition(
                Expression.Equal(sourceCollection, Expression.Constant(null, sourceCollection.Type)),
                Expression.Default(targetType),
                EnsureType(materialized, targetType));
        }

        return EnsureType(materialized, targetType);
    }

    private static Expression BuildValueConversion(Expression source, Type targetType)
    {
        if (TryConvertDirectly(source, targetType, out var direct))
        {
            return direct;
        }

        if (TryGetEnumerableElementType(source.Type, out var sourceElementType)
            && TryGetEnumerableElementType(targetType, out _))
        {
            return BuildCollectionProjection(source, sourceElementType, Array.Empty<string>(), targetType, null);
        }

        if (CanBeNull(source.Type))
        {
            return Expression.Condition(
                Expression.Equal(source, Expression.Constant(null, source.Type)),
                Expression.Default(targetType),
                BuildObjectInitializer(source, targetType));
        }

        return BuildObjectInitializer(source, targetType);
    }

    private static bool TryConvertDirectly(Expression source, Type targetType, out Expression result)
    {
        if (targetType == typeof(object))
        {
            result = EnsureType(source, targetType);
            return true;
        }

        if (targetType.IsAssignableFrom(source.Type))
        {
            result = EnsureType(source, targetType);
            return true;
        }

        var sourceUnderlying = Nullable.GetUnderlyingType(source.Type) ?? source.Type;
        var targetUnderlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (sourceUnderlying == targetUnderlying && !TryGetEnumerableElementType(targetType, out _))
        {
            result = EnsureType(source, targetType);
            return true;
        }

        result = default!;
        return false;
    }

    private static Expression EnsureType(Expression expression, Type targetType)
    {
        return expression.Type == targetType
            ? expression
            : Expression.Convert(expression, targetType);
    }

    private static PropertyInfo[] GetReadableProperties(Type type)
        => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetMethod is not null && p.GetIndexParameters().Length == 0)
            .ToArray();

    private static PropertyInfo[] GetWritableProperties(Type type)
        => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.SetMethod is not null && p.GetIndexParameters().Length == 0)
            .ToArray();

    private static MemberInfo? FindReadableMember(Type type, string name)
        => (MemberInfo?)type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
            ?? type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

    private static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        if (type == typeof(string))
        {
            elementType = default!;
            return false;
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        if (enumerable is not null)
        {
            elementType = enumerable.GetGenericArguments()[0];
            return true;
        }

        elementType = default!;
        return false;
    }

    private static bool CanBeNull(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private sealed class ReplaceParameterVisitor(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
