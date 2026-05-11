using IntelliTect.Coalesce.CodeGeneration.Api.Generators;
using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.Testing;
using IntelliTect.Coalesce.Testing.Util;
using IntelliTect.Coalesce.TypeDefinition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace IntelliTect.Coalesce.CodeGeneration.Tests;

public class GeneratedContractShapeGenerationTests : CodeGenTestBase
{
    [Test]
    public async Task GeneratedContracts_SupportShapeLevelNullabilityTransforms()
    {
        var executor = BuildExecutor();
        var generatorServices = executor.ServiceProvider.GetRequiredService<GeneratorServices>();

        var compilation = ReflectionRepositoryFactory.GetCompilation(ReflectionRepositoryFactory.ModelSyntaxTrees);
        var sourceType = compilation.GetTypeByMetadataName(
            "IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext.GeneratedContractShapeEntity");
        await Assert.That(sourceType).IsNotNull();
        var shapeSourceType = sourceType!;

        var getShapes = typeof(GeneratedContracts)
            .GetMethod("GetShapes", BindingFlags.NonPublic | BindingFlags.Static)!;
        var resolveProperties = typeof(GeneratedContracts)
            .GetMethod("ResolveProperties", BindingFlags.NonPublic | BindingFlags.Static)!;

        var shapes = ((IEnumerable<GeneratedContracts.ContractShape>)getShapes.Invoke(null, [shapeSourceType])!)
            .ToDictionary(shape => shape.TypeName, StringComparer.Ordinal);

        async Task<string> RenderShape(string typeName)
        {
            var shape = shapes[typeName];
            var properties = (IReadOnlyList<GeneratedContracts.ContractPropertyModel>)resolveProperties.Invoke(null, [shapeSourceType, shape])!;
            var file = new GeneratedContractFile(generatorServices)
            {
                Model = new GeneratedContracts.GeneratedContractFileModel(
                    shape,
                    properties,
                    shapeSourceType.ToDisplayString(SymbolTypeViewModel.DefaultDisplayFormat)),
            };

            return await file.BuildOutputAsync();
        }

        var detailsContents = await RenderShape("IGeneratedContractShapeDetails");
        var writeBaseContents = await RenderShape("IGeneratedContractShapeWriteBase");
        var createContents = await RenderShape("GeneratedContractShapeCreate");
        var patchContents = await RenderShape("IGeneratedContractShapePatch");

        await Assert.That(detailsContents.Contains("string Description { get; }")).IsTrue();
        await Assert.That(detailsContents.Contains("string Name { get; }")).IsTrue();

        await Assert.That(writeBaseContents.Contains("string Name { get; }")).IsTrue();
        await Assert.That(writeBaseContents.Contains("string? Description { get; }")).IsTrue();
        await Assert.That(writeBaseContents.Contains("string? OptionalNotes { get; }")).IsTrue();
        await Assert.That(writeBaseContents.Contains("global::System.Collections.Generic.List<string>? Tags { get; }")).IsTrue();
        await Assert.That(writeBaseContents.Contains("int Count { get; }")).IsTrue();

        await Assert.That(createContents.Contains("public required string Name { get; set; }")).IsTrue();
        await Assert.That(createContents.Contains("public string? Description { get; set; }")).IsTrue();
        await Assert.That(createContents.Contains("public global::System.Collections.Generic.List<string>? Tags { get; set; }")).IsTrue();

        await Assert.That(patchContents.Contains("string? Name { get; }")).IsTrue();
        await Assert.That(patchContents.Contains("int? Count { get; }")).IsTrue();
        await Assert.That(patchContents.Contains("bool? Enabled { get; }")).IsTrue();
        await Assert.That(patchContents.Contains("global::System.DateTime? EffectiveOn { get; }")).IsTrue();

        ReflectionRepositoryFactory.GetCompilation(
            [
                CSharpSyntaxTree.ParseText(SourceText.From(detailsContents), path: "IGeneratedContractShapeDetails.g.cs"),
                CSharpSyntaxTree.ParseText(SourceText.From(writeBaseContents), path: "IGeneratedContractShapeWriteBase.g.cs"),
                CSharpSyntaxTree.ParseText(SourceText.From(createContents), path: "GeneratedContractShapeCreate.g.cs"),
                CSharpSyntaxTree.ParseText(SourceText.From(patchContents), path: "IGeneratedContractShapePatch.g.cs"),
            ],
            assertSuccess: true);
    }

    [Test]
    public async Task GeneratedContracts_PropagateDtoSourceAttributesToClassOutputs()
    {
        var executor = BuildExecutor();
        var generatorServices = executor.ServiceProvider.GetRequiredService<GeneratorServices>();

        var compilation = ReflectionRepositoryFactory.GetCompilation(ReflectionRepositoryFactory.ModelSyntaxTrees);
        var sourceType = compilation.GetTypeByMetadataName(
            "IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext.GeneratedContractShapeProjectionSource");
        await Assert.That(sourceType).IsNotNull();
        var shapeSourceType = sourceType!;

        var getShapes = typeof(GeneratedContracts)
            .GetMethod("GetShapes", BindingFlags.NonPublic | BindingFlags.Static)!;
        var resolveProperties = typeof(GeneratedContracts)
            .GetMethod("ResolveProperties", BindingFlags.NonPublic | BindingFlags.Static)!;

        var shape = ((IEnumerable<GeneratedContracts.ContractShape>)getShapes.Invoke(null, [shapeSourceType])!)
            .Single(candidate => candidate.TypeName == "GeneratedContractShapeProjectionSpec");

        var properties = (IReadOnlyList<GeneratedContracts.ContractPropertyModel>)resolveProperties.Invoke(null, [shapeSourceType, shape])!;
        var file = new GeneratedContractFile(generatorServices)
        {
            Model = new GeneratedContracts.GeneratedContractFileModel(
                shape,
                properties,
                shapeSourceType.ToDisplayString(SymbolTypeViewModel.DefaultDisplayFormat)),
        };

        var contents = await file.BuildOutputAsync();

        await Assert.That(contents.Contains("[global::IntelliTect.Coalesce.DataAnnotations.DtoSource(\"OwnerTenant.Name\")]")).IsTrue();
        await Assert.That(contents.Contains("[global::IntelliTect.Coalesce.DataAnnotations.DtoSource(\"Children\", OrderBy = \"Name\", OrderByDirection = global::IntelliTect.Coalesce.DataAnnotations.DefaultOrderByAttribute.OrderByDirections.Descending)]")).IsTrue();
        await Assert.That(contents.Contains("public string? TenantName { get; set; }")).IsTrue();
        await Assert.That(contents.Contains("public global::System.Collections.Generic.List<string> OrderedChildren { get; set; } = [];")).IsTrue();

        ReflectionRepositoryFactory.GetCompilation(
            [CSharpSyntaxTree.ParseText(SourceText.From(contents), path: "GeneratedContractShapeProjectionSpec.g.cs")],
            assertSuccess: true);
    }
}
