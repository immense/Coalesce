using System;

namespace IntelliTect.Coalesce.DataAnnotations;

/// <summary>
/// Injects a raw C# member declaration into the generated partial class body.
/// The escape hatch for members the generator cannot model declaratively
/// (computed properties, static factories, internal fields, etc.).
/// <para>
/// The code string is emitted verbatim and is not validated. For example:
/// <c>[GeneratedContractRawMember("public DatabaseType DatabaseType =&gt; DatabaseType.Global;")]</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class GeneratedContractRawMemberAttribute : Attribute
{
    /// <summary>
    /// Inject the member into every generated shape on this class.
    /// </summary>
    public GeneratedContractRawMemberAttribute(string code)
    {
        Code = code;
    }

    /// <summary>
    /// Inject the member only into the named shape.
    /// </summary>
    public GeneratedContractRawMemberAttribute(string shapeName, string code)
    {
        ShapeName = shapeName;
        Code = code;
    }

    /// <summary>Shape this member targets, or <c>null</c> for all shapes.</summary>
    public string? ShapeName { get; }

    /// <summary>Raw C# emitted verbatim inside the partial class body.</summary>
    public string Code { get; }
}
