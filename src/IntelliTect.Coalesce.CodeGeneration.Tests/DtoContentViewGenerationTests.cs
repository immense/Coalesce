using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.Testing;
using IntelliTect.Coalesce.Testing.Util;
using IntelliTect.Coalesce.TypeDefinition;

namespace IntelliTect.Coalesce.CodeGeneration.Tests;

public class DtoContentViewGenerationTests : CodeGenTestBase
{
    [Test]
    public async Task ApiDtos_GenerateActionDefaultsAndViewAwareMappings()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "Coalesce.DtoContentViewTests", Guid.NewGuid().ToString("N"), "Api");
        var suite = BuildExecutor()
            .CreateRootGenerator<ApiOnlySuite>()
            .WithModel(ReflectionRepositoryFactory.Symbol)
            .WithOutputPath(outDir);

        await suite.GenerateAsync();

        var dtoFile = Directory.GetFiles(outDir, "ContentViewEntityDto.g.cs", SearchOption.AllDirectories).Single();
        var controllerFile = Directory.GetFiles(outDir, "ContentViewEntityController.g.cs", SearchOption.AllDirectories).Single();
        var personDtoFile = Directory.GetFiles(outDir, "PersonDto.g.cs", SearchOption.AllDirectories).Single();

        var dtoContents = await File.ReadAllTextAsync(dtoFile);
        var controllerContents = await File.ReadAllTextAsync(controllerFile);
        var personDtoContents = await File.ReadAllTextAsync(personDtoFile);

        await Assert.That(controllerContents.Contains("GetImplementation(id, ApplyDefaultIncludes(parameters, \"detail\"), dataSource)")).IsTrue();
        await Assert.That(controllerContents.Contains("ListImplementation(ApplyDefaultIncludes(parameters, \"list\"), dataSource)")).IsTrue();
        await Assert.That(controllerContents.Contains("SaveImplementation(dto, ApplyDefaultIncludes(parameters, \"save\"), dataSource, behaviors)")).IsTrue();
        await Assert.That(controllerContents.Contains("CountImplementation(ApplyDefaultIncludes(parameters, \"list\"), dataSource)")).IsTrue();

        await Assert.That(dtoContents.Contains("this.ReportedByCompanyName = obj.ReportedBy?.Company?.Name;")).IsTrue();
        await Assert.That(dtoContents.Contains("includes == \"detail\"")).IsTrue();
        await Assert.That(dtoContents.Contains("includes == \"list\"")).IsTrue();
        await Assert.That(dtoContents.Contains("includes == \"save\"")).IsTrue();
        await Assert.That(personDtoContents.Contains("this.CompanyName = obj.Company?.Name;")).IsTrue();
        await Assert.That(personDtoContents.Contains("includes == \"detail\"")).IsTrue();

        await AssertSuiteCSharpOutputCompiles(suite);
    }
}
