using System;

namespace IntelliTect.Coalesce.DataAnnotations;

/// <summary>
/// Assembly-level defaults for <see cref="GeneratedContractShapeAttribute"/>. Properties on this
/// attribute fill in any value the individual shape attribute did not set explicitly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class CoalesceGeneratedContractDefaultsAttribute : Attribute
{
    /// <summary>
    /// Default value for <see cref="GeneratedContractShapeAttribute.GenerateConstructors"/>
    /// when the shape attribute does not specify it.
    /// </summary>
    public bool GenerateConstructors { get; set; } = true;

    /// <summary>
    /// Prefix prepended to shape names that the source generator auto-derives from the
    /// source class name. Has no effect on shape names supplied explicitly on the
    /// <see cref="GeneratedContractShapeAttribute"/>.
    /// </summary>
    public string? ShapeNamePrefix { get; set; }
}
