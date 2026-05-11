using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.CodeGeneration.Vue.Generators;
using IntelliTect.Coalesce.Testing;
using IntelliTect.Coalesce.Testing.Util;
using IntelliTect.Coalesce.TypeDefinition;

namespace IntelliTect.Coalesce.CodeGeneration.Tests;

public class DtoFlattenGenerationTests : CodeGenTestBase
{
    [Test]
    public async Task ApiDtos_GenerateFlattenedResponseProperties()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "Coalesce.DtoFlattenTests", Guid.NewGuid().ToString("N"), "Api");
        var suite = BuildExecutor()
            .CreateRootGenerator<ApiOnlySuite>()
            .WithModel(ReflectionRepositoryFactory.Symbol)
            .WithOutputPath(outDir);

        await suite.GenerateAsync();

        var caseDtoFile = Directory.GetFiles(outDir, "CaseDto.g.cs", SearchOption.AllDirectories).Single();
        var contents = await File.ReadAllTextAsync(caseDtoFile);

        await Assert.That(contents.Contains("public string AssignedToName { get; set; }")).IsTrue();
        await Assert.That(contents.Contains("public string ReportedByCompanyName { get; set; }")).IsTrue();
        await Assert.That(contents.Contains("this.AssignedToName = obj.AssignedTo?.Name;")).IsTrue();
        await Assert.That(contents.Contains("this.ReportedByCompanyName = obj.ReportedBy?.Company?.Name;")).IsTrue();

        await AssertSuiteCSharpOutputCompiles(suite);
    }

    [Test]
    public async Task VueOutput_GeneratesFlattenedResponseProperties()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "Coalesce.DtoFlattenTests", Guid.NewGuid().ToString("N"), "Vue");
        var suite = BuildExecutor()
            .CreateRootGenerator<VueSuite>()
            .WithModel(ReflectionRepositoryFactory.Symbol)
            .WithOutputPath(outDir);

        await suite.GenerateAsync();

        var models = await File.ReadAllTextAsync(Path.Combine(outDir, "src", "models.g.ts"));
        var metadata = await File.ReadAllTextAsync(Path.Combine(outDir, "src", "metadata.g.ts"));

        await Assert.That(models.Contains("assignedToName: string | null")).IsTrue();
        await Assert.That(models.Contains("reportedByCompanyName: string | null")).IsTrue();
        await Assert.That(metadata.Contains("assignedToName:")).IsTrue();
        await Assert.That(metadata.Contains("reportedByCompanyName:")).IsTrue();
    }
}
