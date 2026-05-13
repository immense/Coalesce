using System;

namespace IntelliTect.Coalesce.DataAnnotations;

/// <summary>
/// Specifies default DTO content views for generated CRUD controller actions.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DtoActionDefaultsAttribute : Attribute
{
    public string? Get { get; set; }
    public string? List { get; set; }
    public string? Count { get; set; }
    public string? Save { get; set; }
    public string? BulkSave { get; set; }
    public string? Delete { get; set; }

    /// <summary>
    /// If true, generated standard CRUD controller actions will use distinct response DTO types
    /// based on their configured content views, and will enforce those content views at runtime.
    /// </summary>
    public bool UseContentViewResponseTypes { get; set; }
}
