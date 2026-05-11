using System;

namespace IntelliTect.Coalesce.DataAnnotations;

/// <summary>
/// Defines a named DTO content view for a model.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DtoContentViewAttribute : Attribute
{
    public DtoContentViewAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// The name of the content view.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// If false, properties without explicit view annotations are excluded when this view is active.
    /// </summary>
    public bool IncludeByDefault { get; set; } = true;
}
