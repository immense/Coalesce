using System;

namespace IntelliTect.Coalesce.DataAnnotations;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class GeneratedContractShapeAttribute : Attribute
{
    /// <summary>
    /// Declares a generated contract shape with full explicit control over output location.
    /// </summary>
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

    /// <summary>
    /// Declares a generated contract shape using conventions to infer assembly, namespace, and type name.
    /// <para>Conventions:</para>
    /// <list type="bullet">
    /// <item><c>TargetAssemblyName</c>: inferred from the consuming compilation's assembly name.</item>
    /// <item><c>TargetNamespace</c>: inferred from the source class's namespace.</item>
    /// <item><c>TypeName</c>: inferred from the source class name minus "ContractSource"/"Source" suffix.</item>
    /// <item><c>OutputKind</c>: defaults to <see cref="GeneratedContractOutputKind.Class"/>.</item>
    /// <item><c>Policy</c>: defaults to <see cref="GeneratedContractPolicy.PublicScalarProperties"/>.</item>
    /// </list>
    /// </summary>
    public GeneratedContractShapeAttribute(string shapeName)
    {
        ShapeName = shapeName;
        OutputKind = GeneratedContractOutputKind.Class;
        TargetAssemblyName = string.Empty;
        TargetNamespace = string.Empty;
        TypeName = string.Empty;
        Policy = GeneratedContractPolicy.PublicScalarProperties;
    }

    public string ShapeName { get; }
    public GeneratedContractOutputKind OutputKind { get; init; }
    public string TargetAssemblyName { get; }
    public string TargetNamespace { get; }
    public string TypeName { get; }
    public GeneratedContractPolicy Policy { get; init; }
    public string[] Members { get; init; } = [];
    public string[] ExcludedMembers { get; init; } = [];
    public string[] Implements { get; init; } = [];
    public Type[] IncludedPropertyAttributes { get; init; } = [];
    public bool SettableProperties { get; init; }

    /// <summary>
    /// When true (default for Class output), the generator emits parameterless
    /// and all-args constructors on the generated type.
    /// </summary>
    public bool GenerateConstructors { get; init; } = true;

    public GeneratedContractNullabilityTransform NullabilityTransform { get; init; }
}
