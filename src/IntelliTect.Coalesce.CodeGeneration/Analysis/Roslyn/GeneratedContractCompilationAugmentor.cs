#nullable enable

using IntelliTect.Coalesce.DataAnnotations;
using IntelliTect.Coalesce.CodeGeneration.Api.Generators;
using IntelliTect.Coalesce.TypeDefinition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IntelliTect.Coalesce.CodeGeneration.Analysis.Roslyn;

internal static class GeneratedContractCompilationAugmentor
{
    public static Compilation AugmentWithSameProjectGeneratedContracts(
        Compilation compilation,
        string projectDirectory,
        CSharpParseOptions? parseOptions = null)
    {
        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(compilation.AssemblyName))
        {
            return compilation;
        }

        var targetAssemblyName = NormalizeAssemblyName(compilation.AssemblyName);
        var outputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generatedTrees = new List<SyntaxTree>();
        var emittedTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sourceType in GetCandidateTypes(compilation))
        {
            // Convention default: TargetAssemblyName = the SOURCE class's assembly.
            var sourceAssembly = NormalizeAssemblyName(
                sourceType.ContainingAssembly?.Name ?? targetAssemblyName);

            var assemblyDefaults = GeneratedContracts.GetAssemblyDefaults(sourceType.ContainingAssembly);

            var shapes = GeneratedContracts.GetShapes(sourceType, assemblyDefaults)
                .Select(shape => GeneratedContracts.ResolveConventions(shape, sourceType, sourceAssembly, assemblyDefaults))
                .Where(shape => ShouldAugmentShape(sourceType, shape))
                .Where(shape => string.Equals(
                    NormalizeAssemblyName(shape.TargetAssemblyName),
                    targetAssemblyName,
                    StringComparison.Ordinal))
                .ToList();

            foreach (var shape in shapes)
            {
                var typeKey = $"{shape.TargetNamespace}.{shape.TypeName}";
                if (!emittedTypes.Add(typeKey))
                {
                    throw new InvalidOperationException(
                        $"Generated contract '{typeKey}' is declared more than once. " +
                        $"The duplicate declaration was found on '{sourceType.ToDisplayString()}'.");
                }

                var model = new GeneratedContracts.GeneratedContractFileModel(
                    shape,
                    GeneratedContracts.ResolveProperties(sourceType, shape),
                    sourceType.ToDisplayString(SymbolTypeViewModel.DefaultDisplayFormat),
                    GeneratedContracts.GetRawMembers(sourceType, shape.ShapeName));

                var outputPath = Path.Combine(projectDirectory, GeneratedContracts.GeneratedContractsRelativePath, $"{shape.TypeName}.g.cs");
                outputPaths.Add(Path.GetFullPath(outputPath));
                generatedTrees.Add(GeneratedContractFile.CreateSyntaxTree(
                    model,
                    parseOptions ?? compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions,
                    outputPath));
            }
        }

        if (generatedTrees.Count == 0)
        {
            return compilation;
        }

        var existingTrees = compilation.SyntaxTrees
            .Where(tree =>
                !string.IsNullOrWhiteSpace(tree.FilePath) &&
                outputPaths.Contains(Path.GetFullPath(tree.FilePath)))
            .ToList();

        if (existingTrees.Count > 0)
        {
            compilation = compilation.RemoveSyntaxTrees(existingTrees);
        }

        return compilation.AddSyntaxTrees(generatedTrees);
    }

    private static bool ShouldAugmentShape(INamedTypeSymbol sourceType, GeneratedContracts.ContractShape shape)
    {
        if (shape.OutputKind != (int)GeneratedContractOutputKind.Class)
        {
            return true;
        }

        return shape.TypeName != sourceType.Name
            || shape.TargetNamespace != sourceType.ContainingNamespace.ToDisplayString();
    }

    private static string NormalizeAssemblyName(string assemblyName)
    {
        if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            assemblyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(assemblyName);
        }

        return assemblyName;
    }

    private static IEnumerable<INamedTypeSymbol> GetCandidateTypes(Compilation compilation)
    {
        var discovered = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var sourceType in GetDeclaredTypes(compilation))
        {
            discovered.Add(sourceType);
        }

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!ShouldInspectAssembly(assembly))
            {
                continue;
            }

            foreach (var symbol in GetNamedTypes(assembly.GlobalNamespace))
            {
                discovered.Add(symbol);
            }
        }

        return discovered;
    }

    private static IEnumerable<INamedTypeSymbol> GetDeclaredTypes(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol &&
                    symbol.ContainingAssembly is not null &&
                    SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly))
                {
                    yield return symbol;
                }
            }
        }
    }

    private static bool ShouldInspectAssembly(IAssemblySymbol assembly)
        => assembly.Name != "IntelliTect.Coalesce" &&
           assembly.Modules.Any(module =>
               module.ReferencedAssemblySymbols.Any(reference => reference.Name == "IntelliTect.Coalesce"));

    private static IEnumerable<INamedTypeSymbol> GetNamedTypes(INamespaceSymbol @namespace)
    {
        foreach (var member in @namespace.GetMembers())
        {
            foreach (var type in GetNamedTypes(member))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetNamedTypes(INamespaceOrTypeSymbol symbol)
    {
        switch (symbol)
        {
            case INamespaceSymbol @namespace:
                foreach (var member in @namespace.GetMembers())
                {
                    foreach (var type in GetNamedTypes(member))
                    {
                        yield return type;
                    }
                }
                break;
            case INamedTypeSymbol type:
                yield return type;
                foreach (var nestedType in type.GetTypeMembers())
                {
                    foreach (var discovered in GetNamedTypes(nestedType))
                    {
                        yield return discovered;
                    }
                }
                break;
        }
    }
}
