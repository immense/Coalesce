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
        var tagLinkDtoFile = Directory.GetFiles(outDir, "ContentViewEntityTagLinkDto.g.cs", SearchOption.AllDirectories).Single();

        var dtoContents = await File.ReadAllTextAsync(dtoFile);
        var controllerContents = await File.ReadAllTextAsync(controllerFile);
        var personDtoContents = await File.ReadAllTextAsync(personDtoFile);
        var tagLinkDtoContents = await File.ReadAllTextAsync(tagLinkDtoFile);

        await Assert.That(controllerContents.Contains("Task<ItemResult<GetContentViewEntityDetailResponse>> Get(")).IsTrue();
        await Assert.That(controllerContents.Contains("GetImplementation<GetContentViewEntityDetailResponse>(id, ApplyFixedIncludes(parameters, \"detail\"), dataSource)")).IsTrue();
        await Assert.That(controllerContents.Contains("Task<ListResult<GetContentViewEntityListResponse>> List(")).IsTrue();
        await Assert.That(controllerContents.Contains("ListImplementation<GetContentViewEntityListResponse>(ApplyFixedIncludes(parameters, \"list\"), dataSource)")).IsTrue();
        await Assert.That(controllerContents.Contains("Task<ItemResult<GetContentViewEntitySaveResponse>> Save(")).IsTrue();
        await Assert.That(controllerContents.Contains("SaveImplementation<GetContentViewEntitySaveResponse>(dto, ApplyFixedIncludes(parameters, \"save\"), dataSource, behaviors)")).IsTrue();
        await Assert.That(controllerContents.Contains("CountImplementation(ApplyFixedIncludes(parameters, \"list\"), dataSource)")).IsTrue();

        await Assert.That(dtoContents.Contains("public partial class GetContentViewEntityResponse")).IsTrue();
        await Assert.That(dtoContents.Contains("public partial class GetContentViewEntityListResponse")).IsTrue();
        await Assert.That(dtoContents.Contains("public partial class GetContentViewEntityDetailResponse")).IsTrue();
        await Assert.That(dtoContents.Contains("public partial class GetContentViewEntitySaveResponse")).IsTrue();
        await Assert.That(dtoContents.Contains("public System.DateTime? CreatedAtUTC { get; set; }")).IsTrue();
        await Assert.That(dtoContents.Contains("this.CreatedAtUTC = obj.CreatedAt.ToUniversalTime();")).IsTrue();
        await Assert.That(dtoContents.Contains("ContentViewEntityTagLinkDetailResponse> Tags { get; set; }")).IsTrue();
        await Assert.That(tagLinkDtoContents.Contains("public partial class ContentViewEntityTagLinkDetailResponse")).IsTrue();
        await Assert.That(tagLinkDtoContents.Contains("public partial class ContentViewEntityTagLinkListResponse")).IsTrue();
        await Assert.That(tagLinkDtoContents.Contains("this.Id = obj.Tag?.Id;")).IsTrue();
        await Assert.That(tagLinkDtoContents.Contains("this.Name = obj.Tag?.Name;")).IsTrue();
        await Assert.That(dtoContents.Contains("this.ReportedByCompanyName = obj.ReportedBy?.Company?.Name;")).IsTrue();
        await Assert.That(dtoContents.Contains("includes == \"detail\"")).IsTrue();
        await Assert.That(dtoContents.Contains("includes == \"list\"")).IsTrue();
        await Assert.That(dtoContents.Contains("includes == \"save\"")).IsTrue();
        await Assert.That(personDtoContents.Contains("this.CompanyName = obj.Company?.Name;")).IsTrue();
        await Assert.That(personDtoContents.Contains("includes == \"detail\"")).IsTrue();

        await AssertSuiteCSharpOutputCompiles(suite);
    }
}
