using System;

namespace IntelliTect.Coalesce.DataAnnotations;

/// <summary>
/// Excludes the decorated property from all generated contract shapes on the containing class.
/// Optionally restrict the exclusion to a single shape by passing its name.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class GeneratedContractIgnoreAttribute : Attribute
{
    /// <summary>
    /// Ignore the property in every generated contract shape on the containing class.
    /// </summary>
    public GeneratedContractIgnoreAttribute()
    {
        ShapeName = null;
    }

    /// <summary>
    /// Ignore the property only in the specified generated contract shape.
    /// </summary>
    public GeneratedContractIgnoreAttribute(string shapeName)
    {
        ShapeName = shapeName;
    }

    /// <summary>
    /// Name of the shape this exclusion targets, or <c>null</c> for all shapes.
    /// </summary>
    public string? ShapeName { get; }
}
