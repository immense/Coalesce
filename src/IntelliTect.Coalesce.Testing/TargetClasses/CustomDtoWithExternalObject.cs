using System.ComponentModel.DataAnnotations;
using IntelliTect.Coalesce.Testing.TargetClasses.TestDbContext;

#nullable enable

namespace IntelliTect.Coalesce.Testing.TargetClasses;

public class ExternalObjectWithoutListText
{
    public string? Value { get; set; }
    public NestedExternalObjectWithoutListText? Child { get; set; }
}

public class NestedExternalObjectWithoutListText
{
    public string? Value { get; set; }
}

[Coalesce]
public class CaseDtoWithExternalObject : IClassDto<Case, AppDbContext>
{
    [Key]
    public int CaseKey { get; set; }

    public string? Title { get; set; }

    public ExternalObjectWithoutListText? ExternalObject { get; set; }

    public void MapTo(Case obj, IMappingContext context)
    {
        obj.Title = Title;
    }

    public void MapFrom(Case obj, IMappingContext context, IncludeTree? tree = null)
    {
        CaseKey = obj.CaseKey;
        Title = obj.Title;
        ExternalObject = new ExternalObjectWithoutListText
        {
            Value = obj.Title,
            Child = new NestedExternalObjectWithoutListText
            {
                Value = obj.Title
            }
        };
    }
}
