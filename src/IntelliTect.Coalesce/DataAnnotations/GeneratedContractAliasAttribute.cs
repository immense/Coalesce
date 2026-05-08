using System;

namespace IntelliTect.Coalesce.DataAnnotations;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class GeneratedContractAliasAttribute : Attribute
{
    public GeneratedContractAliasAttribute(string shapeName, string alias)
    {
        ShapeName = shapeName;
        Alias = alias;
    }

    public string ShapeName { get; }
    public string Alias { get; }
}
