using System;
using System.Linq.Expressions;

namespace IntelliTect.Coalesce.Mapping;

public static class AutoProjection
{
    /// <summary>
    /// Build a projection expression from <typeparamref name="TSource"/> to <typeparamref name="TTarget"/>
    /// using matching property names and optional <see cref="DataAnnotations.DtoSourceAttribute"/> annotations
    /// on the target DTO properties.
    /// </summary>
    public static Expression<Func<TSource, TTarget>> For<TSource, TTarget>()
        => AutoDtoProjectionBuilder.GetProjection<TSource, TTarget>();

    /// <summary>
    /// Build a projection expression from <typeparamref name="TSource"/> to <typeparamref name="TTarget"/>
    /// and layer one or more explicit member overrides on top of the convention-based projection.
    /// Later overrides win when the same target member is assigned more than once.
    /// </summary>
    public static Expression<Func<TSource, TTarget>> For<TSource, TTarget>(
        params Expression<Func<TSource, TTarget>>[] overrides)
        => overrides is { Length: > 0 }
            ? AutoDtoProjectionBuilder.ComposeAutoProjection(overrides)
            : For<TSource, TTarget>();

    /// <summary>
    /// Layer one or more explicit member overrides on top of an existing projection.
    /// Later overrides win when the same target member is assigned more than once.
    /// </summary>
    public static Expression<Func<TSource, TTarget>> Compose<TSource, TTarget>(
        Expression<Func<TSource, TTarget>> baseProjection,
        params Expression<Func<TSource, TTarget>>[] overrides)
        => overrides is { Length: > 0 }
            ? AutoDtoProjectionBuilder.Compose(baseProjection, overrides)
            : baseProjection;
}
