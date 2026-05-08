using IntelliTect.Coalesce.TypeDefinition;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTect.Coalesce.TypeUsage;

public class DbContextTypeUsage
{
    public DbContextTypeUsage(ClassViewModel classViewModel)
    {
        ClassViewModel = classViewModel;
        var coalesceAttributes = classViewModel.GetAttributes<CoalesceAttribute>().ToList();
        var includeInheritedDbSets =
            coalesceAttributes
                .FirstOrDefault(attr => attr.GetAllValues().Any(v => v.Key == nameof(CoalesceAttribute.IncludeInheritedDbSets)))
                ?.GetValue<bool>(nameof(CoalesceAttribute.IncludeInheritedDbSets))
            ?? coalesceAttributes
                .FirstOrDefault()
                ?.GetValue<bool>(nameof(CoalesceAttribute.IncludeInheritedDbSets))
            ?? true;
        Entities = classViewModel
            .ClientProperties

            // Only use props that were explicitly declared on the dbcontext (and not a base class),
            // plus inherited non-Microsoft DbSets when the context opts into the legacy behavior.
            // This prevents us from picking up things from Microsoft.AspNetCore.Identity.EntityFrameworkCore
            // that don't have keys & other properties that Coalesce can work with, while still allowing
            // narrow derived contexts to intentionally expose only the DbSets they declare themselves.
            .Where(p =>
                p.Parent.Equals(classViewModel) ||
                (
                    includeInheritedDbSets &&
                    !p.PureType.FullNamespace.StartsWith(nameof(Microsoft) + ".")
                )
            )

            .Where(p => p.Type.IsA(typeof(DbSet<>)))
            .Select(p => new EntityTypeUsage(this, p.PureType, p.Name))
            .ToList()
            .AsReadOnly();

    }

    public ClassViewModel ClassViewModel { get; }

    public IReadOnlyList<EntityTypeUsage> Entities { get; }

    public override bool Equals(object? obj) => obj is DbContextTypeUsage that && that.ClassViewModel.Equals(ClassViewModel);

    public override int GetHashCode() => ClassViewModel.GetHashCode();
}
