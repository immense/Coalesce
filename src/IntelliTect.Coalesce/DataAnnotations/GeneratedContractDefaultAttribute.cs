using System;

namespace IntelliTect.Coalesce.DataAnnotations;

/// <summary>
/// Supplies a C# expression used to initialize the decorated property inside the
/// generated parameterless constructor.
/// <para>
/// The expression is emitted verbatim into the generated code — the source generator
/// does not validate or rewrite it. For example, <c>[GeneratedContractDefault("[]")]</c>
/// on a <c>List&lt;int&gt;</c> property emits <c>PropertyName = [];</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedContractDefaultAttribute : Attribute
{
    public GeneratedContractDefaultAttribute(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Raw C# expression emitted as the right-hand side of the initializer.
    /// </summary>
    public string Value { get; }
}
