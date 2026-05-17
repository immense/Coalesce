using System;

namespace IntelliTect.Coalesce.DataAnnotations;

/// <summary>
/// Supplies a C# expression used as the default value for the corresponding parameter
/// in the generated all-args constructor.
/// <para>
/// The expression is emitted verbatim — e.g. <c>[GeneratedContractParameterDefault("false")]</c>
/// on a <c>bool IncludeDisabled</c> property produces
/// <c>public X(..., bool IncludeDisabled = false, ...)</c>.
/// </para>
/// <para>
/// C# requires that parameters with defaults follow parameters without defaults, so this
/// attribute is typically applied to the trailing properties of the contract source class.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedContractParameterDefaultAttribute : Attribute
{
    public GeneratedContractParameterDefaultAttribute(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Raw C# expression emitted as the default value for the constructor parameter.
    /// </summary>
    public string Value { get; }
}
