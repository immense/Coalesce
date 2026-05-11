using IntelliTect.Coalesce.DataAnnotations;
using IntelliTect.Coalesce.Mapping;
using IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

#nullable enable

namespace IntelliTect.Coalesce.Testing.TargetClasses;

[Coalesce]
[Create(PermissionLevel = SecurityPermissionLevels.DenyAll)]
[Edit(PermissionLevel = SecurityPermissionLevels.DenyAll)]
[Delete(PermissionLevel = SecurityPermissionLevels.DenyAll)]
public class CaseAutoReadDto : AutoReadDto<Case, AppDbContext>
{
    [Key]
    [DtoSource(nameof(Case.CaseKey))]
    public int CaseId { get; set; }

    public string? Title { get; set; }

    [DtoSource("AssignedTo.Name")]
    public string? AssignedToName { get; set; }

    [DtoSource("ReportedBy")]
    public PersonRecord? ReportedBy { get; set; }

    [DtoSource("CaseProducts.Product.Name", OrderBy = "Product.Name")]
    public List<string>? ProductNames { get; set; }

    public record PersonRecord(int PersonId, string Name);
}
