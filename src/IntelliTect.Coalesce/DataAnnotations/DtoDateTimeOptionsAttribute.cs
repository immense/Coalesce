using System;

namespace IntelliTect.Coalesce.DataAnnotations;

public enum DtoDateTimeMode
{
    Preserve = 0,
    Utc = 1,
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
}
