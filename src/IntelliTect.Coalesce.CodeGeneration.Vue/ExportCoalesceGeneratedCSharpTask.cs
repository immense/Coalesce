#nullable enable

using IntelliTect.Coalesce.CodeGeneration.Analysis.MsBuild;
using IntelliTect.Coalesce.CodeGeneration.Analysis.Roslyn;
using IntelliTect.Coalesce.CodeGeneration.Configuration;
using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.CodeGeneration.Vue.Generators;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json;

namespace IntelliTect.Coalesce.CodeGeneration.Vue.Tasks;

public sealed class ExportCoalesceGeneratedCSharpTask : Microsoft.Build.Utilities.Task
{
    private const string ConfigFileName = "coalesce.json";
    private const string ModelsGeneratorName = "Models";
    private const string ModelsGeneratorFullName = "IntelliTect.Coalesce.CodeGeneration.Api.Generators.Models";
    private const string ControllersGeneratorName = "Controllers";
    private const string ControllersGeneratorFullName = "IntelliTect.Coalesce.CodeGeneration.Api.Generators.Controllers";
    private const string ScriptsGeneratorName = "Scripts";
    private const string ScriptsGeneratorFullName = "IntelliTect.Coalesce.CodeGeneration.Vue.Generators.Scripts";
    private const string KernelPluginsGeneratorName = "KernelPlugins";
    private const string KernelPluginsGeneratorFullName = "IntelliTect.Coalesce.CodeGeneration.Api.Generators.KernelPlugins";

    [Required]
    public string ProjectFilePath { get; set; } = string.Empty;

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    [Required]
    public string SnapshotPath { get; set; } = string.Empty;

    public string? ConfigPath { get; set; }
    public string? ProjectName { get; set; }
    public string? RootNamespace { get; set; }
    public string? AssemblyName { get; set; }
    public string? Configuration { get; set; }
    public string? TargetFramework { get; set; }
    public string? LangVersion { get; set; }
    public string? Nullable { get; set; }
    public string? DefineConstants { get; set; }
    public string? OutputType { get; set; }
    public string? Platform { get; set; }
    public string? TargetPath { get; set; }
    public string? TargetDirectory { get; set; }
    public ITaskItem[] CompileItems { get; set; } = [];
    public ITaskItem[] ResolvedReferences { get; set; } = [];
    public ITaskItem[] ProjectReferences { get; set; } = [];
    public ITaskItem[] EmbeddedItems { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            ExecuteAsync().GetAwaiter().GetResult();
            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true, showDetail: true, file: null);
            return false;
        }
    }

    private async System.Threading.Tasks.Task ExecuteAsync()
    {
        var resolvedProjectFilePath = Path.GetFullPath(ProjectFilePath);
        var resolvedProjectDirectory = Path.GetFullPath(ProjectDirectory);
        var resolvedSnapshotPath = Path.GetFullPath(SnapshotPath);
        var resolvedConfigPath = ResolveConfigPath(ConfigPath, resolvedProjectDirectory);

        if (resolvedConfigPath is null)
        {
            DeleteSnapshot(resolvedSnapshotPath);
            return;
        }

        var sourceGenerationConfig = SourceGenerationConfig.Load(resolvedConfigPath);
        if (!sourceGenerationConfig.ShouldGenerateAnyCategory)
        {
            DeleteSnapshot(resolvedSnapshotPath);
            return;
        }

        if (!PathsEqual(sourceGenerationConfig.WebProjectPath, resolvedProjectFilePath))
        {
            DeleteSnapshot(resolvedSnapshotPath);
            return;
        }

        if (IsSnapshotCurrent(resolvedSnapshotPath, resolvedConfigPath, sourceGenerationConfig))
        {
            return;
        }

        var configuration = LoadConfiguration(resolvedConfigPath);
        ApplyBuildSettings(configuration, resolvedConfigPath, resolvedProjectFilePath);

        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        var roslynLogger = loggerFactory.CreateLogger<RoslynProjectContextFactory>();

        var dataProjectContext = (RoslynProjectContext)new RoslynProjectContextFactory(roslynLogger)
            .CreateContext(configuration.DataProject, restore: false);
        var webProjectContext = CreateCurrentProjectContext(configuration.WebProject, resolvedProjectFilePath);

        var outputs = await CaptureGeneratedCSharpOutputsAsync(
            configuration,
            dataProjectContext,
            webProjectContext,
            sourceGenerationConfig);

        WriteSnapshotIfChanged(resolvedSnapshotPath, outputs);
    }

    private async System.Threading.Tasks.Task<Dictionary<string, string>> CaptureGeneratedCSharpOutputsAsync(
        CoalesceConfiguration configuration,
        RoslynProjectContext dataProjectContext,
        RoslynProjectContext webProjectContext,
        SourceGenerationConfig sourceGenerationConfig)
    {
        string tempOutputDirectory = Path.Combine(Path.GetTempPath(), $"coalesce-sourcegen-{Guid.NewGuid():N}");
        var originalTargetDirectory = configuration.Output.TargetDirectory;
        var originalDryRun = configuration.DryRun;

        try
        {
            Directory.CreateDirectory(tempOutputDirectory);
            configuration.Output.TargetDirectory = tempOutputDirectory;
            configuration.DryRun = false;

            EnsureGeneratorDisabled(configuration, disabled: true, ScriptsGeneratorName, ScriptsGeneratorFullName);
            EnsureGeneratorDisabled(configuration, disabled: true, KernelPluginsGeneratorName, KernelPluginsGeneratorFullName);
            EnsureGeneratorDisabled(configuration, disabled: !sourceGenerationConfig.GenerateModels, ModelsGeneratorName, ModelsGeneratorFullName);
            EnsureGeneratorDisabled(configuration, disabled: !sourceGenerationConfig.GenerateControllers, ControllersGeneratorName, ControllersGeneratorFullName);

            var executor = new GenerationExecutor(
                configuration,
                LogLevel.Warning,
                dataProjectOverride: dataProjectContext,
                webProjectOverride: webProjectContext,
                throwOnFailure: true);

            await executor.GenerateAsync(typeof(VueSuite));

            return CaptureGeneratedCSharpOutputs(
                tempOutputDirectory,
                sourceGenerationConfig.GenerateModels,
                sourceGenerationConfig.GenerateControllers);
        }
        finally
        {
            configuration.Output.TargetDirectory = originalTargetDirectory;
            configuration.DryRun = originalDryRun;

            try
            {
                if (Directory.Exists(tempOutputDirectory))
                {
                    Directory.Delete(tempOutputDirectory, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private void ApplyBuildSettings(CoalesceConfiguration configuration, string configPath, string resolvedProjectFilePath)
    {
        var configDirectory = Path.GetDirectoryName(configPath)!;

        configuration.WebProject.ProjectFile = resolvedProjectFilePath;
        configuration.WebProject.RootNamespace ??= RootNamespace;
        configuration.WebProject.Configuration = string.IsNullOrWhiteSpace(Configuration)
            ? configuration.WebProject.Configuration
            : Configuration!;
        configuration.WebProject.Framework = string.IsNullOrWhiteSpace(TargetFramework)
            ? configuration.WebProject.Framework
            : TargetFramework!;

        configuration.DataProject.ProjectFile = ResolveProjectPath(configDirectory, configuration.DataProject.ProjectFile);
        configuration.DataProject.Configuration = string.IsNullOrWhiteSpace(Configuration)
            ? configuration.DataProject.Configuration
            : Configuration!;
        configuration.DataProject.Framework = string.IsNullOrWhiteSpace(TargetFramework)
            ? configuration.DataProject.Framework
            : TargetFramework!;
    }

    private RoslynProjectContext CreateCurrentProjectContext(ProjectConfiguration projectConfiguration, string resolvedProjectFilePath)
    {
        var msBuildProjectContext = new MsBuildProjectContext
        {
            DependenciesDesignTime = [],
            CompilationItems = GetExistingFullPaths(CompileItems),
            ProjectReferences = GetExistingFullPaths(ProjectReferences),
            ResolvedReferences = GetExistingFullPaths(ResolvedReferences),
            EmbededItems = GetExistingFullPaths(EmbeddedItems),
            ProjectName = ProjectName ?? Path.GetFileNameWithoutExtension(resolvedProjectFilePath),
            ProjectFullPath = resolvedProjectFilePath,
            AssemblyFullPath = string.IsNullOrWhiteSpace(TargetPath)
                ? Path.ChangeExtension(resolvedProjectFilePath, ".dll")
                : Path.GetFullPath(TargetPath),
            OutputType = OutputType ?? "Library",
            Platform = Platform ?? string.Empty,
            RootNamespace = RootNamespace ?? projectConfiguration.RootNamespace ?? Path.GetFileNameWithoutExtension(resolvedProjectFilePath),
            TargetDirectory = string.IsNullOrWhiteSpace(TargetDirectory)
                ? Path.GetDirectoryName(string.IsNullOrWhiteSpace(TargetPath)
                    ? resolvedProjectFilePath
                    : Path.GetFullPath(TargetPath)) ?? Path.GetDirectoryName(resolvedProjectFilePath)!
                : Path.GetFullPath(TargetDirectory),
            DepsFile = string.IsNullOrWhiteSpace(TargetPath) ? string.Empty : Path.GetFileNameWithoutExtension(TargetPath) + ".deps.json",
            RuntimeConfig = string.IsNullOrWhiteSpace(TargetPath) ? string.Empty : Path.GetFileNameWithoutExtension(TargetPath) + ".runtimeconfig.json",
            Configuration = Configuration ?? projectConfiguration.Configuration,
            TargetFramework = TargetFramework ?? projectConfiguration.Framework ?? string.Empty,
            LangVersion = LangVersion ?? LanguageVersion.Preview.ToDisplayString(),
            Nullable = Nullable ?? string.Empty,
            DefineConstants = DefineConstants ?? string.Empty,
        };

        return RoslynProjectContextFactory.CreateContext(projectConfiguration, msBuildProjectContext);
    }

    private bool IsSnapshotCurrent(string snapshotPath, string configPath, SourceGenerationConfig sourceGenerationConfig)
    {
        if (!File.Exists(snapshotPath))
        {
            return false;
        }

        var snapshotTimestamp = File.GetLastWriteTimeUtc(snapshotPath);

        foreach (var inputPath in EnumerateSnapshotInputs(configPath, sourceGenerationConfig))
        {
            if (File.Exists(inputPath) && File.GetLastWriteTimeUtc(inputPath) > snapshotTimestamp)
            {
                return false;
            }
        }

        return true;
    }

    private IEnumerable<string> EnumerateSnapshotInputs(string configPath, SourceGenerationConfig sourceGenerationConfig)
    {
        yield return configPath;
        yield return Path.GetFullPath(ProjectFilePath);
        yield return sourceGenerationConfig.WebProjectPath;
        yield return sourceGenerationConfig.DataProjectPath;
        yield return typeof(ExportCoalesceGeneratedCSharpTask).Assembly.Location;
        yield return typeof(GenerationExecutor).Assembly.Location;
        yield return typeof(VueSuite).Assembly.Location;

        foreach (var path in GetExistingFullPaths(CompileItems))
        {
            yield return path;
        }

        foreach (var path in GetExistingFullPaths(ResolvedReferences))
        {
            yield return path;
        }

        foreach (var path in GetExistingFullPaths(ProjectReferences))
        {
            yield return path;
        }
    }

    private void WriteSnapshotIfChanged(string snapshotPath, IReadOnlyDictionary<string, string> outputs)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(outputs);
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);

        if (File.Exists(snapshotPath) && string.Equals(File.ReadAllText(snapshotPath), json, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(snapshotPath, json);
    }

    private static Dictionary<string, string> CaptureGeneratedCSharpOutputs(
        string tempOutputDirectory,
        bool includeModels,
        bool includeControllers)
    {
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);

        if (includeModels)
        {
            CaptureDirectory(Path.Combine("Models", "Generated"));
        }

        if (includeControllers)
        {
            CaptureDirectory(Path.Combine("Api", "Generated"));
        }

        return outputs;

        void CaptureDirectory(string relativeDirectory)
        {
            var fullDirectory = Path.Combine(tempOutputDirectory, relativeDirectory);
            if (!Directory.Exists(fullDirectory))
            {
                return;
            }

            foreach (var filePath in Directory.GetFiles(fullDirectory, "*.g.cs", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relativePath = Path.GetRelativePath(tempOutputDirectory, filePath).Replace('\\', '/');
                outputs[relativePath] = File.ReadAllText(filePath);
            }
        }
    }

    private static void EnsureGeneratorDisabled(CoalesceConfiguration config, bool disabled, params string[] generatorNames)
    {
        foreach (var generatorName in generatorNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            if (!config.GeneratorConfig.TryGetValue(generatorName, out var generatorConfig))
            {
                generatorConfig = new JObject();
                config.GeneratorConfig[generatorName] = generatorConfig;
            }

            generatorConfig[Generator.DisabledJsonPropertyName] = disabled;
        }
    }

    private static string? ResolveConfigPath(string? explicitConfigPath, string projectDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            var resolvedExplicitPath = Path.GetFullPath(explicitConfigPath);
            return File.Exists(resolvedExplicitPath) ? resolvedExplicitPath : null;
        }

        var directory = new DirectoryInfo(projectDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ConfigFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static CoalesceConfiguration LoadConfiguration(string configPath)
        => JsonConvert.DeserializeObject<CoalesceConfiguration>(File.ReadAllText(configPath))
            ?? throw new InvalidOperationException($"Unable to read '{configPath}'.");

    private static string ResolveProjectPath(string configDirectory, string projectFilePath)
        => Path.GetFullPath(Path.Combine(configDirectory, projectFilePath));

    private static string[] GetExistingFullPaths(IEnumerable<ITaskItem> items)
        => items
            .Select(item => item.ItemSpec)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void DeleteSnapshot(string snapshotPath)
    {
        try
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private sealed class SourceGenerationConfig
    {
        public required string WebProjectPath { get; init; }
        public required string DataProjectPath { get; init; }
        public required bool GenerateModels { get; init; }
        public required bool GenerateControllers { get; init; }

        public bool ShouldGenerateAnyCategory => GenerateModels || GenerateControllers;

        public static SourceGenerationConfig Load(string configPath)
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (!root.TryGetProperty("webProject", out var webProject) || !webProject.TryGetProperty("projectFile", out var webProjectFileProperty))
            {
                throw new InvalidOperationException($"'{ConfigFileName}' does not contain a webProject.projectFile entry.");
            }

            if (!root.TryGetProperty("dataProject", out var dataProject) || !dataProject.TryGetProperty("projectFile", out var dataProjectFileProperty))
            {
                throw new InvalidOperationException($"'{ConfigFileName}' does not contain a dataProject.projectFile entry.");
            }

            var configDirectory = Path.GetDirectoryName(configPath)!;
            return new SourceGenerationConfig
            {
                WebProjectPath = ResolveProjectPath(configDirectory, webProjectFileProperty.GetString() ?? string.Empty),
                DataProjectPath = ResolveProjectPath(configDirectory, dataProjectFileProperty.GetString() ?? string.Empty),
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

                if (generatorSettings.ValueKind == JsonValueKind.Object
                    && generatorSettings.TryGetProperty("disabled", out var disabledProperty)
                    && disabledProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return disabledProperty.GetBoolean();
                }
            }

            return false;
        }
    }
}
