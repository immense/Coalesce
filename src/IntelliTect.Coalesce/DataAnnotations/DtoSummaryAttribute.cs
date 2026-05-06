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
}
