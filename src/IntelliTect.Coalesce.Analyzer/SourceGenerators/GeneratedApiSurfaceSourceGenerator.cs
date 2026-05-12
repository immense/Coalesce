using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace IntelliTect.Coalesce.Analyzer.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public sealed class GeneratedApiSurfaceSourceGenerator : IIncrementalGenerator
{
    private const string CaptureEnvironmentVariable = "COALESCE_SOURCEGEN_CAPTURE";
    private const string ModelsGeneratorName = "Models";
    private const string ModelsGeneratorFullName = "IntelliTect.Coalesce.CodeGeneration.Api.Generators.Models";
    private const string ControllersGeneratorName = "Controllers";
    private const string ControllersGeneratorFullName = "IntelliTect.Coalesce.CodeGeneration.Api.Generators.Controllers";
    private const string ConfigFileName = "coalesce.json";
    private const string CaptureOptionName = "--emit-csharp-sourcegen";

    private static readonly DiagnosticDescriptor CaptureFailedDiagnostic = new(
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
        context.RegisterSourceOutput(context.CompilationProvider, static (productionContext, compilation) =>
            Execute(productionContext, compilation));
    }

    private static void Execute(SourceProductionContext context, Compilation compilation)
    {
        if (string.Equals(Environment.GetEnvironmentVariable(CaptureEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(compilation.AssemblyName))
        {
            return;
        }

        var configPath = FindCoalesceConfigurationPath(compilation);
        if (configPath is null)
        {
            return;
        }

        SourceGenerationConfig config;
        try
        {
            config = SourceGenerationConfig.Load(configPath);
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CaptureFailedDiagnostic,
                location: null,
                $"Unable to read '{ConfigFileName}' for Coalesce in-memory C# generation: {ex.Message}"));
            return;
        }

        if (!config.ShouldGenerateAnyCategory)
        {
            return;
        }

        if (!string.Equals(config.WebProjectAssemblyName, NormalizeAssemblyName(compilation.AssemblyName!), StringComparison.Ordinal))
        {
            return;
        }

        var stillPresentPaths = GetStillPresentGeneratedFilePaths(compilation, config.GenerateModels, config.GenerateControllers);
        if (stillPresentPaths.Count > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratedFilesStillPresentDiagnostic,
                location: null,
                $"Coalesce in-memory C# generation is enabled via '{ConfigFileName}', but generated files are still compiled from disk: {string.Join(", ", stillPresentPaths.Take(5))}{(stillPresentPaths.Count > 5 ? ", ..." : string.Empty)}. Delete or remove the on-disk generated files before enabling the source generator."));
            return;
        }

        string captureFilePath = Path.Combine(Path.GetTempPath(), $"coalesce-csharp-sourcegen-{Guid.NewGuid():N}.json");
        try
        {
            var captureResult = InvokeCoalesceTool(configPath, captureFilePath);
            if (!captureResult.Success)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CaptureFailedDiagnostic,
                    location: null,
                    captureResult.ErrorMessage));
                return;
            }

            var outputs = LoadCapturedSources(captureFilePath);
            foreach (var output in outputs)
            {
                var normalizedPath = NormalizePath(output.Key);
                if (!ShouldEmit(normalizedPath, config.GenerateModels, config.GenerateControllers))
                {
                    continue;
                }

                context.AddSource(normalizedPath, SourceText.From(output.Value, Encoding.UTF8));
            }
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CaptureFailedDiagnostic,
                location: null,
                $"Coalesce in-memory C# generation failed: {ex.Message}"));
        }
        finally
        {
            try
            {
                if (File.Exists(captureFilePath))
                {
                    File.Delete(captureFilePath);
                }
            }
            catch
            {
                // Best effort only.
            }
        }
    }

    private static string? FindCoalesceConfigurationPath(Compilation compilation)
    {
        foreach (var filePath in compilation.SyntaxTrees
            .Select(tree => tree.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(filePath!);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                var candidate = Path.Combine(directory!, ConfigFileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = Directory.GetParent(directory!)?.FullName;
            }
        }

        return null;
    }

    private static List<string> GetStillPresentGeneratedFilePaths(Compilation compilation, bool includeModels, bool includeControllers)
        => compilation.SyntaxTrees
            .Select(tree => tree.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePath(path!))
            .Where(path =>
                (includeModels && path.Contains("/Models/Generated/", StringComparison.OrdinalIgnoreCase)) ||
                (includeControllers && path.Contains("/Api/Generated/", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool ShouldEmit(string normalizedPath, bool includeModels, bool includeControllers)
        => (includeModels && normalizedPath.StartsWith("Models/Generated/", StringComparison.OrdinalIgnoreCase))
        || (includeControllers && normalizedPath.StartsWith("Api/Generated/", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, string> LoadCapturedSources(string captureFilePath)
    {
        if (!File.Exists(captureFilePath))
        {
            throw new FileNotFoundException($"Coalesce source-generation capture file was not created: {captureFilePath}");
        }

        var json = File.ReadAllText(captureFilePath);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static CaptureInvocationResult InvokeCoalesceTool(string configPath, string captureFilePath)
    {
        var workingDirectory = Path.GetDirectoryName(configPath)!;
        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Environment.GetEnvironmentVariable("DOTNET_EXE")
            ?? "dotnet";
        var linkedToolDllPath = TryGetLinkedSourceToolDllPath();

        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetHost,
            Arguments = linkedToolDllPath is { Length: > 0 }
                ? $"{Quote(linkedToolDllPath)} {Quote(configPath)} {CaptureOptionName} {Quote(captureFilePath)} --verbosity Error"
                : $"coalesce {Quote(configPath)} {CaptureOptionName} {Quote(captureFilePath)} --verbosity Error",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.EnvironmentVariables[CaptureEnvironmentVariable] = "1";

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return CaptureInvocationResult.Failure("Failed to start 'dotnet coalesce' for Coalesce in-memory C# generation.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(300_000))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Ignore kill failures when timing out.
            }

            return CaptureInvocationResult.Failure("'dotnet coalesce' timed out while capturing Coalesce in-memory C# sources.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            var details = string.Join(Environment.NewLine,
                new[]
                {
                    TrimOutput("stdout", stdout),
                    TrimOutput("stderr", stderr),
                }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return CaptureInvocationResult.Failure(
                $"'dotnet coalesce' exited with code {process.ExitCode} while capturing Coalesce in-memory C# sources.{(string.IsNullOrWhiteSpace(details) ? string.Empty : Environment.NewLine + details)}");
        }

        return CaptureInvocationResult.Successful();
    }

    private static string TrimOutput(string streamName, string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        const int maxLength = 4_000;
        var trimmed = output!.Length <= maxLength ? output : output.Substring(output.Length - maxLength, maxLength);
        return $"{streamName}:{Environment.NewLine}{trimmed.Trim()}";
    }

    private static string? TryGetLinkedSourceToolDllPath()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(GeneratedApiSurfaceSourceGenerator).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            return null;
        }

        var candidateRoots = EnumerateAncestorDirectories(assemblyDirectory!);
        var candidateRelativePaths = new[]
        {
            Path.Combine("src", "IntelliTect.Coalesce.DotnetTool", "bin", "Debug", "net10.0", "dotnet-coalesce.dll"),
            Path.Combine("src", "IntelliTect.Coalesce.DotnetTool", "bin", "Release", "net10.0", "dotnet-coalesce.dll"),
            Path.Combine("src", "IntelliTect.Coalesce.DotnetTool", "bin", "Debug", "net9.0", "dotnet-coalesce.dll"),
            Path.Combine("src", "IntelliTect.Coalesce.DotnetTool", "bin", "Release", "net9.0", "dotnet-coalesce.dll"),
            Path.Combine("src", "IntelliTect.Coalesce.DotnetTool", "bin", "Debug", "net8.0", "dotnet-coalesce.dll"),
            Path.Combine("src", "IntelliTect.Coalesce.DotnetTool", "bin", "Release", "net8.0", "dotnet-coalesce.dll"),
        };

        foreach (var root in candidateRoots)
        {
            foreach (var relativePath in candidateRelativePaths)
            {
                var candidate = Path.Combine(root, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateAncestorDirectories(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static string Quote(string value)
        => '"' + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');

    private static string NormalizeAssemblyName(string assemblyName)
        => assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || assemblyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(assemblyName)
            : assemblyName;

    private sealed class CaptureInvocationResult
    {
        public CaptureInvocationResult(bool success, string errorMessage)
        {
            Success = success;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }
        public string ErrorMessage { get; }

        public static CaptureInvocationResult Successful() => new(true, string.Empty);
        public static CaptureInvocationResult Failure(string message) => new(false, message);
    }

    private sealed class SourceGenerationConfig
    {
        public string WebProjectAssemblyName { get; set; } = string.Empty;
        public bool GenerateModels { get; set; }
        public bool GenerateControllers { get; set; }

        public bool ShouldGenerateAnyCategory => GenerateModels || GenerateControllers;

        public static SourceGenerationConfig Load(string configPath)
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (!root.TryGetProperty("webProject", out var webProject) || !webProject.TryGetProperty("projectFile", out var projectFileProperty))
            {
                throw new InvalidOperationException($"'{ConfigFileName}' does not contain a webProject.projectFile entry.");
            }

            var configDirectory = Path.GetDirectoryName(configPath)!;
            var resolvedWebProjectPath = Path.GetFullPath(Path.Combine(configDirectory, projectFileProperty.GetString()!));
            var webProjectAssemblyName = GetProjectAssemblyName(resolvedWebProjectPath);

            return new SourceGenerationConfig
            {
                WebProjectAssemblyName = webProjectAssemblyName,
                GenerateModels = IsGeneratorDisabled(root, ModelsGeneratorName, ModelsGeneratorFullName),
                GenerateControllers = IsGeneratorDisabled(root, ControllersGeneratorName, ControllersGeneratorFullName),
            };
        }

        private static bool IsGeneratorDisabled(JsonElement root, string shortName, string fullName)
        {
            if (!root.TryGetProperty("generatorConfig", out var generatorConfig))
            {
                return false;
            }

            foreach (var key in new[] { fullName, shortName })
            {
                if (!generatorConfig.TryGetProperty(key, out var generatorSettings))
                {
                    continue;
                }

                if (generatorSettings.ValueKind == JsonValueKind.Object &&
                    generatorSettings.TryGetProperty("disabled", out var disabledProperty) &&
                    disabledProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return disabledProperty.GetBoolean();
                }
            }

            return false;
        }

        private static string GetProjectAssemblyName(string projectFilePath)
        {
            if (!File.Exists(projectFilePath))
            {
                throw new FileNotFoundException($"Configured Coalesce web project was not found: {projectFilePath}");
            }

            var projectDocument = XDocument.Load(projectFilePath);
            var assemblyName = projectDocument
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "AssemblyName")
                ?.Value
                ?.Trim();

            return NormalizeAssemblyName(string.IsNullOrWhiteSpace(assemblyName)
                ? Path.GetFileNameWithoutExtension(projectFilePath)
                : assemblyName!);
        }
    }
}
