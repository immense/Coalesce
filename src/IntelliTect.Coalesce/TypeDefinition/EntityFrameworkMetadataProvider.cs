using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace IntelliTect.Coalesce.TypeDefinition;

internal enum EntityFrameworkPropertyKind
{
    Scalar,
    Navigation,
    Complex,
}

internal interface IEntityFrameworkMetadataProvider
{
    void Clear();
    string? GetSinglePrimaryKeyPropertyName(ClassViewModel model);
    EntityFrameworkPropertyKind? GetPropertyKind(PropertyViewModel property);
}

internal sealed class RuntimeEntityFrameworkMetadataProvider : IEntityFrameworkMetadataProvider
{
    private readonly ReflectionRepository _repository;
    private readonly Func<string, Type?> _typeResolver;
    private readonly object _cacheLock = new();
    private Dictionary<string, EntityFrameworkClassMetadata>? _metadata;

    public RuntimeEntityFrameworkMetadataProvider(ReflectionRepository repository, Func<string, Type?>? typeResolver = null)
    {
        _repository = repository;
        _typeResolver = typeResolver ?? LoadedAssemblyTypeResolver.Resolve;
    }

    public void Clear()
    {
        lock (_cacheLock)
        {
            _metadata = null;
        }
    }

    public string? GetSinglePrimaryKeyPropertyName(ClassViewModel model)
        => GetClassMetadata(model)?.SinglePrimaryKeyPropertyName;

    public EntityFrameworkPropertyKind? GetPropertyKind(PropertyViewModel property)
        => GetClassMetadata(property.EffectiveParent)?.GetPropertyKind(property.Name);

    private EntityFrameworkClassMetadata? GetClassMetadata(ClassViewModel model)
    {
        var metadata = GetOrBuildMetadata();
        metadata.TryGetValue(model.Type.VerboseFullyQualifiedName, out var classMetadata);
        return classMetadata;
    }

    private IReadOnlyDictionary<string, EntityFrameworkClassMetadata> GetOrBuildMetadata()
    {
        if (_metadata is not null)
        {
            return _metadata;
        }

        lock (_cacheLock)
        {
            if (_metadata is not null)
            {
                return _metadata;
            }

            _metadata = BuildMetadata();
            return _metadata;
        }
    }

    private Dictionary<string, EntityFrameworkClassMetadata> BuildMetadata()
    {
        var metadata = new Dictionary<string, EntityFrameworkClassMetadata>(StringComparer.Ordinal);

        foreach (var contextUsage in _repository.DbContexts)
        {
            var contextType = _typeResolver(contextUsage.ClassViewModel.Type.VerboseFullyQualifiedName);
            if (contextType is null || !typeof(DbContext).IsAssignableFrom(contextType))
            {
                continue;
            }

            try
            {
                using var dbContext = TryCreateDbContext(contextType);
                if (dbContext is null)
                {
                    continue;
                }

                PopulateMetadata(metadata, dbContext.Model);
            }
            catch
            {
                // EF metadata is supplemental: if we can't build it, preserve existing convention-based behavior.
            }
        }

        return metadata;
    }

    private static void PopulateMetadata(Dictionary<string, EntityFrameworkClassMetadata> metadata, IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.ClrType is not Type clrType)
            {
                continue;
            }

            var classMetadata = GetOrCreateClassMetadata(metadata, clrType);

            if (entityType.FindPrimaryKey() is { Properties.Count: 1 } primaryKey
                && primaryKey.Properties[0].PropertyInfo is { } primaryKeyProperty)
            {
                classMetadata.SinglePrimaryKeyPropertyName ??= primaryKeyProperty.Name;
            }

            foreach (var property in entityType.GetProperties())
            {
                if (property.PropertyInfo is { } propertyInfo)
                {
                    classMetadata.PropertyKinds[propertyInfo.Name] = EntityFrameworkPropertyKind.Scalar;
                }
            }

            foreach (var navigation in entityType.GetNavigations())
            {
                if (navigation.PropertyInfo is { } propertyInfo)
                {
                    classMetadata.PropertyKinds[propertyInfo.Name] = navigation.TargetEntityType.IsOwned()
                        ? EntityFrameworkPropertyKind.Complex
                        : EntityFrameworkPropertyKind.Navigation;
                }
            }

            foreach (var complexProperty in GetComplexProperties(entityType))
            {
                classMetadata.PropertyKinds[complexProperty.Name] = EntityFrameworkPropertyKind.Complex;
            }
        }
    }

    private static EntityFrameworkClassMetadata GetOrCreateClassMetadata(
        Dictionary<string, EntityFrameworkClassMetadata> metadata,
        Type clrType)
    {
        var verboseName = new ReflectionTypeViewModel(clrType).VerboseFullyQualifiedName;
        if (!metadata.TryGetValue(verboseName, out var classMetadata))
        {
            classMetadata = new EntityFrameworkClassMetadata();
            metadata[verboseName] = classMetadata;
        }

        return classMetadata;
    }

    private static IEnumerable<PropertyInfo> GetComplexProperties(IReadOnlyEntityType entityType)
    {
        foreach (var complexProperty in ((IReadOnlyTypeBase)entityType).GetComplexProperties())
        {
            if (complexProperty.PropertyInfo is { } propertyInfo)
            {
                yield return propertyInfo;
            }
        }
    }

    private static DbContext? TryCreateDbContext(Type contextType)
    {
        var parameterlessCtor = contextType.GetConstructor(Type.EmptyTypes);
        if (parameterlessCtor?.Invoke(null) is DbContext context)
        {
            return context;
        }

        var options = CreateDbContextOptions(contextType);
        if (options is null)
        {
            return null;
        }

        var genericOptionsType = typeof(DbContextOptions<>).MakeGenericType(contextType);
        foreach (var ctor in contextType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length != 1)
            {
                continue;
            }

            if (parameters[0].ParameterType == genericOptionsType || parameters[0].ParameterType == typeof(DbContextOptions))
            {
                return ctor.Invoke([options]) as DbContext;
            }
        }

        return null;
    }

    private static DbContextOptions? CreateDbContextOptions(Type contextType)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        if (Activator.CreateInstance(builderType) is not DbContextOptionsBuilder builder)
        {
            return null;
        }

        TryConfigureInMemory(builderType, builder);
        return builder.Options;
    }

    private static void TryConfigureInMemory(Type builderType, DbContextOptionsBuilder builder)
    {
        var inMemoryExtensions = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "Microsoft.EntityFrameworkCore.InMemory")
            ?.GetType("Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions");

        var method = inMemoryExtensions?
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "UseInMemoryDatabase")
                {
                    return false;
                }

                var parameters = m.GetParameters();
                return parameters.Length >= 2
                    && parameters[0].ParameterType.IsAssignableFrom(builderType)
                    && parameters[1].ParameterType == typeof(string);
            });

        if (method is null)
        {
            return;
        }

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        args[0] = builder;
        args[1] = $"coalesce-metadata-{Guid.NewGuid():N}";
        for (var i = 2; i < args.Length; i++)
        {
            args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
        }

        method.Invoke(null, args);
    }

    private sealed class EntityFrameworkClassMetadata
    {
        public string? SinglePrimaryKeyPropertyName { get; set; }
        public Dictionary<string, EntityFrameworkPropertyKind> PropertyKinds { get; } = new(StringComparer.Ordinal);

        public EntityFrameworkPropertyKind? GetPropertyKind(string propertyName)
            => PropertyKinds.TryGetValue(propertyName, out var propertyKind) ? propertyKind : null;
    }
}

internal static class LoadedAssemblyTypeResolver
{
    public static Type? Resolve(string verboseFullyQualifiedName)
        => AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(GetLoadableTypes)
            .FirstOrDefault(type => new ReflectionTypeViewModel(type).VerboseFullyQualifiedName == verboseFullyQualifiedName);

    internal static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }
}
