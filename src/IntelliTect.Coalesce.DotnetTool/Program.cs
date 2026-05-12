#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelliTect.Coalesce.CodeGeneration.Analysis;
using IntelliTect.Coalesce.CodeGeneration.Configuration;
using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.CodeGeneration.Utilities;
using IntelliTect.Coalesce.TypeDefinition;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable UnassignedGetOnlyAutoProperty

namespace IntelliTect.Coalesce.Cli;

[HelpOption]
public class Program
{
    private const string ModelsGeneratorName = "Models";
    private const string ModelsGeneratorFullName = "IntelliTect.Coalesce.CodeGeneration.Api.Generators.Models";
    private const string ControllersGeneratorName = "Controllers";
    private const string ControllersGeneratorFullName = "IntelliTect.Coalesce.CodeGeneration.Api.Generators.Controllers";
    private const string ScriptsGeneratorName = "Scripts";
    private const string ScriptsGeneratorFullName = "IntelliTect.Coalesce.CodeGeneration.Vue.Generators.Scripts";
    private const string KernelPluginsGeneratorName = "KernelPlugins";
    private const string KernelPluginsGeneratorFullName = "IntelliTect.Coalesce.CodeGeneration.Api.Generators.KernelPlugins";

    [Option(CommandOptionType.NoValue,
        Description = "Wait for a debugger to be attached before starting generation", LongName = "debug",
        ShortName = "d")]
    public bool Debug { get; }

    [Option(CommandOptionType.NoValue,
        Description = "Do not write output files to disk.", LongName = "what-if", ShortName = "WhatIf")]
    public bool DryRun { get; }

    [Option(CommandOptionType.NoValue,
        Description = "Verify that no output changes have been made. Use in CI builds to ensure that codegen has not been forgotten.", LongName = "verify", ShortName = "")]
    public bool Verify { get; }

    [Option(CommandOptionType.SingleValue,
        Description = "Write Coalesce-generated C# Models/Generated and Api/Generated outputs as a JSON map to the specified file path. Intended for the Coalesce source generator.",
        LongName = "emit-csharp-sourcegen")]
    public string? EmitCSharpSourceGenOutput { get; }

    [Argument(0, "config", Description =
        "Path to a coalesce.json configuration file that will drive generation.  If not specified, it will search in current folder.")]
    public string? ConfigFile { get; }

    [Option(CommandOptionType.SingleValue, ShortName = "v", LongName = "verbosity",
        Description = "Output verbosity. Options are Trace, Debug, Information, Warning, Error, Critical, None.")]
    // TODO: Change this type to be the Enum once Nate McMaster ships v2.2.0 of his library.
    public string? LogLevelOption { get; }

    private static Task<int> Main(string[] args)
    {
        ApplicationTimer.Stopwatch.Start();
        return CommandLineApplication.ExecuteAsync<Program>(args);
    }

    private async Task<int> OnExecuteAsync(CommandLineApplication app)
    {
#if DEBUG
        if (Debug) WaitForDebugger();
#endif

        // Get the target framework (e.g. ".NETCoreApp,Version=v2.0") that Coalesce was compiled against.
        // I added this originally for debugging, but its kinda nice to show regardless.
        var frameworkVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        // This reflects the version of the nuget package.
        string version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion ?? "unknown";

        if (!Enum.TryParse(LogLevelOption, true, out LogLevel logLevel)) logLevel = LogLevel.Information;

        if (logLevel <= LogLevel.Information)
        {
            Console.WriteLine($"Starting Coalesce {version}, running under {frameworkVersion}");
            Console.WriteLine("https://github.com/IntelliTect/Coalesce");
            Console.WriteLine();
        }

        FileInfo configFile = LocateConfigFile(ConfigFile);
        CoalesceConfiguration config = LoadConfiguration(configFile.FullName);
        config.DryRun = DryRun;

        // Must go AFTER we load in the config file, since if the config file was a relative path, changing this ruins that.
        Directory.SetCurrentDirectory(configFile.DirectoryName!);

        if (logLevel <= LogLevel.Information)
        {
            Console.WriteLine($"Working in '{Directory.GetCurrentDirectory()}', using '{Path.GetFileName(configFile.FullName)}'");
        }

        var rootGenerator = ResolveRootGenerator(config.RootGenerator);
        if (rootGenerator == null)
        {
            Console.Error.WriteLine($"Couldn't find a root generator that matches {config.RootGenerator ?? "Vue"}");
            Console.Error.WriteLine($"Valid root generators are: {string.Join(",", GetRootGenerators().Select(g => g.FullName))}");
            return -1;
        }

        if (!string.IsNullOrWhiteSpace(EmitCSharpSourceGenOutput))
        {
            return await EmitCSharpSourceGenOutputsAsync(config, logLevel, rootGenerator);
        }

        var executor = new GenerationExecutor(config, logLevel);
        try
        {
            await executor.GenerateAsync(rootGenerator);
        }
        catch (CoalesceModelException e)
        {
            // Only write the message here, not a full exception.ToString() w/ stack trace
            executor.Logger.LogError(e.Message);
            return -1;
        }
        catch (ProjectAnalysisException e)
        {
            // Only write the message here, not a full exception.ToString() w/ stack trace
            executor.Logger.LogError(e.Message);
            return -1;
        }
        catch (Exception e)
        {
            executor.Logger.LogError(e.ToString());
            return -1;
        }

        if (Verify && executor.GenerationContext.ActionsPerformedCount > 0)
        {
            executor.Logger.LogError("Output has uncommitted changes. Run `dotnet coalesce` and commit the changes.");
            return -1;
        }

        return 0;
    }

    private static CoalesceConfiguration LoadConfiguration(string configFilePath)
    {
        using var reader = new StreamReader(configFilePath);
        using var jsonReader = new JsonTextReader(reader);
        var serializer = new JsonSerializer();
        return serializer.Deserialize<CoalesceConfiguration>(jsonReader)!;
    }

    private static Type? ResolveRootGenerator(string? rootGeneratorName)
    {
        var effectiveRootGeneratorName = rootGeneratorName ?? "Vue";
        var rootGenerators = GetRootGenerators();

        return rootGenerators.FirstOrDefault(t => t.FullName == effectiveRootGeneratorName)
            ?? rootGenerators.FirstOrDefault(t => t.Name == effectiveRootGeneratorName)
            ?? rootGenerators.SingleOrDefault(t => t.FullName!.Contains(effectiveRootGeneratorName, StringComparison.Ordinal));
    }

    private static Type[] GetRootGenerators()
        =>
        [
            typeof(CodeGeneration.Vue.Generators.VueSuite),
        ];

    private async Task<int> EmitCSharpSourceGenOutputsAsync(CoalesceConfiguration config, LogLevel logLevel, Type rootGenerator)
    {
        var originalTargetDirectory = config.Output.TargetDirectory;
        var originalDryRun = config.DryRun;
        string tempOutputDirectory = Path.Combine(Path.GetTempPath(), $"coalesce-sourcegen-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempOutputDirectory);
            config.Output.TargetDirectory = tempOutputDirectory;
            config.DryRun = false;

            EnsureGeneratorDisabled(config, disabled: true, ScriptsGeneratorName, ScriptsGeneratorFullName);
            EnsureGeneratorDisabled(config, disabled: true, KernelPluginsGeneratorName, KernelPluginsGeneratorFullName);
            EnsureGeneratorDisabled(config, disabled: false, ModelsGeneratorName, ModelsGeneratorFullName);
            EnsureGeneratorDisabled(config, disabled: false, ControllersGeneratorName, ControllersGeneratorFullName);

            var executor = new GenerationExecutor(config, logLevel);
            try
            {
                await executor.GenerateAsync(rootGenerator);
            }
            catch (CoalesceModelException e)
            {
                executor.Logger.LogError(e.Message);
                return -1;
            }
            catch (ProjectAnalysisException e)
            {
                executor.Logger.LogError(e.Message);
                return -1;
            }
            catch (Exception e)
            {
                executor.Logger.LogError(e.ToString());
                return -1;
            }

            var outputs = CaptureGeneratedCSharpOutputs(tempOutputDirectory);
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(EmitCSharpSourceGenOutput!));
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await File.WriteAllTextAsync(
                Path.GetFullPath(EmitCSharpSourceGenOutput!),
                JsonConvert.SerializeObject(outputs, Formatting.None));

            return 0;
        }
        finally
        {
            config.Output.TargetDirectory = originalTargetDirectory;
            config.DryRun = originalDryRun;

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

    private static Dictionary<string, string> CaptureGeneratedCSharpOutputs(string tempOutputDirectory)
    {
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var relativeDirectory in new[]
                 {
                     Path.Combine("Models", "Generated"),
                     Path.Combine("Api", "Generated"),
                 })
        {
            var fullDirectory = Path.Combine(tempOutputDirectory, relativeDirectory);
            if (!Directory.Exists(fullDirectory))
            {
                continue;
            }

            foreach (var filePath in Directory.GetFiles(fullDirectory, "*.g.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(tempOutputDirectory, filePath).Replace('\\', '/');
                outputs[relativePath] = File.ReadAllText(filePath);
            }
        }

        return outputs;
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

    private static void WaitForDebugger()
    {
        Console.WriteLine($"Attach a debugger to processID: {Process.GetCurrentProcess().Id}. Waiting...");
        var waitStep = 10;
        for (var i = 60; i > 0; i -= waitStep)
        {
            if (Debugger.IsAttached)
            {
                Console.WriteLine("Debugger attached.");
                break;
            }

            Console.WriteLine($"Waiting {i}...");
            Thread.Sleep(1000 * waitStep);
        }
    }

    private static FileInfo LocateConfigFile(string? explicitLocation)
    {
        FileInfo? file = null;
        if (!string.IsNullOrWhiteSpace(explicitLocation))
        {
            file = new FileInfo(explicitLocation);
            if (!file.Exists)
                throw new FileNotFoundException("Couldn't find Coalesce configuration file",
                    file.FullName);
            return file;
        }

        const string configFileName = "coalesce.json";
        file = new DirectoryInfo(Directory.GetCurrentDirectory())
            .FindFileInAncestorDirectory(configFileName);

        if (file != null) return file;

        throw new FileNotFoundException("Couldn't locate a coalesce.json configuration file");
    }
}
