using IntelliTect.Coalesce.CodeGeneration.Analysis.Roslyn;
using IntelliTect.Coalesce.Testing;
using IntelliTect.Coalesce.Testing.Util;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace IntelliTect.Coalesce.CodeGeneration.Tests;

public class GeneratedContractCompilationAugmentorTests
{
    [Test]
    public async Task SameProjectGeneratedClassShapes_ForTheSourceType_DoNotDuplicateMembers()
    {
        var source = $$"""
            #nullable enable
            using IntelliTect.Coalesce.DataAnnotations;

            namespace IntelliTect.Coalesce.Testing.GeneratedContracts;

            [GeneratedContractShape(
                "same-project-generated-class",
                GeneratedContractOutputKind.Class,
                "{{ReflectionRepositoryFactory.SymbolDiscoveryAssemblyName}}",
                "IntelliTect.Coalesce.Testing.GeneratedContracts",
                "SameProjectGeneratedClass",
                Members = [nameof(Name)])]
            public partial class SameProjectGeneratedClass
            {
                public string Name { get; set; } = null!;
            }
            """;

        var compilation = ReflectionRepositoryFactory.GetCompilation(
            [CSharpSyntaxTree.ParseText(SourceText.From(source), path: "SameProjectGeneratedClass.cs")],
            assertSuccess: true);

        var augmentedCompilation = (CSharpCompilation)GeneratedContractCompilationAugmentor
            .AugmentWithSameProjectGeneratedContracts(compilation, "/tmp/coalesce-generated-contracts-test");

        ReflectionRepositoryFactory.AssertCompilationSuccess(augmentedCompilation);
        await Task.CompletedTask;
    }
}
