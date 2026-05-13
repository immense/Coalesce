using IntelliTect.Coalesce.CodeGeneration.Api.Generators;
using IntelliTect.Coalesce.CodeGeneration.Configuration;
using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.Testing.Util;
using IntelliTect.Coalesce.TypeDefinition;
using IntelliTect.Coalesce.Validation;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Assembly = System.Reflection.Assembly;

namespace IntelliTect.Coalesce.Testing;

public class CodeGenTestBase
{
    public static Lazy<Assembly> WebAssembly { get; } = new(GetWebAssembly);

    private static Assembly GetWebAssembly()
    {
        var suite = new GenerationExecutor(
                new() { WebProject = new() { RootNamespace = "MyProject" } },
                Microsoft.Extensions.Logging.LogLevel.Information
            )
            .CreateRootGenerator<ApiOnlySuite>()
            .WithModel(ReflectionRepositoryFactory.Symbol)
            .WithOutputPath(".");

        var compilation = GetCSharpCompilation(suite).Result;

        using var ms = new MemoryStream();
        EmitResult emitResult = compilation.Emit(ms);

        if (!emitResult.Success) throw new Exception("Web project compilation failed: " + string.Join("\n\n", emitResult.Diagnostics));

        var assembly = Assembly.Load(ms.ToArray());
        ReflectionRepository.Global.AddAssembly(assembly);
        return assembly;
    }

    public class ApiOnlySuite : CompositeGenerator<ReflectionRepository>, IRootGenerator
    {
        public ApiOnlySuite(CompositeGeneratorServices services) : base(services) { }

        public override IEnumerable<IGenerator> GetGenerators()
        {
            yield return Generator<IntelliTect.Coalesce.CodeGeneration.Api.Generators.Models>()
                .WithModel(Model)
                .AppendOutputPath("Models");

            yield return Generator<Controllers>()
                .WithModel(Model);
        }
    }

    protected GenerationExecutor BuildExecutor()
    {
        return new GenerationExecutor(
            new CoalesceConfiguration
            {
                WebProject = new ProjectConfiguration
                {
                    RootNamespace = "MyProject"
                }
            },
            Microsoft.Extensions.Logging.LogLevel.Information
        );
    }

    protected Task AssertSuiteCSharpOutputCompiles(IRootGenerator suite)
        => GetCSharpCompilation(suite, assertSuccess: true);

    public static async Task<CSharpCompilation> GetCSharpCompilation(IRootGenerator suite, bool assertSuccess = true)
    {
        var generators = suite
            .GetGeneratorsFlattened()
            .OfType<IFileGenerator>()
            .Where(g => g.EffectiveOutputPath.EndsWith(".cs"))
            .ToList();

        var tasks = generators.Select(gen => (Generator: gen, Output: gen.GetOutputAsync()));
        await Task.WhenAll(tasks.Select(t => t.Output));

        var generatedFiles = tasks
            .Select((task) => CSharpSyntaxTree.ParseText(
                SourceText.From(new StreamReader(task.Output.Result).ReadToEnd()),
                path: task.Generator.EffectiveOutputPath
            ))
            .ToArray();

        return ReflectionRepositoryFactory.GetCompilation(generatedFiles, assertSuccess);
    }

    private static ProcessStartInfo GetShellExecStartInfo(string program, IEnumerable<string> args, string workingDirectory = null)
    {
        var arguments = args.ToList();
        var exeToRun = ResolveExecutablePath(program);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, the node executable is a .cmd file, so it can't be executed
            // directly (except with UseShellExecute=true, but that's no good, because
            // it prevents capturing stdio). So we need to invoke it via "cmd /c".
            exeToRun = "cmd";
            arguments.Insert(0, program);
            arguments.Insert(0, "/c");
        }

        var start = new ProcessStartInfo(exeToRun)
        {
            WorkingDirectory = workingDirectory,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.Environment["PATH"] = string.Join(
            Path.PathSeparator,
            new[]
            {
                Path.GetDirectoryName(ResolveExecutablePath("node")),
                Path.GetDirectoryName(ResolveExecutablePath("npm")),
                "/opt/homebrew/bin",
                "/usr/local/bin",
                "/usr/bin",
                start.Environment.TryGetValue("PATH", out var existingPath) ? existingPath : Environment.GetEnvironmentVariable("PATH"),
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct()
        );
        foreach (var arg in arguments) start.ArgumentList.Add(arg);

        return start;
    }

    private static string ResolveExecutablePath(string program)
    {
        if (Path.IsPathRooted(program) || program.Contains(Path.DirectorySeparatorChar) || program.Contains(Path.AltDirectorySeparatorChar))
        {
            return program;
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var candidate in pathEntries
            .Concat(new[]
            {
                "/opt/homebrew/bin",
                "/usr/local/bin",
                "/usr/bin",
            })
            .Distinct())
        {
            var fullPath = Path.Combine(candidate, program);
            if (System.IO.File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return program;
    }

    protected static async Task AssertTypescriptProjectCompiles(
        string tsConfigPath,
        string workingDirectory,
        string tsVersion
    )
    {
        var targetFramework = Path.GetFileName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var tsPath = Path.Combine(Path.GetTempPath(), "coalesce-ts-tests", targetFramework, "ts" + tsVersion);
        if (Directory.Exists(tsPath)) Directory.Delete(tsPath, recursive: true);
        Directory.CreateDirectory(tsPath);

        using var npmInstall = Process.Start(GetShellExecStartInfo("npm", new[] { "i", "typescript@" + tsVersion, "--prefix", tsPath }, tsPath))
            ?? throw new InvalidOperationException("Failed to start npm.");
        await npmInstall.WaitForExitAsync();
        await Assert.That(npmInstall.ExitCode).IsEqualTo(0);

        var effectiveWorkingDirectory = Directory.Exists(workingDirectory)
            ? workingDirectory
            : Path.GetDirectoryName(tsConfigPath) ?? tsPath;
        Directory.CreateDirectory(effectiveWorkingDirectory);

        var start = GetShellExecStartInfo(
            ResolveExecutablePath("node"),
            new List<string>
            {
                Path.Combine(tsPath, "node_modules", "typescript", "bin", "tsc"),
                "--project",
                tsConfigPath,
                "--noEmit"
            },
            effectiveWorkingDirectory
        );
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;

        var typescriptProcess = Process.Start(start);

        // Collect stdout and stderr so we can report it in the event of a failure.
        var streamTasks = new[]
        {
            typescriptProcess.StandardOutput,
            typescriptProcess.StandardError,
        }.Select(async stream =>
        {
            var sb = new StringBuilder();
            while (!typescriptProcess.HasExited)
            {
                var content = stream.ReadToEnd();
                if (content != "") sb.Append(content);
                await Task.Delay(10);
            }
            return sb.ToString();
        }).ToArray();

        await typescriptProcess.WaitForExitAsync();
        var streams = await Task.WhenAll(streamTasks);
        await Assert.That(typescriptProcess.ExitCode).IsEqualTo(0)
            .Because(string.Join("\n\n", streams));
    }

    public static DirectoryInfo GetRepoRoot([CallerFilePath] string sourceFilePath = null)
    {
        DirectoryInfo FindRepoRoot(DirectoryInfo start)
        {
            if (start is null || !start.Exists) return null;

            return start.FindFileInAncestorDirectory("Coalesce.slnx")?.Directory
                ?? start.FindDirectoryInAncestorDirectory("b");
        }

        var candidateRoots = new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(CodeGenTestBase).Assembly.Location),
            Directory.GetCurrentDirectory(),
            sourceFilePath is null ? null : Path.GetDirectoryName(sourceFilePath),
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => new DirectoryInfo(path!))
        .DistinctBy(path => path.FullName);

        foreach (var candidateRoot in candidateRoots)
        {
            var repoRoot = FindRepoRoot(candidateRoot);
            if (repoRoot is not null)
            {
                return repoRoot;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Coalesce repo root.");
    }
}
