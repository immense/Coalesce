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

    private sealed class MetadataReferencePathComparer : IEqualityComparer<MetadataReference>
    {
        public static MetadataReferencePathComparer Instance { get; } = new();

        public bool Equals(MetadataReference? x, MetadataReference? y)
            => StringComparer.OrdinalIgnoreCase.Equals((x as PortableExecutableReference)?.FilePath, (y as PortableExecutableReference)?.FilePath);

        public int GetHashCode(MetadataReference obj)
            => StringComparer.OrdinalIgnoreCase.GetHashCode((obj as PortableExecutableReference)?.FilePath ?? string.Empty);
    }
}
