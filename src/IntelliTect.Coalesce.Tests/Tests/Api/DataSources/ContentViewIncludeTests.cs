using IntelliTect.Coalesce.Api;
using IntelliTect.Coalesce.Testing.Fixtures;
using IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext;
using IntelliTect.Coalesce.Testing.Util;

namespace IntelliTect.Coalesce.Tests.Api.DataSources;

public class ContentViewIncludeTests : TestDbContextFixture
{
    private StandardDataSource<T, AppDbContext> Source<T>()
        where T : class, new()
        => new StandardDataSource<T, AppDbContext>(CrudContext);

    [Test]
    public async Task GetQuery_WhenExplicitListViewRequested_OnlyIncludesListNavigations()
    {
        var query = Source<ContentViewEntity>().GetQuery(new DataSourceParameters { Includes = "list" });
        var tree = query.GetIncludeTree();

        await Assert.That(tree[nameof(ContentViewEntity.AssignedTo)]).IsNotNull();
        await Assert.That(tree[nameof(ContentViewEntity.AssignedTo)][nameof(Person.Company)]).IsNull();
        await Assert.That(tree[nameof(ContentViewEntity.ReportedBy)]).IsNull();
    }

    [Test]
    public async Task GetQuery_WhenExplicitDetailViewRequested_IncludesDetailNavigations()
    {
        var query = Source<ContentViewEntity>().GetQuery(new DataSourceParameters { Includes = "detail" });
        var tree = query.GetIncludeTree();

        await Assert.That(tree[nameof(ContentViewEntity.AssignedTo)]).IsNotNull();
        await Assert.That(tree[nameof(ContentViewEntity.AssignedTo)][nameof(Person.Company)]).IsNotNull();
        await Assert.That(tree[nameof(ContentViewEntity.ReportedBy)]).IsNotNull();
        await Assert.That(tree[nameof(ContentViewEntity.ReportedBy)][nameof(Person.Company)]).IsNotNull();
    }
}
