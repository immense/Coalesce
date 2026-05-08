using IntelliTect.Coalesce.CodeGeneration.Analysis.Roslyn;
using IntelliTect.Coalesce.TypeDefinition;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTect.Coalesce.CodeGeneration.Generation;

public static class ProjectTypeDiscovery
{
    public sealed record DiscoveryProject(
        string ProjectFilePath,
        IReadOnlyList<INamedTypeSymbol> DeclaredTypes,
        Func<string, INamedTypeSymbol> ResolveTypeByMetadataName,
        IReadOnlyList<string> Diagnostics);

    public static IReadOnlyList<string> GetDiagnostics(GenerationContext generationContext)
        => GetDiscoveryProjects(generationContext)
            .SelectMany(project => project.Diagnostics)
            .Distinct()
            .ToList();

    public static IReadOnlyList<INamedTypeSymbol> GetAllTypes(GenerationContext generationContext)
        => ResolveTypes(GetDiscoveryProjects(generationContext));

    internal static IReadOnlyList<INamedTypeSymbol> MergeTypes(
        IEnumerable<(string ProjectFilePath, IEnumerable<INamedTypeSymbol> Types)> projectTypes)
    {
        var mergedTypes = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

        foreach (var (projectFilePath, types) in projectTypes)
        {
            if (string.IsNullOrWhiteSpace(projectFilePath))
            {
                continue;
            }

            foreach (var type in types)
            {
                var key = type.ToDisplayString(SymbolTypeViewModel.DefaultDisplayFormat);
                mergedTypes.TryAdd(key, type);
            }
        }

        return mergedTypes.Values.ToList();
    }

    internal static IReadOnlyList<INamedTypeSymbol> ResolveTypes(
        IReadOnlyList<DiscoveryProject> projectTypes)
    {
        if (projectTypes.Count == 0)
        {
            return [];
        }

        var mergedTypes = MergeTypes(projectTypes
            .Select(project => (project.ProjectFilePath, (IEnumerable<INamedTypeSymbol>)project.DeclaredTypes)));

        if (projectTypes.Count == 1)
        {
            return mergedTypes;
        }

        var masterProject = projectTypes[^1];

        return mergedTypes
            .Select(type =>
            {
                var key = type.ToDisplayString(SymbolTypeViewModel.DefaultDisplayFormat);
                return masterProject.ResolveTypeByMetadataName(key) ?? type;
            })
            .ToList();
    }

    private static IReadOnlyList<DiscoveryProject> GetDiscoveryProjects(
        GenerationContext generationContext)
    {
        return new[] { generationContext.DataProject, generationContext.WebProject }
            .OfType<RoslynProjectContext>()
            .GroupBy(project => project.ProjectFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var locator = (RoslynTypeLocator)group.First().TypeLocator;
                return new DiscoveryProject(
                    group.Key,
                    locator.GetAllTypes(),
                    locator.FindTypeByMetadataName,
                    locator.GetDiagnostics().ToList());
            })
            .ToList();
    }
}
