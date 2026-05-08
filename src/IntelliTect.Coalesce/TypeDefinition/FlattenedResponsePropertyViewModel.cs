using IntelliTect.Coalesce.DataAnnotations;
using IntelliTect.Coalesce.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IntelliTect.Coalesce.TypeDefinition;

public sealed class FlattenedResponsePropertyViewModel
{
    private FlattenedResponsePropertyViewModel(
        ClassViewModel declaringClass,
        DtoFlattenAttribute attribute,
        IReadOnlyList<PropertyViewModel> pathProperties)
    {
        DeclaringClass = declaringClass;
        Attribute = attribute;
        PathProperties = pathProperties;
        Name = string.IsNullOrWhiteSpace(attribute.Name)
            ? string.Concat(pathProperties.Select(p => p.Name))
            : attribute.Name!;
    }

    public ClassViewModel DeclaringClass { get; }
    public DtoFlattenAttribute Attribute { get; }
    public IReadOnlyList<PropertyViewModel> PathProperties { get; }
    public string Name { get; }
    public string DisplayName => Name.ToProperCase();
    public string Path => Attribute.Path;
    public PropertyViewModel RootProperty => PathProperties[0];
    public PropertyViewModel LeafProperty => PathProperties[^1];
    public TypeViewModel Type => LeafProperty.Type;
    public string IncludePath => string.Join(".", PathProperties.Take(PathProperties.Count - 1).Select(p => p.Name));
    public IEnumerable<string> ContentViews => SplitContentViews(Attribute.ContentViews);
    public IEnumerable<string> ExcludedContentViews => SplitContentViews(Attribute.ExcludedContentViews);

    public bool IsMappedForContentView(string? contentView)
    {
        if (string.IsNullOrWhiteSpace(contentView))
        {
            return !ContentViews.Any();
        }

        if (ExcludedContentViews.Contains(contentView, StringComparer.Ordinal))
        {
            return false;
        }

        if (ContentViews.Any())
        {
            return ContentViews.Contains(contentView, StringComparer.Ordinal);
        }

        return DeclaringClass.ShouldIncludeUnspecifiedPropertiesForContentView(contentView);
    }

    public string AccessExpression(string rootExpression)
    {
        if (PathProperties.Count < 2)
        {
            throw new InvalidOperationException($"Flattened DTO property '{Name}' must have at least two path segments.");
        }

        var expression = rootExpression;
        for (var i = 0; i < PathProperties.Count; i++)
        {
            var separator = i == 0 ? "." : "?.";
            expression += separator + PathProperties[i].Name;
        }

        return expression;
    }

    public static IReadOnlyList<FlattenedResponsePropertyViewModel> FromClass(ClassViewModel model)
    {
        var flattened = model
            .GetAttributes<DtoFlattenAttribute>()
            .Select(a => Create(model, a))
            .ToList();

        return new ReadOnlyCollection<FlattenedResponsePropertyViewModel>(flattened);
    }

    public static FlattenedResponsePropertyViewModel Create(ClassViewModel model, AttributeViewModel<DtoFlattenAttribute> attribute)
    {
        var path = attribute.GetValue(a => a.Path);
        var name = attribute.GetValue(a => a.Name);
        var contentViews = attribute.GetValue(a => a.ContentViews);
        var excludedContentViews = attribute.GetValue(a => a.ExcludedContentViews);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"[{nameof(DtoFlattenAttribute)}] on {model.FullyQualifiedName} must specify a non-empty path.");
        }

        var segments = path
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 2)
        {
            throw new InvalidOperationException($"[{nameof(DtoFlattenAttribute)}] path '{path}' on {model.FullyQualifiedName} must contain at least one navigation and one leaf property.");
        }

        var pathProperties = new List<PropertyViewModel>(segments.Length);
        ClassViewModel current = model;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var prop = current.PropertyByName(segment)
                ?? throw new InvalidOperationException($"[{nameof(DtoFlattenAttribute)}] path '{path}' on {model.FullyQualifiedName} could not resolve segment '{segment}' on {current.FullyQualifiedName}.");

            pathProperties.Add(prop);

            if (i < segments.Length - 1)
            {
                if (prop.Type.IsCollection)
                {
                    throw new InvalidOperationException($"[{nameof(DtoFlattenAttribute)}] path '{path}' on {model.FullyQualifiedName} cannot traverse collection property '{prop.Name}'.");
                }

                current = prop.Type.ClassViewModel
                    ?? throw new InvalidOperationException($"[{nameof(DtoFlattenAttribute)}] path '{path}' on {model.FullyQualifiedName} cannot traverse scalar property '{prop.Name}'.");
            }
        }

        var leaf = pathProperties[^1];
        if (leaf.Type.IsCollection || leaf.Type.IsDictionary || leaf.Type.HasClassViewModel)
        {
            throw new InvalidOperationException($"[{nameof(DtoFlattenAttribute)}] path '{path}' on {model.FullyQualifiedName} must end on a scalar/enum value, not '{leaf.Type.FullyQualifiedName}'.");
        }

        return new FlattenedResponsePropertyViewModel(
            model,
            new DtoFlattenAttribute(path)
            {
                Name = name,
                ContentViews = contentViews,
                ExcludedContentViews = excludedContentViews,
            },
            pathProperties);
    }

    private static IEnumerable<string> SplitContentViews(string? contentViews)
        => (contentViews ?? "")
            .Trim()
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s));
}
