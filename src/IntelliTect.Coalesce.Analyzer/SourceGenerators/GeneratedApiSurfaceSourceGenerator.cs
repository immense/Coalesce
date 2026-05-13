using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace IntelliTect.Coalesce.Analyzer.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public sealed class GeneratedApiSurfaceSourceGenerator : IIncrementalGenerator
{
    private const string SnapshotFileName = "coalesce-generated-csharp.json";

    private static readonly DiagnosticDescriptor SnapshotFailedDiagnostic = new(
        id: "COALESCESG001",
        title: "Coalesce in-memory C# generation failed",
        messageFormat: "{0}",
        category: "Coalesce.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GeneratedFilesStillPresentDiagnostic = new(
        id: "COALESCESG002",
        title: "Coalesce generated C# files are still included in the compilation",
        messageFormat: "{0}",
        category: "Coalesce.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var snapshots = context.AdditionalTextsProvider
            .Where(static file => string.Equals(Path.GetFileName(file.Path), SnapshotFileName, StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancellationToken) => LoadSnapshot(file, cancellationToken))
            .Collect();

        var inputs = context.CompilationProvider.Combine(snapshots);
        context.RegisterSourceOutput(inputs, static (productionContext, input) =>
            Emit(productionContext, input.Left, input.Right));
    }

    private static SnapshotParseResult LoadSnapshot(AdditionalText file, CancellationToken cancellationToken)
    {
        try
        {
            var sourceText = file.GetText(cancellationToken);
            if (sourceText is null)
            {
                return SnapshotParseResult.Failure(file.Path, $"Unable to read Coalesce generated source snapshot '{file.Path}'.");
            }

            var files = ParseSnapshot(sourceText.ToString());
            return SnapshotParseResult.Success(file.Path, files);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return SnapshotParseResult.Failure(file.Path, $"Unable to parse Coalesce generated source snapshot '{file.Path}': {ex.Message}");
        }
    }

    private static IReadOnlyDictionary<string, string> ParseSnapshot(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        JsonElement filesElement;

        if (root.ValueKind == JsonValueKind.Object && root.EnumerateObject().All(static property => property.Value.ValueKind == JsonValueKind.String))
        {
            filesElement = root;
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("files", out var nestedFiles))
        {
            filesElement = nestedFiles;
        }
        else
        {
            throw new InvalidOperationException($"Expected '{SnapshotFileName}' to contain either a JSON object map or an object with a 'files' property.");
        }

        if (filesElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Expected '{SnapshotFileName}' to contain a JSON object of generated files.");
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in filesElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"Generated source '{property.Name}' in '{SnapshotFileName}' must be a JSON string.");
            }

            files[NormalizePath(property.Name)] = property.Value.GetString() ?? string.Empty;
        }

        return files;
    }

    private static void Emit(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<SnapshotParseResult> snapshots)
    {
        if (snapshots.IsDefaultOrEmpty)
        {
            return;
        }

        var generatedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots)
        {
            if (snapshot.ErrorMessage is { Length: > 0 })
            {
                context.ReportDiagnostic(Diagnostic.Create(SnapshotFailedDiagnostic, location: null, snapshot.ErrorMessage));
                continue;
            }

            foreach (var generatedFile in snapshot.Files)
            {
                generatedFiles[NormalizePath(generatedFile.Key)] = generatedFile.Value;
            }
        }

        if (generatedFiles.Count == 0)
        {
            return;
        }

        var stillPresentPaths = GetStillPresentGeneratedFilePaths(compilation, generatedFiles.Keys);
        if (stillPresentPaths.Count > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratedFilesStillPresentDiagnostic,
                location: null,
                $"Coalesce generated C# files are compiled from disk while the generated snapshot is also being applied: {string.Join(", ", stillPresentPaths.Take(5))}{(stillPresentPaths.Count > 5 ? ", ..." : string.Empty)}. Delete or remove the on-disk generated files before enabling Coalesce in-memory C# generation."));
            return;
        }

        foreach (var generatedFile in generatedFiles.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            context.AddSource(generatedFile.Key, SourceText.From(generatedFile.Value, Encoding.UTF8));
        }
    }

    private static List<string> GetStillPresentGeneratedFilePaths(Compilation compilation, IEnumerable<string> generatedFilePaths)
    {
        var includeModels = generatedFilePaths.Any(path => path.StartsWith("Models/Generated/", StringComparison.OrdinalIgnoreCase));
        var includeControllers = generatedFilePaths.Any(path => path.StartsWith("Api/Generated/", StringComparison.OrdinalIgnoreCase));

        if (!includeModels && !includeControllers)
        {
            return [];
        }

        return compilation.SyntaxTrees
            .Select(tree => tree.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePath(path!))
            .Where(path =>
                (includeModels && path.Contains("/Models/Generated/", StringComparison.OrdinalIgnoreCase)) ||
                (includeControllers && path.Contains("/Api/Generated/", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');

    private sealed class SnapshotParseResult
    {
        public SnapshotParseResult(string path, IReadOnlyDictionary<string, string> files, string? errorMessage)
        {
            Path = path;
            Files = files;
            ErrorMessage = errorMessage;
        }

        public string Path { get; }
        public IReadOnlyDictionary<string, string> Files { get; }
        public string? ErrorMessage { get; }

        public static SnapshotParseResult Success(string path, IReadOnlyDictionary<string, string> files)
            => new(path, files, null);

        public static SnapshotParseResult Failure(string path, string errorMessage)
            => new(path, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), errorMessage);
    }
}
