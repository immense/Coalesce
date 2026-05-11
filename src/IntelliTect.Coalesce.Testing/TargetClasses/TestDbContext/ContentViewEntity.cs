using IntelliTect.Coalesce.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext;

[Create(PermissionLevel = SecurityPermissionLevels.AllowAll)]
[Edit(PermissionLevel = SecurityPermissionLevels.AllowAll)]
[DtoContentView("list", IncludeByDefault = false)]
[DtoContentView("detail", IncludeByDefault = false)]
[DtoContentView("save", IncludeByDefault = false)]
[DtoActionDefaults(List = "list", Get = "detail", Save = "save", Count = "list")]
[DtoFlatten("ReportedBy.Company.Name", Name = "ReportedByCompanyName", ContentViews = "detail")]
public class ContentViewEntity
{
    [DtoIncludes("list,detail,save")]
    public int ContentViewEntityId { get; set; }

    [DtoIncludes("list,detail,save")]
    public string Name { get; set; }

    [DtoIncludes("detail,save")]
    public string Description { get; set; }

    public int? AssignedToId { get; set; }

    [DtoIncludes("list,detail")]
    [DtoReference]
    [ForeignKey(nameof(AssignedToId))]
    public Person AssignedTo { get; set; }

    public int? ReportedById { get; set; }

    [DtoIncludes("detail")]
    [ForeignKey(nameof(ReportedById))]
    public Person ReportedBy { get; set; }

    public string NeverMapped { get; set; }
}
