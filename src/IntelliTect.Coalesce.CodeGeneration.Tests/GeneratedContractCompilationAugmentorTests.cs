using IntelliTect.Coalesce.CodeGeneration.Analysis.Roslyn;
using IntelliTect.Coalesce.Testing;
using IntelliTect.Coalesce.Testing.Util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
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

    [Test]
    public async Task ReferencedAssemblyGeneratedClassShapes_AreAugmentedIntoTargetCompilation()
    {
        var producerSource = """
            #nullable enable
            using IntelliTect.Coalesce.DataAnnotations;

            namespace IntelliTect.Coalesce.Testing.GeneratedContracts;

            [GeneratedContractShape(
                "referenced-generated-class",
                GeneratedContractOutputKind.Class,
                "GeneratedContractConsumer",
                "IntelliTect.Coalesce.Testing.GeneratedContracts",
                "ReferencedGeneratedClass",
                Members = [nameof(Name)])]
            public class ReferencedGeneratedClassSource
            {
                public string Name { get; set; } = null!;
            }
            """;

        var producerCompilation = ReflectionRepositoryFactory.GetCompilation(
            [CSharpSyntaxTree.ParseText(SourceText.From(producerSource), path: "ReferencedGeneratedClassSource.cs")],
            assertSuccess: true);

        var producerReference = CreateMetadataReference(producerCompilation);

        var consumerSource = """
            #nullable enable

            namespace IntelliTect.Coalesce.Testing.GeneratedContracts;

            public class Consumer
            {
                public ReferencedGeneratedClass Contract { get; set; } = new()
                {
                    Name = string.Empty
                };
            }
            """;

        var consumerCompilation = (CSharpCompilation)ReflectionRepositoryFactory.GetCompilation(
            [CSharpSyntaxTree.ParseText(SourceText.From(consumerSource), path: "Consumer.cs")],
            assertSuccess: false)
            .AddReferences(producerReference)
            .WithAssemblyName("GeneratedContractConsumer");

        var augmentedCompilation = (CSharpCompilation)GeneratedContractCompilationAugmentor
            .AugmentWithSameProjectGeneratedContracts(consumerCompilation, "/tmp/coalesce-generated-contracts-test");

        ReflectionRepositoryFactory.AssertCompilationSuccess(augmentedCompilation);
        await Task.CompletedTask;
    }

    [Test]
    public async Task GeneratedClassShapes_CanInheritGeneratedBaseClassShape()
    {
        var source = $$"""
            #nullable enable
            using IntelliTect.Coalesce.DataAnnotations;

            namespace IntelliTect.Coalesce.Testing.GeneratedContracts;

            [GeneratedContractShape(
                "generated-base",
                GeneratedContractOutputKind.Class,
                "{{ReflectionRepositoryFactory.SymbolDiscoveryAssemblyName}}",
                "IntelliTect.Coalesce.Testing.GeneratedContracts",
                "GeneratedBaseContract",
                Members = [nameof(Id)],
                GenerateConstructors = true)]
            public class GeneratedBaseContractSource
            {
                public int Id { get; set; }
            }

            [GeneratedContractShape(
                "generated-derived",
                GeneratedContractOutputKind.Class,
                "{{ReflectionRepositoryFactory.SymbolDiscoveryAssemblyName}}",
                "IntelliTect.Coalesce.Testing.GeneratedContracts",
                "GeneratedDerivedContract",
                Members = [nameof(Name)],
                BaseClassTypeName = "global::IntelliTect.Coalesce.Testing.GeneratedContracts.GeneratedBaseContract",
                GenerateConstructors = true)]
            public class GeneratedDerivedContractSource
            {
                public string Name { get; set; } = null!;
            }

            public class Consumer
            {
                public GeneratedDerivedContract Contract { get; set; } = new(1, "name");

                public int Id => Contract.Id;
            }
            """;

        var compilation = ReflectionRepositoryFactory.GetCompilation(
            [CSharpSyntaxTree.ParseText(SourceText.From(source), path: "GeneratedDerivedContractSource.cs")],
            assertSuccess: false);

        var augmentedCompilation = (CSharpCompilation)GeneratedContractCompilationAugmentor
            .AugmentWithSameProjectGeneratedContracts(compilation, "/tmp/coalesce-generated-contracts-test");

        ReflectionRepositoryFactory.AssertCompilationSuccess(augmentedCompilation);
        await Task.CompletedTask;
    }

    private static PortableExecutableReference CreateMetadataReference(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        EmitResult emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, emitResult.Diagnostics));
        }

        stream.Position = 0;
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
