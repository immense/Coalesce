using System;

namespace IntelliTect.Coalesce.DataAnnotations;

/// <summary>
/// Adds a read-only flattened DTO property for a nested property path.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DtoFlattenAttribute : Attribute
{
    public DtoFlattenAttribute(string path)
    {
        Path = path;
    }

    /// <summary>
    /// Dot-delimited property path to flatten, such as "AssignedTo.Name".
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Optional generated property name. Defaults to the concatenated path segments.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Comma-delimited list of content views this flattened property should be included on.
    /// </summary>
    public string? ContentViews { get; set; }

    /// <summary>
    /// Comma-delimited list of content views this flattened property should be excluded from.
    /// </summary>
    public string? ExcludedContentViews { get; set; }
}
