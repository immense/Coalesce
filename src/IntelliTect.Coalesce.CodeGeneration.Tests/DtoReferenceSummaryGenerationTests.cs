using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.CodeGeneration.Vue.Generators;
using IntelliTect.Coalesce.Testing;
using IntelliTect.Coalesce.Testing.Util;
using IntelliTect.Coalesce.TypeDefinition;

namespace IntelliTect.Coalesce.CodeGeneration.Tests;

public class DtoReferenceSummaryGenerationTests : CodeGenTestBase
{
    [Test]
    public async Task ApiDtos_GenerateReferenceSummaryResponseProperties()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "Coalesce.DtoReferenceSummaryTests", Guid.NewGuid().ToString("N"), "Api");
        var suite = BuildExecutor()
            .CreateRootGenerator<ApiOnlySuite>()
            .WithModel(ReflectionRepositoryFactory.Symbol)
            .WithOutputPath(outDir);

        await suite.GenerateAsync();

        var caseDtoFile = Directory.GetFiles(outDir, "CaseDto.g.cs", SearchOption.AllDirectories).Single();
        var personDtoFile = Directory.GetFiles(outDir, "PersonDto.g.cs", SearchOption.AllDirectories).Single();

        var caseContents = await File.ReadAllTextAsync(caseDtoFile);
        var personContents = await File.ReadAllTextAsync(personDtoFile);

        await Assert.That(caseContents.Contains("PersonSummaryResponse AssignedTo { get; set; }")).IsTrue();
        await Assert.That(caseContents.Contains("MapToDto<")).IsTrue();
        await Assert.That(caseContents.Contains("PersonSummaryResponse>(context, tree?[nameof(this.AssignedTo)])")).IsTrue();
        await Assert.That(personContents.Contains("public partial class PersonSummaryResponse")).IsTrue();
        await Assert.That(personContents.Contains("public int? PersonId { get; set; }")).IsTrue();
        await Assert.That(personContents.Contains("public string Name { get; set; }")).IsTrue();
        await Assert.That(personContents.Contains("public string CompanyName { get; set; }")).IsTrue();
        await Assert.That(personContents.Contains("this.CompanyName = obj.Company?.Name;")).IsTrue();

        await AssertSuiteCSharpOutputCompiles(suite);
    }

    [Test]
    public async Task VueOutput_GeneratesReferenceSummaryModels()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "Coalesce.DtoReferenceSummaryTests", Guid.NewGuid().ToString("N"), "Vue");
        var suite = BuildExecutor()
            .CreateRootGenerator<VueSuite>()
            .WithModel(ReflectionRepositoryFactory.Symbol)
            .WithOutputPath(outDir);

        await suite.GenerateAsync();

        var models = await File.ReadAllTextAsync(Path.Combine(outDir, "src", "models.g.ts"));
        var metadata = await File.ReadAllTextAsync(Path.Combine(outDir, "src", "metadata.g.ts"));
        var viewmodels = await File.ReadAllTextAsync(Path.Combine(outDir, "src", "viewmodels.g.ts"));

        await Assert.That(models.Contains("export interface PersonSummary extends Model<typeof metadata.PersonSummary>")).IsTrue();
        await Assert.That(models.Contains("companyName: string | null")).IsTrue();
        await Assert.That(models.Contains("assignedTo: PersonSummary | null")).IsTrue();
        await Assert.That(metadata.Contains("export const PersonSummary = domain.types.PersonSummary =")).IsTrue();
        await Assert.That(metadata.Contains("get typeDef() { return (domain.types.PersonSummary as ObjectType & { name: \"PersonSummary\" }) },")).IsTrue();
        await Assert.That(viewmodels.Contains("assignedTo: $models.PersonSummary | null;")).IsTrue();
    }
}
