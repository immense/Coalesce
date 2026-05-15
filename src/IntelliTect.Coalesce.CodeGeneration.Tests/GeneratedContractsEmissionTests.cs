using System.Linq;
using IntelliTect.Coalesce.CodeGeneration.Api.Generators;
using IntelliTect.Coalesce.CodeGeneration.Configuration;
using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.CodeGeneration.Vue.Generators;
using IntelliTect.Coalesce.Testing.Util;
using Microsoft.Extensions.Logging;

namespace IntelliTect.Coalesce.CodeGeneration.Tests;

public class GeneratedContractsEmissionTests
{
    [Test]
    public async Task VueSuite_DoesNotIncludeGeneratedContractsDiskGenerator()
    {
        var executor = new GenerationExecutor(
            new CoalesceConfiguration
            {
                WebProject = new ProjectConfiguration { RootNamespace = "MyProject" }
            },
            LogLevel.Warning);

        var generator = executor.CreateRootGenerator<VueSuite>()
            .WithModel(ReflectionRepositoryFactory.Symbol);

        var generators = generator.GetGeneratorsFlattened().ToList();

        await Assert.That(generators.Any(g => g is GeneratedContracts)).IsFalse();
    }
}
