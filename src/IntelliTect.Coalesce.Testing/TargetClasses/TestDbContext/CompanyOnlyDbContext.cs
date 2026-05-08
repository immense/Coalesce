using IntelliTect.Coalesce.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext;

[Coalesce(IncludeInheritedDbSets = false)]
public class CompanyOnlyDbContext : AppDbContext
{
    public CompanyOnlyDbContext() { }

    public CompanyOnlyDbContext(DbContextOptions<CompanyOnlyDbContext> options)
        : base(options) { }

    public new DbSet<Company> Companies => Set<Company>();
}
