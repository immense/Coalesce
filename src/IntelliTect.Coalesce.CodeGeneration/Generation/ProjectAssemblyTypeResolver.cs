using IntelliTect.Coalesce.CodeGeneration.Analysis.MsBuild;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace IntelliTect.Coalesce.CodeGeneration.Generation;

#nullable enable

internal sealed class ProjectAssemblyTypeResolver
{
    private readonly string _assemblyPath;
    private readonly ProjectAssemblyLoadContext _loadContext;
    private readonly Lazy<Assembly> _rootAssembly;
    private readonly Lazy<Assembly?> _defaultContextAssembly;

    public ProjectAssemblyTypeResolver(MsBuildProjectContext projectContext)
    {
        _assemblyPath = projectContext.AssemblyFullPath;
        _loadContext = new ProjectAssemblyLoadContext(projectContext.AssemblyFullPath, projectContext.TargetDirectory);
        _rootAssembly = new Lazy<Assembly>(() => _loadContext.LoadFromAssemblyPath(projectContext.AssemblyFullPath));
        _defaultContextAssembly = new Lazy<Assembly?>(TryLoadIntoDefaultContext);
    }

    public Type? Resolve(string verboseFullyQualifiedName)
    {
        var normalizedTypeName = NormalizeTypeName(verboseFullyQualifiedName);
        var assembly = _rootAssembly.Value;

        return assembly.GetType(normalizedTypeName, throwOnError: false, ignoreCase: false)
            ?? TryResolveFromDefinedTypes(assembly, normalizedTypeName)
            ?? TryResolveFromDefaultContext(normalizedTypeName);
    }

    private static string NormalizeTypeName(string verboseFullyQualifiedName)
        => verboseFullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? verboseFullyQualifiedName["global::".Length..]
            : verboseFullyQualifiedName;

    private static Type? TryResolveFromDefinedTypes(Assembly assembly, string normalizedTypeName)
    {
        try
        {
            var simpleTypeName = normalizedTypeName[(normalizedTypeName.LastIndexOf('.') + 1)..];
            return assembly.DefinedTypes
                .FirstOrDefault(type =>
                    string.Equals(type.FullName, normalizedTypeName, StringComparison.Ordinal)
                    || string.Equals(type.Name, simpleTypeName, StringComparison.Ordinal))
                ?.AsType();
        }
        catch
        {
            return null;
        }
    }

    private Type? TryResolveFromDefaultContext(string normalizedTypeName)
    {
        var defaultAssembly = _defaultContextAssembly.Value;
        if (defaultAssembly is null)
        {
            return null;
        }

        return defaultAssembly.GetType(normalizedTypeName, throwOnError: false, ignoreCase: false)
            ?? TryResolveFromDefinedTypes(defaultAssembly, normalizedTypeName);
    }

    private Assembly? TryLoadIntoDefaultContext()
    {
        try
        {
            return AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(assembly =>
                    string.Equals(assembly.Location, _assemblyPath, StringComparison.OrdinalIgnoreCase))
                ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(_assemblyPath);
        }
        catch
        {
            return null;
        }
    }

    private sealed class ProjectAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly IReadOnlyDictionary<string, string> _referencePaths;

        public ProjectAssemblyLoadContext(string assemblyPath, string targetDirectory)
            : base($"CoalesceProject:{Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(assemblyPath);
            _referencePaths = Directory
                .EnumerateFiles(targetDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(File.Exists)
                .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var defaultAssembly = AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(assembly =>
                    string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            if (defaultAssembly is not null)
            {
                return defaultAssembly;
            }

            if (assemblyName.Name is { } assemblySimpleName
                && _referencePaths.TryGetValue(assemblySimpleName, out var resolvedReferencePath))
            {
                return LoadFromAssemblyPath(resolvedReferencePath);
            }

            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
        }
    }
}
