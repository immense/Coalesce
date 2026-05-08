using System;

namespace IntelliTect.Coalesce.DataAnnotations;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class GeneratedContractNullableAttribute : Attribute
{
    public GeneratedContractNullableAttribute(string shapeName)
    {
        ShapeName = shapeName;
    }

    public string ShapeName { get; }
}
