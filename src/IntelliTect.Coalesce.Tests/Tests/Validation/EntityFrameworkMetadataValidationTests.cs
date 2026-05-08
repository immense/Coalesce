using IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext;
using IntelliTect.Coalesce.Testing.Util;
using IntelliTect.Coalesce.TypeDefinition;
using IntelliTect.Coalesce.Validation;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTect.Coalesce.Tests.Validation;

public class EntityFrameworkMetadataValidationTests
{
    [Test]
    public async Task ReflectionValidation_AllowsFluentSingleKeyAndValueObjects()
    {
        var validationIssues = ValidateContext.Validate(CreateReflectionRepository())
            .Where(issue => !issue.WasSuccessful && !issue.IsWarning)
            .Select(issue => issue.ToString())
            .ToList();

        await Assert.That(validationIssues).IsEmpty();
    }

    [Test]
    public async Task SymbolValidation_AllowsFluentSingleKeyAndValueObjects()
    {
        var validationIssues = ValidateContext.Validate(CreateSymbolRepository())
            .Where(issue => !issue.WasSuccessful && !issue.IsWarning)
            .Select(issue => issue.ToString())
            .ToList();

        await Assert.That(validationIssues).IsEmpty();
    }

    [Test]
    public async Task ReflectionRepository_OnlyPromotesWhitelistedDbSetEntities()
    {
        var repository = CreateReflectionRepository<AppDbContext>(nameof(Case));

        await Assert.That(repository.CrudApiBackedClasses.Select(c => c.Name))
            .IsEquivalentTo([nameof(Case)]);
    }

    [Test]
    public async Task SymbolRepository_OnlyPromotesWhitelistedDbSetEntities()
    {
        var repository = CreateSymbolRepository<AppDbContext>(nameof(Case));

        await Assert.That(repository.CrudApiBackedClasses.Select(c => c.Name))
            .IsEquivalentTo([nameof(Case)]);
    }

    private static ReflectionRepository CreateReflectionRepository()
    {
        return CreateReflectionRepository<FluentMetadataDbContext>();
    }

    private static ReflectionRepository CreateSymbolRepository()
    {
        return CreateSymbolRepository<FluentMetadataDbContext>();
    }

    private static ReflectionRepository CreateReflectionRepository<TContext>(params string[] additionalRoots)
    {
        var repository = new ReflectionRepository();
        repository.SetRootTypeWhitelist([typeof(TContext).Name, ..additionalRoots]);
        repository.AddAssembly<TContext>();
        return repository;
    }

    private static ReflectionRepository CreateSymbolRepository<TContext>(params string[] additionalRoots)
    {
        var repository = new ReflectionRepository();
        repository.SetRootTypeWhitelist([typeof(TContext).Name, ..additionalRoots]);
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
