#nullable enable

using IntelliTect.Coalesce.DataAnnotations;
using System;
using System.Collections.Generic;

namespace IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext;

[GeneratedContractShape(
    "details",
    GeneratedContractOutputKind.Interface,
    "IntelliTect.Coalesce.Testing",
    "IntelliTect.Coalesce.Testing.GeneratedContracts",
    "IGeneratedContractShapeDetails",
    Policy = GeneratedContractPolicy.PublicScalarProperties,
    ExcludedMembers = [nameof(Id)])]
[GeneratedContractShape(
    "write-base",
    GeneratedContractOutputKind.Interface,
    "IntelliTect.Coalesce.Testing",
    "IntelliTect.Coalesce.Testing.GeneratedContracts",
    "IGeneratedContractShapeWriteBase",
    Policy = GeneratedContractPolicy.PublicScalarProperties,
    ExcludedMembers = [nameof(Id)],
    NullabilityTransform = GeneratedContractNullabilityTransform.NullableReferenceTypes)]
[GeneratedContractShape(
    "create",
    GeneratedContractOutputKind.Class,
    "IntelliTect.Coalesce.Testing",
    "IntelliTect.Coalesce.Testing.GeneratedContracts",
    "GeneratedContractShapeCreate",
    Policy = GeneratedContractPolicy.PublicScalarProperties,
    ExcludedMembers = [nameof(Id)],
    Implements = ["IntelliTect.Coalesce.Testing.GeneratedContracts.IGeneratedContractShapeWriteBase"],
    NullabilityTransform = GeneratedContractNullabilityTransform.NullableReferenceTypes)]
[GeneratedContractShape(
    "patch",
    GeneratedContractOutputKind.Interface,
    "IntelliTect.Coalesce.Testing",
    "IntelliTect.Coalesce.Testing.GeneratedContracts",
    "IGeneratedContractShapePatch",
    Policy = GeneratedContractPolicy.PublicScalarProperties,
    ExcludedMembers = [nameof(Id)],
    NullabilityTransform = GeneratedContractNullabilityTransform.NullableAll)]
public class GeneratedContractShapeEntity
{
    public int Id { get; set; }

    [GeneratedContractNonNullable("write-base")]
    [GeneratedContractNonNullable("create")]
    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;
    public string? OptionalNotes { get; set; }
    public int Count { get; set; }
    public bool Enabled { get; set; }
    public DateTime EffectiveOn { get; set; }
    public List<string> Tags { get; set; } = [];
}

[GeneratedContractShape(
    "projection-spec",
    GeneratedContractOutputKind.Class,
    "IntelliTect.Coalesce.Testing",
    "IntelliTect.Coalesce.Testing.GeneratedContracts",
    "GeneratedContractShapeProjectionSpec",
    Members = [nameof(TenantName), nameof(OrderedChildren)])]
public class GeneratedContractShapeProjectionSource
{
    [DtoSource("OwnerTenant.Name")]
    public string? TenantName { get; set; }

    [DtoSource(
        "Children",
        OrderBy = "Name",
        OrderByDirection = DefaultOrderByAttribute.OrderByDirections.Descending)]
    public List<string> OrderedChildren { get; set; } = [];
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class GeneratedContractShapeMirrorAttribute : Attribute
{
    public GeneratedContractShapeMirrorAttribute(string label)
    {
        Label = label;
    }

    public string Label { get; }
    public bool Enabled { get; init; }
}

[GeneratedContractShape(
    "attribute-spec",
    GeneratedContractOutputKind.Class,
    "IntelliTect.Coalesce.Testing",
    "IntelliTect.Coalesce.Testing.GeneratedContracts",
    "GeneratedContractShapeAttributeSpec",
    Members = [nameof(Name)],
    IncludedPropertyAttributes = [typeof(GeneratedContractShapeMirrorAttribute)])]
public class GeneratedContractShapeAttributeSource
{
    [GeneratedContractShapeMirror("tenant-name", Enabled = true)]
    public string Name { get; set; } = null!;
}
