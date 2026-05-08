using System;

namespace IntelliTect.Coalesce.DataAnnotations;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class GeneratedContractShapeAttribute : Attribute
{
    public GeneratedContractShapeAttribute(
        string shapeName,
        GeneratedContractOutputKind outputKind,
        string targetAssemblyName,
        string targetNamespace,
        string typeName)
    {
        ShapeName = shapeName;
        OutputKind = outputKind;
        TargetAssemblyName = targetAssemblyName;
        TargetNamespace = targetNamespace;
        TypeName = typeName;
    }

    public string ShapeName { get; }
    public GeneratedContractOutputKind OutputKind { get; }
    public string TargetAssemblyName { get; }
    public string TargetNamespace { get; }
    public string TypeName { get; }
    public GeneratedContractPolicy Policy { get; init; }
    public string[] Members { get; init; } = [];
    public string[] ExcludedMembers { get; init; } = [];
    public string[] Implements { get; init; } = [];
    public bool SettableProperties { get; init; }
}
