using IntelliTect.Coalesce.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;

namespace IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext;

[Coalesce]
public class FluentMetadataDbContext : DbContext
{
    public DbSet<FluentConfiguredEntity> FluentConfiguredEntities { get; set; }

    public FluentMetadataDbContext()
        : this(Guid.NewGuid().ToString())
    {
    }

    public FluentMetadataDbContext(string memoryDatabaseName)
        : base(new DbContextOptionsBuilder<FluentMetadataDbContext>()
            .UseInMemoryDatabase(memoryDatabaseName)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.NavigationBaseIncludeIgnored))
            .Options)
    {
    }

    public FluentMetadataDbContext(DbContextOptions options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FluentConfiguredEntity>(entity =>
        {
            entity.HasKey(e => e.TenantScopedKey);

            entity.OwnsOne(e => e.OwnedValue);

            entity.Property(e => e.ConvertedValue)
                .HasConversion(
                    value => value.Value,
                    value => new FluentConvertedValueObject { Value = value });

#if NET10_0_OR_GREATER
            entity.ComplexProperty(e => e.ComplexValue);
#endif
        });
    }
}

public class FluentConfiguredEntity
{
    public int TenantScopedKey { get; set; }
    public string Name { get; set; } = null!;
    public FluentOwnedValueObject OwnedValue { get; set; } = new();
    public FluentConvertedValueObject ConvertedValue { get; set; } = new();
#if NET10_0_OR_GREATER
    public FluentComplexValueObject ComplexValue { get; set; } = new();
#endif
}

public class FluentOwnedValueObject
{
    public string Value { get; set; } = null!;
}

public class FluentConvertedValueObject
{
    public string Value { get; set; } = null!;
}

#if NET10_0_OR_GREATER
public class FluentComplexValueObject
{
    public string Value { get; set; } = null!;
}
#endif
