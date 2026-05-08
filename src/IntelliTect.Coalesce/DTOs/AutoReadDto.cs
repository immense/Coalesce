using IntelliTect.Coalesce.Mapping;
using Microsoft.EntityFrameworkCore;
using System;

namespace IntelliTect.Coalesce;

/// <summary>
/// Read-only custom DTO base that can populate itself from an entity using
/// convention-based or <see cref="DataAnnotations.DtoSourceAttribute"/>-based mapping.
/// Pair with <see cref="Mapping.AutoProjection"/> to reuse the same mapping as a SQL-translated projection.
/// </summary>
public abstract class AutoReadDto<T, TContext> : IClassDto<T, TContext>
    where T : class
    where TContext : DbContext
{
    public virtual void MapTo(T obj, IMappingContext context)
        => throw new NotSupportedException(
            $"{GetType().Name} is configured as a read-only DTO. Override MapTo if save support is required.");

    public virtual void MapFrom(T obj, IMappingContext context, IncludeTree? tree = null)
        => AutoDtoProjectionBuilder.MapToExisting(obj, this);
}
