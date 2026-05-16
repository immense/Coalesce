using System;

namespace IntelliTect.Coalesce.DataAnnotations;

public enum DtoDateTimeMode
{
    Preserve = 0,
    Utc = 1,
}

public enum UtcSuffixCasing
{
    PascalCase = 0,
    UpperCase = 1,
}

/// <summary>
/// Configures generated response DTO handling for <see cref="DateTime"/> properties.
/// Can be applied at the model or assembly level.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DtoDateTimeOptionsAttribute : Attribute
{
    public DtoDateTimeOptionsAttribute(DtoDateTimeMode mode)
    {
        Mode = mode;
    }

    public DtoDateTimeMode Mode { get; }

    /// <summary>
    /// Controls the casing of the "Utc"/"UTC" suffix appended to response DTO
    /// property names when <see cref="Mode"/> is <see cref="DtoDateTimeMode.Utc"/>.
    /// Defaults to <see cref="UtcSuffixCasing.PascalCase"/>.
    /// </summary>
    public UtcSuffixCasing UtcSuffixCasing { get; set; } = UtcSuffixCasing.PascalCase;
}
