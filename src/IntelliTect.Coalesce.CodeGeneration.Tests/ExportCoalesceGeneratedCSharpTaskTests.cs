using System.Reflection;
using IntelliTect.Coalesce.CodeGeneration.Vue.Tasks;

namespace IntelliTect.Coalesce.CodeGeneration.Tests;

public class ExportCoalesceGeneratedCSharpTaskTests
{
    [Test]
    public async Task SourceGenerationConfig_Load_GeneratesModelsAndControllersForCSharpSourceGen()
    {
        var configPath = await WriteConfigAsync(
            """
            {
              "webProject": { "projectFile": "Web/Web.csproj" },
              "dataProject": { "projectFile": "Data/Data.csproj" },
              "generatorConfig": {
                "Models": { "disabled": false },
                "Controllers": { "disabled": false }
              }
            }
            """);

        var config = LoadSourceGenerationConfig(configPath);

        await Assert.That((bool)GetProperty(config, "GenerateModels")).IsTrue();
        await Assert.That((bool)GetProperty(config, "GenerateControllers")).IsTrue();
        await Assert.That((bool)GetProperty(config, "ShouldGenerateAnyCategory")).IsTrue();
    }

    [Test]
    public async Task SourceGenerationConfig_Load_IgnoresDisabledFlagsForCSharpSourceGen()
    {
        var configPath = await WriteConfigAsync(
            """
            {
              "webProject": { "projectFile": "Web/Web.csproj" },
              "dataProject": { "projectFile": "Data/Data.csproj" },
              "generatorConfig": {
                "Models": { "disabled": true },
                "Controllers": { "disabled": true }
              }
            }
            """);

        var config = LoadSourceGenerationConfig(configPath);

        await Assert.That((bool)GetProperty(config, "GenerateModels")).IsTrue();
        await Assert.That((bool)GetProperty(config, "GenerateControllers")).IsTrue();
        await Assert.That((bool)GetProperty(config, "ShouldGenerateAnyCategory")).IsTrue();
    }

    private static object LoadSourceGenerationConfig(string configPath)
    {
        var nestedType = typeof(ExportCoalesceGeneratedCSharpTask)
            .GetNestedType("SourceGenerationConfig", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find SourceGenerationConfig nested type.");

        var loadMethod = nestedType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find SourceGenerationConfig.Load.");

        return loadMethod.Invoke(null, [configPath])
            ?? throw new InvalidOperationException("SourceGenerationConfig.Load returned null.");
    }

    private static object GetProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(instance)
            ?? throw new InvalidOperationException($"Could not read property '{propertyName}'.");
    }

    private static async Task<string> WriteConfigAsync(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Coalesce.ExportTaskTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "coalesce.json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }
}
