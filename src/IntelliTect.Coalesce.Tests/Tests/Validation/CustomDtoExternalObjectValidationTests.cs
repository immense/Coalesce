using IntelliTect.Coalesce.Testing.TargetClasses;
using IntelliTect.Coalesce.Testing.Util;
using IntelliTect.Coalesce.TypeDefinition;
using IntelliTect.Coalesce.Validation;
using Microsoft.CodeAnalysis;
using System.Linq;

namespace IntelliTect.Coalesce.Tests.Validation;

public class CustomDtoExternalObjectValidationTests
{
    [Test]
    public async Task ReflectionValidation_AllowsExternalObjectPropertiesWithoutListText()
    {
        var validationIssues = ValidateContext.Validate(CreateReflectionRepository())
            .Where(issue => !issue.WasSuccessful && !issue.IsWarning)
            .Select(issue => issue.ToString())
            .ToList();

        await Assert.That(validationIssues).IsEmpty();
    }

    [Test]
    public async Task SymbolValidation_AllowsExternalObjectPropertiesWithoutListText()
    {
        var validationIssues = ValidateContext.Validate(CreateSymbolRepository())
            .Where(issue => !issue.WasSuccessful && !issue.IsWarning)
            .Select(issue => issue.ToString())
            .ToList();

        await Assert.That(validationIssues).IsEmpty();
    }

    private static ReflectionRepository CreateReflectionRepository()
    {
        var repository = new ReflectionRepository();
        repository.SetRootTypeWhitelist(new[] { nameof(CaseDtoWithExternalObject) });
        repository.AddAssembly<CaseDtoWithExternalObject>();
        return repository;
    }

    private static ReflectionRepository CreateSymbolRepository()
    {
        var repository = new ReflectionRepository();
        repository.SetRootTypeWhitelist(new[] { nameof(CaseDtoWithExternalObject) });
        repository.DiscoverCoalescedTypes(
            ReflectionRepositoryFactory.Symbols
                .Where(symbol =>
                    symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is string fullyQualifiedName
                    && (
                        !fullyQualifiedName.Contains("IntelliTect.Coalesce.Testing.TargetClasses")
                        || (symbol is IArrayTypeSymbol arrayType ? arrayType.ElementType : symbol).ContainingAssembly?.MetadataName
                            == ReflectionRepositoryFactory.SymbolDiscoveryAssemblyName
                    ))
                .Select(symbol => new SymbolTypeViewModel(repository, symbol))
        );
        return repository;
    }
}
