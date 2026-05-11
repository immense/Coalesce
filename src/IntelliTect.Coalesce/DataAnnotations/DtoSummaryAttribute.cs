using System;

namespace IntelliTect.Coalesce.DataAnnotations;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class DtoSummaryAttribute : Attribute
{
    public DtoSummaryAttribute(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public string? Name { get; set; }

    /// <summary>
    /// Comma-delimited list of content views this summary property should be included on.
    /// </summary>
    public string? ContentViews { get; set; }

    /// <summary>
    /// Comma-delimited list of content views this summary property should be excluded from.
    /// </summary>
    public string? ExcludedContentViews { get; set; }
}
