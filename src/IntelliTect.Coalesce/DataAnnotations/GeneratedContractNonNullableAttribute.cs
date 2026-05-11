using System;

namespace IntelliTect.Coalesce.DataAnnotations;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class GeneratedContractNonNullableAttribute : Attribute
{
    public GeneratedContractNonNullableAttribute(string shapeName)
    {
        ShapeName = shapeName;
    }

    public string ShapeName { get; }
}
