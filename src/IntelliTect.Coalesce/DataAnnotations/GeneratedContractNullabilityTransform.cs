using System;

namespace IntelliTect.Coalesce.DataAnnotations;

[Flags]
public enum GeneratedContractNullabilityTransform
{
    None = 0,
    NullableReferenceTypes = 1,
    NullableValueTypes = 2,
    NullableAll = NullableReferenceTypes | NullableValueTypes,
}
