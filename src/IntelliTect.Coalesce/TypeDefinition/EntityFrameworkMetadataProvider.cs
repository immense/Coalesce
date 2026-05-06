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
            var verboseContextName = contextUsage.ClassViewModel.Type.VerboseFullyQualifiedName;
            var contextType = (contextUsage.ClassViewModel.Type as ReflectionTypeViewModel)?.Info
                ?? _typeResolver(verboseContextName);
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

            if (entityType.FindPrimaryKey() is { Properties.Count: > 0 } primaryKey
                && primaryKey.Properties[0].PropertyInfo is { } primaryKeyProperty)
            {
                classMetadata.SinglePrimaryKeyPropertyName ??= primaryKeyProperty.Name;
            }
            else if (entityType.GetProperties().FirstOrDefault(property => property.PropertyInfo is not null)?.PropertyInfo is { } fallbackProperty)
            {
                classMetadata.SinglePrimaryKeyPropertyName ??= fallbackProperty.Name;
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
        var verboseName = GetVerboseTypeName(clrType);
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
        foreach (var options in CreateDbContextOptionsCandidates(contextType))
        {
            if (TryInstantiateDbContext(contextType, options) is { } dbContext)
            {
                try
                {
                    _ = dbContext.Model;
                    return dbContext;
                }
                catch
                {
                    dbContext.Dispose();
                }
            }
        }

        var parameterlessCtor = contextType.GetConstructor(Type.EmptyTypes);
        if (parameterlessCtor?.Invoke(null) is not DbContext parameterlessContext)
        {
            return null;
        }

        try
        {
            _ = parameterlessContext.Model;
            return parameterlessContext;
        }
        catch
        {
            parameterlessContext.Dispose();
            return null;
        }
    }

    private static IEnumerable<DbContextOptions> CreateDbContextOptionsCandidates(Type contextType)
    {
        if (CreateDbContextOptions(contextType, TryConfigureSqlite) is { } sqliteOptions)
        {
            yield return sqliteOptions;
        }

        if (CreateDbContextOptions(contextType, TryConfigureInMemory) is { } inMemoryOptions)
        {
            yield return inMemoryOptions;
        }
    }

    private static DbContext? TryInstantiateDbContext(Type contextType, DbContextOptions options)
    {
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

    private static DbContextOptions? CreateDbContextOptions(
        Type contextType,
        Action<Type, DbContextOptionsBuilder> configureBuilder)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        if (Activator.CreateInstance(builderType) is not DbContextOptionsBuilder builder)
        {
            return null;
        }

        configureBuilder(builderType, builder);
        return builder.IsConfigured ? builder.Options : null;
    }

    private static void TryConfigureSqlite(Type builderType, DbContextOptionsBuilder builder)
    {
        var sqliteExtensionsAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "Microsoft.EntityFrameworkCore.Sqlite")
            ?? TryLoadAssembly("Microsoft.EntityFrameworkCore.Sqlite");

        var sqliteExtensionsType = sqliteExtensionsAssembly
            ?.GetType("Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions");

        var method = sqliteExtensionsType?
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "UseSqlite")
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
        args[1] = "Data Source=:memory:";
        for (var i = 2; i < args.Length; i++)
        {
            args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
        }

        method.Invoke(null, args);
    }

    private static void TryConfigureInMemory(Type builderType, DbContextOptionsBuilder builder)
    {
        if (builder.IsConfigured)
        {
            return;
        }

        var inMemoryExtensions = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "Microsoft.EntityFrameworkCore.InMemory")
            ?? TryLoadAssembly("Microsoft.EntityFrameworkCore.InMemory");

        var inMemoryExtensionsType = inMemoryExtensions
            ?.GetType("Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions");

        var method = inMemoryExtensionsType?
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

    private static Assembly? TryLoadAssembly(string assemblyName)
    {
        try
        {
            return Assembly.Load(assemblyName);
        }
        catch
        {
            return null;
        }
    }

    internal static string GetVerboseTypeName(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsArray)
        {
            return GetVerboseTypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (!type.IsGenericType)
        {
            return type.FullName?.Replace('+', '.') ?? string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        var name = type.Name;
        var index = name.IndexOf('`');
        builder.AppendFormat("{0}.{1}", type.Namespace, index >= 0 ? name[..index] : name);
        builder.Append('<');
        var first = true;
        foreach (var arg in type.GetGenericArguments())
        {
            if (!first)
            {
                builder.Append(", ");
            }

            builder.Append(GetVerboseTypeName(arg));
            first = false;
        }

        builder.Append('>');
        return builder.ToString();
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
            .FirstOrDefault(type => RuntimeEntityFrameworkMetadataProvider.GetVerboseTypeName(type) == verboseFullyQualifiedName);

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
