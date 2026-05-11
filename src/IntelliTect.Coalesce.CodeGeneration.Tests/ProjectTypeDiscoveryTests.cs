using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce;
using IntelliTect.Coalesce.TypeDefinition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTect.Coalesce.CodeGeneration.Tests;

public class ProjectTypeDiscoveryTests
{
    [Test]
    public async Task MergeTypes_IncludesTypesFromEachProject()
    {
        var dataType = GetNamedType("namespace DataProject; [IntelliTect.Coalesce.Coalesce] public class DataRoot { }", "DataProject.DataRoot");
        var webType = GetNamedType("namespace WebProject; [IntelliTect.Coalesce.Coalesce] public class WebRoot { }", "WebProject.WebRoot");

        var merged = ProjectTypeDiscovery.MergeTypes([
            ("data.csproj", [dataType]),
            ("web.csproj", [webType]),
        ]);

        await Assert.That(merged.Select(type => type.ToDisplayString(SymbolTypeViewModel.DefaultDisplayFormat)))
            .IsEquivalentTo(["DataProject.DataRoot", "WebProject.WebRoot"]);
    }

    [Test]
    public async Task MergeTypes_PrefersEarlierProjectWhenTypeNameCollides()
    {
        var dataType = GetNamedType("namespace Shared; [IntelliTect.Coalesce.Coalesce] public class TenantRead { }", "Shared.TenantRead");
        var webType = GetNamedType("namespace Shared; [IntelliTect.Coalesce.Coalesce] public class TenantRead { }", "Shared.TenantRead");

        var merged = ProjectTypeDiscovery.MergeTypes([
            ("data.csproj", [dataType]),
            ("web.csproj", [webType]),
        ]);

        await Assert.That(merged).HasSingleItem();
        await Assert.That(SymbolEqualityComparer.Default.Equals(merged.Single(), dataType)).IsTrue();
    }

    [Test]
    public async Task ResolveTypes_UsesMasterProjectSymbolUniverse()
    {
        var dataCompilation = CreateCompilation("DataProject", "namespace Shared; [IntelliTect.Coalesce.Coalesce] public class TenantRead { public SharedEntity Entity { get; set; } } public class SharedEntity { }");
        var dataReference = dataCompilation.ToMetadataReference();
        var webCompilation = CreateCompilation(
            "WebProject",
            "namespace WebProject; [IntelliTect.Coalesce.Coalesce] public class WebRoot { }",
            dataReference);

        var dataType = dataCompilation.GetTypeByMetadataName("Shared.TenantRead")
            ?? throw new InvalidOperationException("Could not resolve Shared.TenantRead from data compilation.");
        var webType = webCompilation.GetTypeByMetadataName("WebProject.WebRoot")
            ?? throw new InvalidOperationException("Could not resolve WebProject.WebRoot from web compilation.");

        var resolved = ProjectTypeDiscovery.ResolveTypes([
            new ProjectTypeDiscovery.DiscoveryProject(
                "data.csproj",
                [dataType],
                _ => null,
                []),
            new ProjectTypeDiscovery.DiscoveryProject(
                "web.csproj",
                [webType],
                typeName => webCompilation.GetTypeByMetadataName(typeName),
                []),
        ]);

        await Assert.That(resolved.Select(type => type.ToDisplayString(SymbolTypeViewModel.DefaultDisplayFormat)))
            .IsEquivalentTo(["Shared.TenantRead", "WebProject.WebRoot"]);
        await Assert.That(SymbolEqualityComparer.Default.Equals(
            resolved.Single(type => type.ToDisplayString(SymbolTypeViewModel.DefaultDisplayFormat) == "Shared.TenantRead"),
            webCompilation.GetTypeByMetadataName("Shared.TenantRead")!)).IsTrue();
    }

    private static INamedTypeSymbol GetNamedType(string source, string typeName)
        => CreateCompilation(Guid.NewGuid().ToString("N"), source).GetTypeByMetadataName(typeName)
            ?? throw new InvalidOperationException($"Could not resolve {typeName}.");

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        params MetadataReference[] additionalReferences)
    {
        return CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(CoalesceAttribute).Assembly.Location),
                ..additionalReferences,
            ]);
    }
}
