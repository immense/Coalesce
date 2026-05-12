#nullable enable

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace IntelliTect.Coalesce.Analyzer.Tests;

public class GeneratedApiSurfaceSourceGeneratorTests
{
    [Test]
    public async Task GeneratesSourcesFromSnapshotAdditionalFile()
    {
        var compilation = CreateCompilation(
            "GeneratedApiSurfaceConsumer",
            ("Consumer.cs",
            """
            namespace Demo;

            public class Consumer
            {
                public Demo.Generated.PersonDto Contract { get; set; } = new();
            }
            """)
        );

        var snapshot = new InMemoryAdditionalText(
            "/tmp/obj/coalesce-generated-csharp.json",
            """
            {
              "Models/Generated/PersonDto.g.cs": "namespace Demo.Generated { public class PersonDto { public string Name { get; set; } = string.Empty; } }"
            }
            """);

        var updatedCompilation = RunGenerator(compilation, [snapshot], out var diagnostics);

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();
        await Assert.That(updatedCompilation.SyntaxTrees.Any(tree => tree.FilePath.Contains("Models/Generated/PersonDto.g.cs", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task ReportsDiagnosticForInvalidSnapshot()
    {
        var compilation = CreateCompilation(
            "GeneratedApiSurfaceConsumer",
            ("Consumer.cs", "namespace Demo; public class Consumer { }")
        );

        var snapshot = new InMemoryAdditionalText(
            "/tmp/obj/coalesce-generated-csharp.json",
            "{ invalid json }");

        RunGenerator(compilation, [snapshot], out var diagnostics);

        await Assert.That(diagnostics.Select(d => d.Id)).Contains("COALESCESG001");
    }

    [Test]
    public async Task ReportsDiagnosticWhenGeneratedFilesAreStillCompiledFromDisk()
    {
        var compilation = CreateCompilation(
            "GeneratedApiSurfaceConsumer",
            ("Consumer.cs", "namespace Demo; public class Consumer { }"),
            ("/repo/Models/Generated/PersonDto.g.cs", "namespace Demo.Generated { public class PersonDto { } }")
        );

        var snapshot = new InMemoryAdditionalText(
            "/tmp/obj/coalesce-generated-csharp.json",
            """
            {
              "Models/Generated/PersonDto.g.cs": "namespace Demo.Generated { public class PersonDto { } }"
            }
            """);

        RunGenerator(compilation, [snapshot], out var diagnostics);

        await Assert.That(diagnostics.Select(d => d.Id)).Contains("COALESCESG002");
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        params (string Path, string Source)[] sources)
    {
        var syntaxTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(
                SourceText.From(source.Source, Encoding.UTF8),
                new CSharpParseOptions(LanguageVersion.Preview),
                path: source.Path))
            .ToArray();

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static CSharpCompilation RunGenerator(
        CSharpCompilation compilation,
        ImmutableArray<AdditionalText> additionalTexts,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new GeneratedApiSurfaceSourceGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts,
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out diagnostics);
        return (CSharpCompilation)updatedCompilation;
    }

    private static List<MetadataReference> GetMetadataReferences()
        => AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .Distinct(MetadataReferencePathComparer.Instance)
            .ToList();

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(content, Encoding.UTF8);
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
