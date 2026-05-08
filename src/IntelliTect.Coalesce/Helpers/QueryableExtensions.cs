using IntelliTect.Coalesce.DataAnnotations;
using IntelliTect.Coalesce.TypeDefinition;
using IntelliTect.Coalesce.Utilities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTect.Coalesce;

public static class QueryableExtensions
{
    /// <summary>
    /// <para>Includes immediate children, as well as the other side of many-to-many relationships.</para>
    /// <para>Does not include navigations or classes that have <see cref="ReadAttribute.NoAutoInclude"/> or <see cref="CoalesceConfigurationAttribute.NoAutoInclude"/> set.</para>
    /// </summary>
    public static IQueryable<T> IncludeChildren<T>(this IQueryable<T> query, ReflectionRepository? reflectionRepository = null, string? includes = null) where T : class
    {
        var model = (reflectionRepository ?? ReflectionRepository.Global).GetClassViewModel<T>()
            ?? throw new ArgumentException("Queried type is not a class");

        var includePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in model.ClientProperties.Where(f => f.CanAutoInclude && f.IsMappedForContentView(includes)))
        {
            if (prop.IsManyToManyCollection && prop.ManyToManyFarNavigationProperty.CanAutoInclude)
            {
                includePaths.Add(prop.Name + "." + prop.ManyToManyFarNavigationProperty!.Name);
            }
            else
            {
                includePaths.Add(prop.Name);
            }
        }

        foreach (var flattened in model.FlattenedResponseProperties.Where(f => f.IsMappedForContentView(includes)))
        {
            includePaths.Add(flattened.IncludePath);
        }

        AddReferenceSummaryIncludePaths(model, includePaths, includes);

        foreach (var includePath in includePaths)
        {
            query = query.Include(includePath);
        }

        return query;
    }

    private static void AddReferenceSummaryIncludePaths(ClassViewModel model, HashSet<string> includePaths, string? includes)
    {
        foreach (var prop in model.ClientProperties.Where(p =>
            p.UsesDtoReferenceSummary
            && p.Object is not null
            && p.IsMappedForContentView(includes)))
        {
            var target = prop.Object!;
            includePaths.Add(prop.Name);

            foreach (var summaryProp in target.SummaryProperties.Where(p => p.IsMappedForContentView(includes)))
            {
                if (!string.IsNullOrWhiteSpace(summaryProp.IncludePath))
                {
                    includePaths.Add($"{prop.Name}.{summaryProp.IncludePath}");
                }
            }
        }
    }

    /// <summary>
    /// Filters a query by a given primary key value.
    /// </summary>
    /// <returns>The filtered query.</returns>
    public static IQueryable<T> WherePrimaryKeyIs<T>(this IQueryable<T> query, object id, ReflectionRepository? reflectionRepository = null)
    {
        var classViewModel = (reflectionRepository ?? ReflectionRepository.Global).GetClassViewModel<T>()
            ?? throw new ArgumentException("Queried type is not a class");

        var pkProp = classViewModel.PrimaryKey
            ?? throw new ArgumentException($"Unable to determine primary key of {classViewModel.FullyQualifiedName}");

        return query.WhereExpression(it => Expression.Equal(it.Prop(pkProp), id.AsQueryParam(pkProp.Type)));
    }

    /// <summary>
    /// Asynchronously finds an object based on a specific primary key value.
    /// </summary>
    /// <returns>The desired item, or null if it was not found.</returns>
    public static Task<T?> FindItemAsync<T>(this IQueryable<T> query, object id, ReflectionRepository? reflectionRepository = null, CancellationToken cancellationToken = default)
        where T : class
    {
        return query.WherePrimaryKeyIs(id, reflectionRepository).FirstOrDefaultAsync(cancellationToken)!;
    }

    /// <summary>
    /// Finds an object based on a specific primary key value.
    /// </summary>
    /// <returns>The desired item, or null if it was not found.</returns>
    public static T? FindItem<T>(this IQueryable<T> query, object id, ReflectionRepository? reflectionRepository = null)
        where T : class
    {
        return query.WherePrimaryKeyIs(id, reflectionRepository).FirstOrDefault();
    }

    public static IQueryable<T> OrderBy<T>(
        this IQueryable<T> query,
        IEnumerable<OrderByInformation> orderings)
        where T : class
    {
        bool isFirst = true;

        foreach (var ordering in orderings)
        {
            var expression = ordering!.LambdaExpression<T>();
            query = (IQueryable<T>)ordering.OrderByMethod<T>(isFirst).Invoke(null, [query, expression])!;
            isFirst = false;
        }

        return query;
    }

    internal static IQueryable<T> WhereExpression<T>(
        this IQueryable<T> query,
        Func<ParameterExpression, Expression> predicateBuilder
    )
    {
        var param = Expression.Parameter(typeof(T));
        return query.Where(Expression.Lambda<Func<T, bool>>(predicateBuilder(param), param));
    }
}
