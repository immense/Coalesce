using IntelliTect.Coalesce.DataAnnotations;
using IntelliTect.Coalesce.Utilities;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTect.Coalesce.TypeDefinition;

public sealed class SummaryPropertyViewModel
{
    private readonly IReadOnlyList<PropertyViewModel> _pathProperties;

    private SummaryPropertyViewModel(
        ClassViewModel parent,
        IReadOnlyList<PropertyViewModel> pathProperties,
        string? explicitName
    )
    {
        Parent = parent;
        _pathProperties = pathProperties;
        LeafProperty = pathProperties[^1];
        Type = LeafProperty.Type;
        Name = explicitName ?? string.Concat(pathProperties.Select(p => p.Name));
        DisplayName = explicitName is null && pathProperties.Count == 1
            ? LeafProperty.DisplayName
            : Name.ToProperCase();
        IncludePath = pathProperties.Count > 1
            ? string.Join(".", pathProperties.Take(pathProperties.Count - 1).Select(p => p.Name))
            : null;
    }

    public ClassViewModel Parent { get; }

    public string Name { get; }

    public string DisplayName { get; }

    public TypeViewModel Type { get; }

    public PropertyViewModel LeafProperty { get; }

    public string? IncludePath { get; }

    public string AccessExpression(string rootExpression)
    {
        var expression = $"{rootExpression}.{_pathProperties[0].Name}";
        for (var i = 1; i < _pathProperties.Count; i++)
        {
            expression += $"?.{_pathProperties[i].Name}";
        }

        return expression;
    }

    public static IReadOnlyList<SummaryPropertyViewModel> FromClass(ClassViewModel model)
    {
        var summary = model
            .GetAttributes<DtoSummaryAttribute>()
            .Select(a => Create(model, a))
            .ToList();

        if (model.ListTextProperty is { } listText
            && model.PrimaryKey is not null
            && !ReferenceEquals(listText, model.PrimaryKey)
            && !summary.Any(p => string.Equals(p.Name, listText.Name, StringComparison.OrdinalIgnoreCase)))
        {
            summary.Insert(0, Create(listText));
        }

        return new ReadOnlyCollection<SummaryPropertyViewModel>(summary);
    }

    public static SummaryPropertyViewModel Create(PropertyViewModel property)
    {
        EnsureLeafSupported(property.Parent, property.Type, property.Name);
        return new SummaryPropertyViewModel(property.Parent, [property], null);
    }

    public static SummaryPropertyViewModel Create(ClassViewModel model, AttributeViewModel<DtoSummaryAttribute> attribute)
    {
        var path = attribute.GetValue(a => a.Path);
        var name = attribute.GetValue(a => a.Name);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"[{nameof(DtoSummaryAttribute)}] on {model.FullyQualifiedName} must specify a non-empty path.");
        }

        var segments = path
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 1)
        {
            throw new InvalidOperationException($"[{nameof(DtoSummaryAttribute)}] path '{path}' on {model.FullyQualifiedName} is invalid.");
        }

        var current = model;
        var pathProperties = new List<PropertyViewModel>(segments.Length);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var prop = current.PropertyByName(segment)
                ?? throw new InvalidOperationException($"[{nameof(DtoSummaryAttribute)}] path '{path}' on {model.FullyQualifiedName} could not resolve segment '{segment}' on {current.FullyQualifiedName}.");

            pathProperties.Add(prop);

            if (i < segments.Length - 1)
            {
                if (prop.Type.IsCollection)
                {
                    throw new InvalidOperationException($"[{nameof(DtoSummaryAttribute)}] path '{path}' on {model.FullyQualifiedName} cannot traverse collection property '{prop.Name}'.");
                }

                current = prop.Type.ClassViewModel
                    ?? throw new InvalidOperationException($"[{nameof(DtoSummaryAttribute)}] path '{path}' on {model.FullyQualifiedName} cannot traverse scalar property '{prop.Name}'.");
            }
        }

        EnsureLeafSupported(model, pathProperties[^1].Type, path);
        return new SummaryPropertyViewModel(model, pathProperties, name);
    }

    private static void EnsureLeafSupported(ClassViewModel model, TypeViewModel type, string path)
    {
        if (type.IsCollection || type.IsDictionary || type.HasClassViewModel)
        {
            throw new InvalidOperationException($"[{nameof(DtoSummaryAttribute)}] path '{path}' on {model.FullyQualifiedName} must end on a scalar/enum value, not '{type.FullyQualifiedName}'.");
        }
    }
}
