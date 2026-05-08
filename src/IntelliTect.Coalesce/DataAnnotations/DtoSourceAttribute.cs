using System;

namespace IntelliTect.Coalesce.DataAnnotations;

[AttributeUsage(AttributeTargets.Property)]
public sealed class DtoSourceAttribute : Attribute
{
    public DtoSourceAttribute(string path)
    {
        Path = path;
    }

    /// <summary>
    /// Dot-separated source path relative to the entity described by the DTO.
    /// Collection navigations are supported and will be projected with Select.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Optional dot-separated path, relative to a collection element, used to sort projected collections.
    /// </summary>
    public string? OrderBy { get; init; }

    public DefaultOrderByAttribute.OrderByDirections OrderByDirection { get; init; }
        = DefaultOrderByAttribute.OrderByDirections.Ascending;
}
