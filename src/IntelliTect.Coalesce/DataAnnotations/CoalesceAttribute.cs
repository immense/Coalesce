using System;

namespace IntelliTect.Coalesce;

/// <summary>
/// The targeted class or member should be exposed by Coalesce.
/// Different types will be exposed in different ways. See documentation for details.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Field, Inherited = false)]
public sealed class CoalesceAttribute : Attribute
{
    /// <summary>
    /// When placed on a type, overrides the name of the type used in client-side code.
    /// </summary>
    public string? ClientTypeName { get; set; }

    /// <summary>
    /// When placed on a type, overrides the generated server-side response DTO class name.
    /// For example, setting this to <c>GetTenantResponse</c> will cause Coalesce to generate
    /// that response DTO class name instead of the default <c>TenantResponse</c>.
    /// </summary>
    public string? ResponseDtoClassName { get; set; }

    /// <summary>
    /// When placed on a <see cref="Microsoft.EntityFrameworkCore.DbContext"/>, controls whether
    /// inherited non-Microsoft <see cref="Microsoft.EntityFrameworkCore.DbSet{TEntity}"/> properties
    /// are discovered alongside sets declared directly on the attributed context type.
    /// </summary>
    public bool IncludeInheritedDbSets { get; set; } = true;
}
