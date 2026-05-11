using IntelliTect.Coalesce.Api;
using IntelliTect.Coalesce.Mapping;
using IntelliTect.Coalesce.Models;
using IntelliTect.Coalesce.Testing.Fixtures;
using IntelliTect.Coalesce.Testing.TargetClasses;
using IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace IntelliTect.Coalesce.Tests.Api.DataSources;

public class AutoProjectedDtoDataSourceTests : TestDbContextFixture
{
    private CaseAutoReadSource Source() => new(CrudContext);

    [Test]
    public async Task GetMappedListAsync_Projects_AutoReadDto()
    {
        Db.Cases.Add(new Case
        {
            Title = "Printer offline",
            AssignedTo = new Person { FirstName = "Ada", LastName = "Lovelace", Title = Person.Titles.Ms },
            ReportedBy = new Person { FirstName = "Grace", LastName = "Hopper", Title = Person.Titles.Mrs },
            CaseProducts =
            [
                new() { Product = new Product { Name = "Windows" } },
                new() { Product = new Product { Name = "Azure" } },
            ]
        });
        Db.SaveChanges();
        Db.ChangeTracker.Clear();

        var result = await Source().GetMappedListAsync<CaseAutoReadDto>(new ListParameters());

        await Assert.That(result.List).HasSingleItem();
        var dto = result.List.Single();
        await Assert.That(dto.CaseId).IsGreaterThan(0);
        await Assert.That(dto.Title).IsEqualTo("Printer offline");
        await Assert.That(dto.AssignedToName).IsEqualTo("Ms Ada Lovelace");
        await Assert.That(dto.ReportedBy).IsNotNull();
        await Assert.That(dto.ReportedBy!.Name).IsEqualTo("Mrs Grace Hopper");
        await Assert.That(dto.ProductNames).IsEquivalentTo(new[] { "Azure", "Windows" });
    }

    [Test]
    public async Task MapFrom_Uses_AutoProjection()
    {
        var entity = new Case
        {
            CaseKey = 42,
            Title = "VPN issue",
            AssignedTo = new Person { FirstName = "Katherine", LastName = "Johnson", Title = Person.Titles.Ms },
            ReportedBy = new Person { PersonId = 7, FirstName = "Margaret", LastName = "Hamilton", Title = Person.Titles.Mrs },
            CaseProducts =
            [
                new() { Product = new Product { Name = "Intune" } }
            ]
        };

        var dto = new CaseAutoReadDto();
        dto.MapFrom(entity, new MappingContext());

        await Assert.That(dto.CaseId).IsEqualTo(42);
        await Assert.That(dto.AssignedToName).IsEqualTo("Ms Katherine Johnson");
        await Assert.That(dto.ReportedBy).IsEquivalentTo(new CaseAutoReadDto.PersonRecord(7, "Mrs Margaret Hamilton"));
        await Assert.That(dto.ProductNames).IsEquivalentTo(new[] { "Intune" });
    }

    [Test]
    public async Task ComposedProjection_Overrides_Computed_And_Child_Members()
    {
        Db.Cases.Add(new Case
        {
            Title = "SSO issue",
            AssignedTo = new Person { FirstName = "Katherine", LastName = "Johnson", Title = Person.Titles.Ms },
            ReportedBy = new Person { PersonId = 7, FirstName = "Margaret", LastName = "Hamilton", Title = Person.Titles.Mrs },
            CaseProducts =
            [
                new() { Product = new Product { Name = "Intune" } },
                new() { Product = new Product { Name = "Azure" } },
            ]
        });
        Db.SaveChanges();
        Db.ChangeTracker.Clear();

        var dto = await Db.Cases
            .IncludeChildren()
            .Select(AutoProjection.For<Case, CaseAutoReadDto>(
                c => new CaseAutoReadDto
                {
                    AssignedToName = c.AssignedTo == null ? null : c.AssignedTo.FirstName + " " + c.AssignedTo.LastName,
                    ProductNames = c.CaseProducts
                        .OrderBy(cp => cp.Product!.Name)
                        .Select(cp => cp.Product!.Name + "!")
                        .ToList(),
                }))
            .SingleAsync();

        await Assert.That(dto.Title).IsEqualTo("SSO issue");
        await Assert.That(dto.ReportedBy).IsEquivalentTo(new CaseAutoReadDto.PersonRecord(7, "Mrs Margaret Hamilton"));
        await Assert.That(dto.AssignedToName).IsEqualTo("Katherine Johnson");
        await Assert.That(dto.ProductNames).IsEquivalentTo(new[] { "Azure!", "Intune!" });
    }

    private sealed class CaseAutoReadSource(CrudContext<AppDbContext> context)
        : ProjectedDtoDataSource<Case, CaseAutoReadDto, AppDbContext>(context)
    {
        public override IQueryable<Case> GetQuery(IDataSourceParameters parameters)
            => Db.Cases.IncludeChildren();

        public override IQueryable<CaseAutoReadDto> ApplyProjection(IQueryable<Case> query, IDataSourceParameters parameters)
            => query.Select(AutoProjection.For<Case, CaseAutoReadDto>());
    }
}
