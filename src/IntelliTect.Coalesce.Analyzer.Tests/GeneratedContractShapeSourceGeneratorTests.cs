#nullable enable

using IntelliTect.Coalesce.DataAnnotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace IntelliTect.Coalesce.Analyzer.Tests;

public class GeneratedContractShapeSourceGeneratorTests
{
    [Test]
    public async Task GeneratesSameProjectContractShapesIntoCompilation()
    {
        var compilation = CreateCompilation(
            "GeneratedContractConsumer",
            """
            #nullable enable
            using System.Collections.Generic;
            using IntelliTect.Coalesce.DataAnnotations;

            namespace Demo;

            [GeneratedContractShape(
                "same-project",
                GeneratedContractOutputKind.Class,
                "GeneratedContractConsumer",
                "Demo.Contracts",
                "PersonContract",
                Members = [nameof(Name), nameof(Tags)])]
            public class PersonSource
            {
                public string Name { get; set; } = null!;
                public List<string> Tags { get; set; } = [];
            }

            public class Consumer
            {
                public Demo.Contracts.PersonContract Contract { get; set; } = new()
                {
                    Name = string.Empty,
                    Tags = []
                };
            }
            """);

        var updatedCompilation = RunGenerator(compilation, out var diagnostics);

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();
        await Assert.That(updatedCompilation.SyntaxTrees.Any(tree => tree.FilePath.Contains("Demo.Contracts.PersonContract.g.cs", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task GeneratesContractsFromReferencedAssembliesIntoTargetCompilation()
    {
        var producerCompilation = CreateCompilation(
            "GeneratedContractProducer",
            """
            #nullable enable
            using IntelliTect.Coalesce.DataAnnotations;

            namespace Demo;

            [GeneratedContractShape(
                "referenced-assembly",
                GeneratedContractOutputKind.Class,
                "GeneratedContractConsumer",
                "Demo.Contracts",
                "ReferencedPersonContract",
                Members = [nameof(Name)])]
            public class PersonSource
            {
                public string Name { get; set; } = null!;
            }
            """);

        var producerReference = CreateMetadataReference(producerCompilation);

        var consumerCompilation = CreateCompilation(
            "GeneratedContractConsumer",
            """
            #nullable enable

            namespace Demo;

            public class Consumer
            {
                public Demo.Contracts.ReferencedPersonContract Contract { get; set; } = new()
                {
                    Name = string.Empty
                };
            }
            """,
            additionalReferences: [producerReference]);

        var updatedCompilation = RunGenerator(consumerCompilation, out var diagnostics);

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();
        await Assert.That(updatedCompilation.SyntaxTrees.Any(tree => tree.FilePath.Contains("Demo.Contracts.ReferencedPersonContract.g.cs", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task PropagatesSelectedPropertyAttributesToGeneratedContracts()
    {
        var compilation = CreateCompilation(
            "GeneratedContractConsumer",
            """
            #nullable enable
            using System;
            using IntelliTect.Coalesce.DataAnnotations;

            namespace Demo;

            [AttributeUsage(AttributeTargets.Property)]
            public sealed class CopyMeAttribute : Attribute
            {
                public CopyMeAttribute(string label)
                {
                    Label = label;
                }

                public string Label { get; }
                public bool Flag { get; init; }
            }

            [GeneratedContractShape(
                "same-project",
                GeneratedContractOutputKind.Class,
                "GeneratedContractConsumer",
                "Demo.Contracts",
                "AttributedContract",
                Members = [nameof(Name)],
                IncludedPropertyAttributes = [typeof(CopyMeAttribute)])]
            public class PersonSource
            {
                [CopyMe("tenant-name", Flag = true)]
                public string Name { get; set; } = null!;
            }

            public class Consumer
            {
                public Demo.Contracts.AttributedContract Contract { get; set; } = new()
                {
                    Name = string.Empty
                };
            }
            """);

        var updatedCompilation = RunGenerator(compilation, out var diagnostics);

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();

        var generatedTree = updatedCompilation.SyntaxTrees
            .Single(tree => tree.FilePath.Contains("Demo.Contracts.AttributedContract.g.cs", StringComparison.Ordinal));
        var generatedText = generatedTree.GetText().ToString();

        await Assert.That(generatedText.Contains("[global::Demo.CopyMeAttribute(\"tenant-name\", Flag = true)]")).IsTrue();
        await Assert.That(generatedText.Contains("public required string Name { get; set; }")).IsTrue();
    }

    [Test]
    public async Task DefaultPolicy_IncludesPublicGetOnlyProperties()
    {
        var compilation = CreateCompilation(
            "GeneratedContractConsumer",
            """
            #nullable enable
            using IntelliTect.Coalesce.DataAnnotations;

            namespace Demo;

            [GeneratedContractShape(
                "same-project",
                GeneratedContractOutputKind.Interface,
                "GeneratedContractConsumer",
                "Demo.Contracts",
                "IMaintenanceSpecifier")]
            public class MaintenanceSpecifierContractSource
            {
                public string MaintenanceIdentifier { get; } = null!;
                public int MaintenanceType { get; } = default!;
            }

            public class Consumer
            {
                public void Read(Demo.Contracts.IMaintenanceSpecifier value)
                {
                    _ = value.MaintenanceIdentifier;
                    _ = value.MaintenanceType;
                }
            }
            """);

        var updatedCompilation = RunGenerator(compilation, out var diagnostics);

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();

        var generatedTree = updatedCompilation.SyntaxTrees
            .Single(tree => tree.FilePath.Contains("Demo.Contracts.IMaintenanceSpecifier.g.cs", StringComparison.Ordinal));
        var generatedText = generatedTree.GetText().ToString();

        await Assert.That(generatedText.Contains("string MaintenanceIdentifier { get; }")).IsTrue();
        await Assert.That(generatedText.Contains("int MaintenanceType { get; }")).IsTrue();
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        IReadOnlyList<MetadataReference>? additionalReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            new CSharpParseOptions(LanguageVersion.Preview),
            path: $"{assemblyName}.cs");

        var references = GetMetadataReferences();
        if (additionalReferences is not null)
        {
            references.AddRange(additionalReferences);
        }

        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references.Distinct(MetadataReferencePathComparer.Instance),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static CSharpCompilation RunGenerator(CSharpCompilation compilation, out ImmutableArray<Diagnostic> diagnostics)
    {
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new GeneratedContractShapeSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out diagnostics);
        return (CSharpCompilation)updatedCompilation;
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

    private static List<MetadataReference> GetMetadataReferences()
        => AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(GeneratedContractShapeAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .ToList();

    [Test]
    public async Task SimplifiedConventionConstructor_InfersAssemblyNamespaceAndTypeName()
    {
        var compilation = CreateCompilation(
            "MyApp.Web",
            """
            #nullable enable
            using IntelliTect.Coalesce.DataAnnotations;

            namespace MyApp.Web.Contracts;

            [GeneratedContractShape("add-tags")]
            internal sealed class AddTagsRequestContractSource
            {
                public int EntityId { get; set; }
                public string TagName { get; set; } = null!;
                public bool Active { get; set; }
            }

            public class Consumer
            {
                // Should resolve to "AddTagsRequest" (minus "ContractSource" suffix)
                // in namespace "MyApp.Web.Contracts" (same as source class)
                // in assembly "MyApp.Web" (same as compilation)
                public MyApp.Web.Contracts.AddTagsRequest Request { get; set; } = new()
                {
                    EntityId = 1,
                    TagName = "hi",
                    Active = true
                };
            }
            """);

        var updatedCompilation = RunGenerator(compilation, out var diagnostics);

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();
        await Assert.That(updatedCompilation.SyntaxTrees.Any(tree => tree.FilePath.Contains("AddTagsRequest.g.cs", StringComparison.Ordinal))).IsTrue();

        // Verify the generated source contains constructors
        var generatedTree = updatedCompilation.SyntaxTrees.First(tree => tree.FilePath.Contains("AddTagsRequest.g.cs", StringComparison.Ordinal));
        var generatedText = generatedTree.GetText().ToString();
        await Assert.That(generatedText.Contains("public AddTagsRequest()")).IsTrue();
        await Assert.That(generatedText.Contains("public AddTagsRequest(")).IsTrue();
    }

    [Test]
    public async Task SimplifiedConventionConstructor_WithSourceSuffix_InfersTypeName()
    {
        var compilation = CreateCompilation(
            "MyApp.Web",
            """
            #nullable enable
            using IntelliTect.Coalesce.DataAnnotations;

            namespace MyApp.Web.Dto;

            [GeneratedContractShape("my-entity")]
            internal sealed class MyEntitySource
            {
                public string Name { get; set; } = null!;
                public int Count { get; set; }
            }

            public class Consumer
            {
                public MyApp.Web.Dto.MyEntity Entity { get; set; } = new() { Name = "x", Count = 1 };
            }
            """);

        var updatedCompilation = RunGenerator(compilation, out var diagnostics);

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();
        await Assert.That(updatedCompilation.SyntaxTrees.Any(tree => tree.FilePath.Contains("MyEntity.g.cs", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task GenerateConstructors_False_OmitsConstructors()
    {
        var compilation = CreateCompilation(
            "MyApp.Web",
            """
            #nullable enable
            using IntelliTect.Coalesce.DataAnnotations;

            namespace MyApp.Web.Contracts;

            [GeneratedContractShape("no-ctor", GenerateConstructors = false)]
            internal sealed class NoCtorContractSource
            {
                public string Name { get; set; } = null!;
            }

            public class Consumer
            {
                public MyApp.Web.Contracts.NoCtor Item { get; set; } = new() { Name = "x" };
            }
            """);

        var updatedCompilation = RunGenerator(compilation, out var diagnostics);

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();

        var generatedTree = updatedCompilation.SyntaxTrees.First(tree => tree.FilePath.Contains("NoCtor.g.cs", StringComparison.Ordinal));
        var generatedText = generatedTree.GetText().ToString();
        // Should NOT contain constructors
        await Assert.That(generatedText.Contains("public NoCtor(")).IsFalse();
    }

    private sealed class MetadataReferencePathComparer : IEqualityComparer<MetadataReference>
    {
        public static MetadataReferencePathComparer Instance { get; } = new();

        public bool Equals(MetadataReference? x, MetadataReference? y)
            => StringComparer.OrdinalIgnoreCase.Equals((x as PortableExecutableReference)?.FilePath, (y as PortableExecutableReference)?.FilePath);

        public int GetHashCode(MetadataReference obj)
            => StringComparer.OrdinalIgnoreCase.GetHashCode((obj as PortableExecutableReference)?.FilePath ?? string.Empty);
    }
}
