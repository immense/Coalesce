#nullable enable

using IntelliTect.Coalesce.CodeGeneration.Analysis.Base;
using IntelliTect.Coalesce.CodeGeneration.Configuration;
using IntelliTect.Coalesce.CodeGeneration.Analysis.MsBuild;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis.CSharp;

namespace IntelliTect.Coalesce.CodeGeneration.Analysis.Roslyn;

public class RoslynProjectContextFactory : IProjectContextFactory
{
    public RoslynProjectContextFactory(ILogger<RoslynProjectContextFactory> logger)
    {
        Logger = logger;
    }

    public ILogger Logger { get; }

    public ProjectContext CreateContext(ProjectConfiguration projectConfig, bool restore = false)
    {
        var tempContext = new RoslynProjectContext(projectConfig);

        var builder = new MsBuildProjectContextBuilder(Logger, tempContext);
        if (restore)
        {
            builder = builder.RestoreProjectPackages();
        }

        var msbContext = builder.BuildProjectContext();
        return CreateContext(projectConfig, msbContext, Logger);
    }

    public static RoslynProjectContext CreateContext(
        ProjectConfiguration projectConfig,
        MsBuildProjectContext msBuildProjectContext,
        ILogger? logger = null)
    {
        var context = new RoslynProjectContext(projectConfig)
        {
            MsBuildProjectContext = msBuildProjectContext,
        };

        if (!LanguageVersionFacts.TryParse(msBuildProjectContext.LangVersion, out var langVersion))
        {
            logger?.LogWarning($"Unknown or unsupported C# Language version '{msBuildProjectContext.LangVersion}' specified by {msBuildProjectContext.ProjectName}. Code generation may malfunction.");
        }
        else
        {
            context.LangVersion = langVersion;
        }

        return context;
    }
}
