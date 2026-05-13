using IntelliTect.Coalesce.DataAnnotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext;

[Create(PermissionLevel = SecurityPermissionLevels.AllowAll)]
[Edit(PermissionLevel = SecurityPermissionLevels.AllowAll)]
[DtoContentView("list", IncludeByDefault = false)]
[DtoContentView("detail", IncludeByDefault = false)]
[DtoContentView("save", IncludeByDefault = false)]
[DtoActionDefaults(List = "list", Get = "detail", Save = "save", Count = "list", UseContentViewResponseTypes = true)]
[DtoDateTimeOptions(DtoDateTimeMode.Utc)]
[DtoFlatten("ReportedBy.Company.Name", Name = "ReportedByCompanyName", ContentViews = "detail")]
public class ContentViewEntity
{
    [DtoIncludes("list,detail,save")]
    public int ContentViewEntityId { get; set; }

    [DtoIncludes("list,detail,save")]
    public string Name { get; set; }

    [DtoIncludes("detail,save")]
    public string Description { get; set; }

    [DtoIncludes("list,detail")]
    public DateTime CreatedAt { get; set; }

    public int? AssignedToId { get; set; }

    [DtoIncludes("list,detail")]
    [DtoReference]
    [ForeignKey(nameof(AssignedToId))]
    public Person AssignedTo { get; set; }

    public int? ReportedById { get; set; }

    [DtoIncludes("detail")]
    [ForeignKey(nameof(ReportedById))]
    public Person ReportedBy { get; set; }

    [DtoIncludes("list,detail")]
    public ICollection<ContentViewEntityTagLink> Tags { get; set; } = new List<ContentViewEntityTagLink>();

    public string NeverMapped { get; set; }
}

[DtoContentView("list", IncludeByDefault = false)]
[DtoContentView("detail", IncludeByDefault = false)]
[DtoFlatten("Tag.Id", Name = "Id", ContentViews = "list,detail")]
[DtoFlatten("Tag.Name", Name = "Name", ContentViews = "list,detail")]
public class ContentViewEntityTagLink
{
    public int ContentViewEntityTagLinkId { get; set; }
    public int ContentViewEntityId { get; set; }
    public ContentViewEntity ContentViewEntity { get; set; }
    public int TagId { get; set; }
    public ContentViewTag Tag { get; set; }
}

public class ContentViewTag
{
    public int Id { get; set; }
    public string Name { get; set; }
}
