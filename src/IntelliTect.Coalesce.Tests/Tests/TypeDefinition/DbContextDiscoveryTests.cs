using IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext;
using IntelliTect.Coalesce.Testing.Util;
using IntelliTect.Coalesce.TypeDefinition;
using Microsoft.CodeAnalysis;

namespace IntelliTect.Coalesce.Tests.TypeDefinition;

public class DbContextDiscoveryTests
{
    [Test]
    public async Task CoalesceContext_CanExcludeInheritedDbSets()
    {
        var repo = new ReflectionRepository();
        repo.SetRootTypeWhitelist(new[] { nameof(CompanyOnlyDbContext) });
        repo.GetOrAddType(typeof(CompanyOnlyDbContext));
        var entities = repo.Entities.ToList();
        var contextEntities = repo.DbContexts
            .Single(context => context.ClassViewModel.Name == nameof(CompanyOnlyDbContext))
            .Entities
            .ToList();

        await Assert.That(entities.Count).IsEqualTo(1);
        await Assert.That(entities.Single().Name).IsEqualTo(nameof(Company));
        await Assert.That(contextEntities.Single().ContextPropertyName).IsEqualTo(nameof(CompanyOnlyDbContext.Companies));
    }

    [Test]
    public async Task CoalesceContext_CanExcludeInheritedDbSets_OnSymbolDiscovery()
    {
        var repo = new ReflectionRepository();
        repo.SetRootTypeWhitelist(new[] { nameof(CompanyOnlyDbContext) });
        repo.DiscoverCoalescedTypes(
            ReflectionRepositoryFactory.Symbols
                .Where(symbol =>
                    symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is string fullyQualifiedName
                    && (
                        !fullyQualifiedName.Contains("IntelliTect.Coalesce.Testing.TargetClasses")
                        || (symbol is IArrayTypeSymbol arrayType ? arrayType.ElementType : symbol).ContainingAssembly?.MetadataName
                            == ReflectionRepositoryFactory.SymbolDiscoveryAssemblyName
                    ))
                .Select(symbol => new SymbolTypeViewModel(repo, symbol))
        );

        var entities = repo.Entities.ToList();
        var contextEntities = repo.DbContexts
            .Single(context => context.ClassViewModel.Name == nameof(CompanyOnlyDbContext))
            .Entities
            .ToList();

        await Assert.That(entities.Count).IsEqualTo(1);
        await Assert.That(entities.Single().Name).IsEqualTo(nameof(Company));
        await Assert.That(contextEntities.Single().ContextPropertyName).IsEqualTo(nameof(CompanyOnlyDbContext.Companies));
    }
}
