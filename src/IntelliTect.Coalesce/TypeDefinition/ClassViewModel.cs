using IntelliTect.Coalesce.DataAnnotations;
using IntelliTect.Coalesce.Helpers.Search;
using IntelliTect.Coalesce.TypeUsage;
using IntelliTect.Coalesce.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace IntelliTect.Coalesce.TypeDefinition;

public abstract class ClassViewModel : IAttributeProvider
{
    protected IReadOnlyCollection<PropertyViewModel>? _Properties;
    protected IReadOnlyCollection<MethodViewModel>? _Methods;

    internal HashSet<ValueViewModel> Usages = new HashSet<ValueViewModel>();

    public ReflectionRepository? ReflectionRepository => Type.ReflectionRepository;

    /// <summary>
    /// Gets the custom attribute metadata for this class,
    /// based on assembly-level <see cref="DataAnnotations.CoalesceMetadataAttribute"/> declarations.
    /// </summary>
    public IEnumerable<CustomMetadataItem> GetCustomMetadata()
        => ReflectionRepository?.CustomMetadata.GetCustomMetadata(this, Type) ?? [];

    protected ClassViewModel(TypeViewModel type)
    {
        Type = type;
    }

    public abstract string Name { get; }
    public abstract string? Comment { get; }
    public TypeViewModel Type { get; protected set; }
    public abstract bool IsStatic { get; }
    public abstract bool IsRecord { get; }

    public abstract IEnumerable<LiteralViewModel> ClientConsts { get; }

    public string FullyQualifiedName => Type.FullyQualifiedName;


    /// <summary>
    /// Returns the name of the type to be used by the client, or in other cases
    /// where the type name should be overridable using the [Coalesce] attribute.
    /// This includes places where there could be name conflicts that should be resolvable
    /// by allowing the developer to override the type name for generation.
    /// </summary>
    public string ClientTypeName =>
        // Check for an override first
        this.GetAttributeValue<CoalesceAttribute>(a => a.ClientTypeName) ??
        // If no override, check for an interface service, and trim the conventional 'I' if found.
        (IsService && Type.IsInterface && Name[0] == 'I' && char.IsUpper(Name[1]) ? Name.Substring(1) : null) ??
        // Nothing special - just use the name.
        Name;

    public string ControllerName => ClientTypeName;

    public string ApiRouteControllerPart => ControllerName;

    public string ApiControllerClassName
    {
        get
        {
#pragma warning disable CS0612 // Type or member is obsolete
            var overrideName = this.GetAttributeValue<ControllerAttribute>(a => a.ApiControllerName);
            if (!string.IsNullOrWhiteSpace(overrideName)) return overrideName;

            var suffix = this.GetAttributeValue<ControllerAttribute>(a => a.ApiControllerSuffix) ?? "";
#pragma warning restore CS0612 // Type or member is obsolete

            return $"{ControllerName}Controller{suffix}";
        }
    }

    [Obsolete("This is governed exclusively via ControllerAttribute, an obsolete attribute. ")]
    public string ApiActionAccessModifier =>
        this.GetAttributeValue<ControllerAttribute, bool>(a => a.ApiActionsProtected) ?? false
        ? "protected"
        : "public";

    public string ParameterDtoTypeName => IsCustomDto ? FullyQualifiedName : $"{ClientTypeName}Parameter";

    public string ResponseDtoTypeName => IsCustomDto
        ? FullyQualifiedName
        : this.GetAttributeValue<CoalesceAttribute>(a => a.ResponseDtoClassName) ?? $"{ClientTypeName}Response";

    private string ResponseDtoTypeNameStem
    {
        get
        {
            var responseTypeName = ResponseDtoTypeName;
            return responseTypeName.EndsWith("Response", StringComparison.Ordinal)
                ? responseTypeName[..^"Response".Length]
                : responseTypeName;
        }
    }

    public ClassViewModel BaseViewModel => DtoBaseViewModel ?? this;

    public IEnumerable<ClassViewModel> ClientDerivedTypes => (ReflectionRepository?.ClientClasses
        .Where(cvm =>
        {
            var baseType = cvm.Type.BaseType;
            while (baseType != null)
            {
                if (baseType == Type) return true;
                baseType = baseType.BaseType;
            }
            return false;
        }) ?? []).OrderBy(cvm => cvm.Name);

    public IEnumerable<ClassViewModel> ClientBaseTypes
    {
        get
        {
            var baseType = Type.BaseType;
            while (baseType != null)
            {
                var cvm = baseType.ClassViewModel;
                if (cvm is not null && ReflectionRepository?.ClientClasses.Contains(cvm) == true)
                {
                    yield return cvm;
                }
                baseType = baseType.BaseType;
            }
        }
    }

    /// <summary>
    /// If this class implements IClassDto, return true.
    /// </summary>
    public bool IsCustomDto => Type.IsA(typeof(IClassDto<>));

    /// <summary>
    /// If this class is a DTO, return the ClassViewModel for the type that this DTO is based upon.
    /// </summary>
    public ClassViewModel? DtoBaseViewModel =>
        (
            Type.GenericArgumentsFor(typeof(IParameterDto<>)) ??
            Type.GenericArgumentsFor(typeof(IResponseDto<>))
        )?[0].ClassViewModel;

    /// <summary>
    /// Name of the ViewModelClass
    /// </summary>
    public string ViewModelClassName => ClientTypeName;

    [Obsolete("This is governed exclusively via ControllerAttribute, an obsolete attribute. ")]
    public bool ApiRouted => this.GetAttributeValue<ControllerAttribute, bool>(a => a.ApiRouted) ?? true;

    /// <summary>
    /// Name of the List ViewModelClass
    /// </summary>
    public string ListViewModelClassName => ClientTypeName + "List";

    public string SummaryViewModelClassName => ClientTypeName + "Summary";

    public string SummaryDtoTypeName => SummaryViewModelClassName + "Response";

    public bool IsService => this.HasAttribute<CoalesceAttribute>() && this.HasAttribute<ServiceAttribute>();
    public bool IsStandaloneEntity => this.HasAttribute<CoalesceAttribute>() && this.HasAttribute<StandaloneEntityAttribute>();

    public string ServiceClientClassName => ClientTypeName + "Client";

    #region Member Info - Properties & Methods

    protected abstract IReadOnlyCollection<PropertyViewModel> RawProperties(ClassViewModel effectiveParent);
    public abstract IReadOnlyCollection<MethodViewModel> Constructors { get; }
    protected abstract IReadOnlyCollection<MethodViewModel> RawMethods(ClassViewModel effectiveParent);
    protected abstract IReadOnlyCollection<TypeViewModel> RawNestedTypes { get; }

    /// <summary>
    /// All properties for the object.
    /// This collection is not filtered to only those properties which should be exposed to the client.
    /// </summary>
    /// <remarks>
    /// This collection is internal to prevent accidental exposing of properties that should not be exposed.
    /// </remarks>
    internal IReadOnlyCollection<PropertyViewModel> Properties
    {
        get
        {
            if (_Properties != null) return _Properties;

            var properties = new List<PropertyViewModel>();
            int count = 1;
            foreach (var prop in RawProperties(this))
            {
                if (properties.FirstOrDefault(f => f.Name == prop.Name) is { } existing)
                {
                    // Prefer the property declared closest to the effective type so hidden/new members
                    // on derived types win over the inherited member they're replacing.
                    var existingDistance = InheritanceDistance(existing.Parent);
                    var newDistance = InheritanceDistance(prop.Parent);
                    if (
                        newDistance < existingDistance ||
                        (
                            newDistance == existingDistance &&
                            existing.IsVirtual &&
                            !prop.IsVirtual
                        )
                    )
                    {
                        properties.Remove(existing);
                        prop.ClassFieldOrder = count;
                        properties.Add(prop);
                    }
                }
                else
                {
                    prop.ClassFieldOrder = count;
                    properties.Add(prop);
                }
                count++;
            }

            // Don't assign to _Properties until the end in order to avoid threading issues.
            // If _Properties were mutable, we could potentially have two threads attempting to build the same collection at once.
            return _Properties = properties.AsReadOnly();
        }
    }

    int InheritanceDistance(ClassViewModel candidate)
    {
        var distance = 0;
        for (var current = this; current is not null; current = current.Type.BaseType?.ClassViewModel)
        {
            if (current.Equals(candidate))
            {
                return distance;
            }

            distance++;
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Properties on the class that are permitted to be exposed to the client.
    /// </summary>
    public IEnumerable<PropertyViewModel> ClientProperties => Properties.Where(p => p.IsClientProperty);

    public IEnumerable<PropertyViewModel> DataSourceParameters => Properties
        .Where(p =>
            !p.IsInternalUse && p.HasPublicSetter && p.HasAttribute<CoalesceAttribute>()
            && p.PureType.TsTypeKind
                is not TypeDiscriminator.File
                and not TypeDiscriminator.Void
                and not TypeDiscriminator.Unknown
        );

    private IReadOnlyList<FlattenedResponsePropertyViewModel>? _flattenedResponseProperties;
    public IReadOnlyList<FlattenedResponsePropertyViewModel> FlattenedResponseProperties
        => _flattenedResponseProperties ??= FlattenedResponsePropertyViewModel.FromClass(this);

    private IReadOnlyDictionary<string, bool>? _dtoContentViews;
    public IReadOnlyDictionary<string, bool> DtoContentViews
        => _dtoContentViews ??= GetAttributes<DtoContentViewAttribute>()
            .Select(a => new
            {
                Name = a.GetValue(x => x.Name),
                IncludeByDefault = a.GetValue(x => x.IncludeByDefault) ?? true,
            })
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .GroupBy(a => a.Name!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().IncludeByDefault, StringComparer.Ordinal);

    public bool ShouldIncludeUnspecifiedPropertiesForContentView(string? contentView)
        => string.IsNullOrWhiteSpace(contentView)
            || !DtoContentViews.TryGetValue(contentView, out var includeByDefault)
            || includeByDefault;

    private IReadOnlyList<string>? _ownResponseContentViews;
    public IReadOnlyList<string> OwnResponseContentViews
        => _ownResponseContentViews ??= CollectResponseContentViews(this)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private IReadOnlyList<string>? _generatedResponseContentViews;
    public IReadOnlyList<string> GeneratedResponseContentViews
        => _generatedResponseContentViews ??= (ClientBaseTypes.FirstOrDefault()?.GeneratedResponseContentViews ?? [])
            .Concat(OwnResponseContentViews)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private DtoDateTimeMode? _responseDtoDateTimeMode;
    public DtoDateTimeMode ResponseDtoDateTimeMode
        => _responseDtoDateTimeMode ??=
            this.GetAttributeValue<DtoDateTimeOptionsAttribute, DtoDateTimeMode>(a => a.Mode)
            ?? Type.Assembly.GetAttributeValue<DtoDateTimeOptionsAttribute, DtoDateTimeMode>(a => a.Mode)
            ?? DtoDateTimeMode.Preserve;

    public string? DefaultGetDtoIncludes => this.GetAttributeValue<DtoActionDefaultsAttribute>(a => a.Get);
    public string? DefaultListDtoIncludes => this.GetAttributeValue<DtoActionDefaultsAttribute>(a => a.List);
    public string? DefaultCountDtoIncludes => this.GetAttributeValue<DtoActionDefaultsAttribute>(a => a.Count);
    public string? DefaultSaveDtoIncludes => this.GetAttributeValue<DtoActionDefaultsAttribute>(a => a.Save);
    public string? DefaultBulkSaveDtoIncludes => this.GetAttributeValue<DtoActionDefaultsAttribute>(a => a.BulkSave);
    public string? DefaultDeleteDtoIncludes => this.GetAttributeValue<DtoActionDefaultsAttribute>(a => a.Delete);
    public bool UseContentViewResponseTypes => this.GetAttributeValue<DtoActionDefaultsAttribute, bool>(a => a.UseContentViewResponseTypes) ?? false;

    public bool ShouldUseContentViewResponseType(string? contentView)
        => UseContentViewResponseTypes && !string.IsNullOrWhiteSpace(contentView);

    public bool HasResponseDtoTypeForContentView(string? contentView)
        => !string.IsNullOrWhiteSpace(contentView)
            && GeneratedResponseContentViews.Contains(contentView, StringComparer.Ordinal);

    public string ResponseDtoTypeNameForContentView(string contentView)
        => $"{ResponseDtoTypeNameStem}{GetContentViewResponseTypeSuffix(contentView)}Response";

    public string GetStandardActionResponseDtoTypeName(string? contentView)
        => ShouldUseContentViewResponseType(contentView)
            ? ResponseDtoTypeNameForContentView(contentView!)
            : ResponseDtoTypeName;

    private static IEnumerable<string> CollectResponseContentViews(ClassViewModel model)
        => model.DtoContentViews.Keys
            .Concat(new[]
            {
                model.DefaultGetDtoIncludes,
                model.DefaultListDtoIncludes,
                model.DefaultCountDtoIncludes,
                model.DefaultSaveDtoIncludes,
                model.DefaultBulkSaveDtoIncludes,
                model.DefaultDeleteDtoIncludes,
            }.OfType<string>().Where(v => !string.IsNullOrWhiteSpace(v)))
            .Concat(model.ClientProperties.SelectMany(p => p.DtoIncludes.Concat(p.DtoExcludes)))
            .Concat(model.FlattenedResponseProperties.SelectMany(p => p.ContentViews.Concat(p.ExcludedContentViews)))
            .Concat(model.SummaryProperties.SelectMany(p => p.ContentViews.Concat(p.ExcludedContentViews)))
            .Where(v => !string.IsNullOrWhiteSpace(v));

    private static string GetContentViewResponseTypeSuffix(string contentView)
    {
        var parts = Regex.Split(contentView, @"[^A-Za-z0-9]+")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.ToPascalCase());

        var suffix = string.Concat(parts);
        if (string.IsNullOrWhiteSpace(suffix))
        {
            suffix = "View";
        }

        return suffix.GetValidCSharpIdentifier();
    }

    /// <summary>
    /// List of method names that should not be exposed to the client.
    /// </summary>
    private readonly string[] excludedMethodNames = new[] {
        nameof(object.ToString),
        nameof(object.Equals),
        nameof(object.GetHashCode),
        nameof(object.GetType),
    };

    /// <summary>
    /// All the methods for the Class.
    /// This collection is NOT filtered to only client methods.
    /// It IS filtered by common methods that 
    /// </summary>
    /// <remarks>
    /// This collection is internal to prevent accidental exposing of methods that should not be exposed.
    /// </remarks>
    internal IReadOnlyCollection<MethodViewModel> Methods =>
        _Methods ?? (_Methods = RawMethods(this)
            .Where(m => !excludedMethodNames.Contains(m.Name)
                && (!IsCustomDto || (m.Name != nameof(IClassDto<object>.MapFrom) && m.Name != nameof(IClassDto<object>.MapTo))))
            .ToList().AsReadOnly());

    public IEnumerable<MethodViewModel> ClientMethods =>
        Methods.Where(m => m.IsClientMethod);

    public IEnumerable<MethodViewModel> KernelMethods =>
        Methods.Where(m => m.HasAttribute<SemanticKernelAttribute>());

    internal IEnumerable<TypeViewModel> ClientNestedTypes =>
        RawNestedTypes.Where(t => !t.IsInternalUse);

    public IEnumerable<ClassViewModel> ClientDataSources(ReflectionRepository repo) => repo
        .DataSources
        .Where(d => d.DeclaredFor.Equals(this))
        .Select(d => d.StrategyClass)
        .OrderBy(d => d.ClientTypeName);

    /// <summary>
    /// Returns a property matching the name if it exists.
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public PropertyViewModel? PropertyByName(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return Properties.FirstOrDefault(f => string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<SummaryPropertyViewModel>? _summaryProperties;
    public IReadOnlyList<SummaryPropertyViewModel> SummaryProperties
        => _summaryProperties ??= SummaryPropertyViewModel.FromClass(this);

    private bool? _shouldGenerateSummaryDto;
    public bool ShouldGenerateSummaryDto
        => _shouldGenerateSummaryDto ??= ReflectionRepository?.ClientClasses
            .SelectMany(c => c.ClientProperties)
            .Any(p => p.UsesDtoReferenceSummary && p.Object == this) == true;

    /// <summary>
    /// Returns a client method matching the name if it exists.
    /// Non-client-exposed methods will not be returned.
    /// </summary>
    /// <param name="name">The name of the method to look for. 
    /// Coalesce doesn't support exposing method overloads - 
    /// in the case of overloads, the first matching method will be returned.</param>
    /// <param name="isStatic">Whether to look for a static or instance method. 
    /// If null, the first match will be returned.</param>
    /// <returns></returns>
    public MethodViewModel? MethodByName(string name, bool? isStatic = null)
    {
        return ClientMethods.FirstOrDefault(f =>
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)
            && (isStatic == null || f.IsStatic == isStatic));
    }

    /// <summary>
    /// Returns a property matching the name if it exists.
    /// </summary>
    /// <param name="propertySelector"></param>
    /// <returns></returns>
    public PropertyViewModel PropertyBySelector<T, TProperty>(Expression<Func<T, TProperty>> propertySelector)
    {
        PropertyInfo propInfo = propertySelector.GetExpressedProperty();
        if (propInfo == null) throw new ArgumentException("Could not find property");
        return PropertyByName(propInfo.Name)!;
    }

    #endregion

    #region Searching/Sorting

    private ICollection<OrderByInformation>? _defaultOrderBy;
    /// <summary>
    /// Gets a sorted list of the default order by attributes for the class.
    /// </summary>
    public ICollection<OrderByInformation> DefaultOrderBy
    {
        get
        {
            if (_defaultOrderBy is not null) return _defaultOrderBy;

            var result = new List<OrderByInformation>();
            foreach (var p in Properties
                .Select(p => new { Prop = p, p.DefaultOrderBy })
                .Where(p => p.DefaultOrderBy != null)
                .OrderBy(p => p.DefaultOrderBy!.FieldOrder)
            )
            {
                var prop = p.Prop;
                var orderInfo = p.DefaultOrderBy!;
                if (
                    // Ordering by a POCO prop
                    prop.Type.ClassViewModel != null &&
                    // The POCO prop isn't already opinionated about its
                    // nested prop (via DefaultOrderByAttribute.FieldName)
                    orderInfo.Properties.Count == 1 &&
                    // The target type *does* have specific fields that it likes to order by
                    prop.Type.ClassViewModel.DefaultOrderBy.Any()
                )
                {
                    foreach (var nestedOrderBy in prop.Type.ClassViewModel.DefaultOrderBy.OrderBy(p => p.FieldOrder))
                    {
                        result.Add(new OrderByInformation()
                        {
                            Properties = new[] { prop }.Concat(nestedOrderBy.Properties).ToList(),
                            FieldOrder = result.Count + 1,
                            OrderByDirection = nestedOrderBy.OrderByDirection
                        });
                    }
                }
                else
                {
                    result.Add(orderInfo);
                }
            }

            if (result.Count > 0)
            {
                return _defaultOrderBy = result;
            }

            // Nothing found, order by ListText and then ID.
            var nameProp = PropertyByName("Name");
            if (nameProp?.IsDbMapped == true && nameProp.IsClientProperty
                && nameProp.GetAttributeValue<DefaultOrderByAttribute, bool>(a => a.Suppress) != true)
            {
                result.Add(new OrderByInformation()
                {
                    Properties = [nameProp],
                    OrderByDirection = DefaultOrderByAttribute.OrderByDirections.Ascending,
                    FieldOrder = 1
                });
            }
            else if (PrimaryKey != null
                && PrimaryKey.GetAttributeValue<DefaultOrderByAttribute, bool>(a => a.Suppress) != true)
            {
                result.Add(new OrderByInformation()
                {
                    Properties = [PrimaryKey],
                    OrderByDirection = DefaultOrderByAttribute.OrderByDirections.Ascending,
                    FieldOrder = 1
                });
            }
            return _defaultOrderBy = result;
        }
    }

    /// <summary>
    /// Gets a list of properties that should be searched for text.
    /// It will first look for any properties that are decorated with the Search attribute.
    /// If it doesn't find those, it will look for a property called Name or {Class}Name.
    /// If none of those are found, it will result in returning the property that is the primary key for the object
    /// </summary>
    public IEnumerable<SearchableProperty> SearchProperties(ClassViewModel? rootModel = null, int depth = 0, int maxDepth = 2)
    {
        // Only go down three levels.
        if (depth == 3) yield break;

        var searchProperties = Properties.Where(f => f.IsSearchable(rootModel)).ToList();
        if (searchProperties.Count > 0)
        {
            // Process these items to make sure we have things we can search on.
            foreach (var property in searchProperties)
            {
                // Get all the child items
                foreach (var searchProperty in property.SearchProperties(rootModel, depth, maxDepth))
                {
                    yield return searchProperty;
                }
            }
            yield break;
        }

        var defaultProp = new[]
            {
                PropertyByName("Name"),
                Properties.FirstOrDefault(p => p.Name == $"{p.Parent.Name}Name"),
                PrimaryKey
            }
            .Where(p => p != null && p.IsClientProperty && !p.HasNotMapped)
            .Select(p => new SearchableValueProperty(p!))
            .FirstOrDefault();

        if (defaultProp != null)
        {
            yield return defaultProp;
        }
    }

    #endregion

    /// <summary>
    /// Returns the property ID field.
    /// </summary>
    public PropertyViewModel? PrimaryKey => Properties.FirstOrDefault(f => f.IsPrimaryKey);

    /// <summary>
    /// Use the ListText Attribute first, then Name and then ID.
    /// </summary>
    public PropertyViewModel? ListTextProperty =>
        Type.IsA(typeof(IDataSource<>)) ? null :
        ClientProperties.FirstOrDefault(f => f.IsListText) ??
        ClientProperties.FirstOrDefault(f => f.Name == "Name") ??
        PrimaryKey;

    [Obsolete("The logic in this property is flawed and does not consider that a single entity can participate in multiple relationships. It will be removed in a future version.")]
    public bool IsOneToOne => PrimaryKey?.IsForeignKey ?? false;

#pragma warning disable CS0618 // Type or member is obsolete
    public bool WillCreateApiController =>
        this.GetAttributeValue<CreateControllerAttribute, bool>(a => a.WillCreateApi) ?? true;
#pragma warning restore CS0618 // Type or member is obsolete

    /// <summary>
    /// Returns a human-readable string that represents the name of this type to the client.
    /// </summary>
    public string DisplayName =>
        this.GetAttributeValue<DisplayNameAttribute>(a => a.DisplayName) ??
        this.GetAttributeValue<DisplayAttribute>(a => a.Name) ??
        ClientTypeName.ToProperCase();

    /// <summary>
    /// Returns the description from the DisplayAttribute or DescriptionAttribute, if present.
    /// </summary>
    public virtual string? Description =>
        this.GetAttributeValue<DisplayAttribute>(a => a.Description) ??
        this.GetAttributeValue<DescriptionAttribute>(a => a.Description);

    public bool IsDbMappedType => DbContext != null;

    /// <summary>
    /// True if the type has a DbSet property on any discovered DbContext types.
    /// </summary>
    public bool HasDbSet => DbContextUsage != null;

    /// <summary>
    /// Metadata about the usage of this type in a <see cref="DbSet{TEntity}"/> on a <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
    /// </summary>
    public EntityTypeUsage? DbContextUsage => ReflectionRepository?.EntityUsages[this].FirstOrDefault();

    /// <summary>
    /// The DbContext associated with the type. 
    /// For entities, the context that owns the DbSet by which the entity was discovered.
    /// For DTOs, the context that owns the underlying entity, or the explicitly provided context.
    /// </summary>
    public ClassViewModel? DbContext =>
        DbContextUsage?.Context.ClassViewModel
        ?? Type.GenericArgumentsFor(typeof(IClassDto<,>))?[1].ClassViewModel
        ?? DtoBaseViewModel?.DbContext;

    private ClassSecurityInfo? _securityInfo;

    public ClassSecurityInfo SecurityInfo => _securityInfo ??= new ClassSecurityInfo(this);

    public bool IsDefaultDataSource => this.HasAttribute<DefaultDataSourceAttribute>();

    public IEnumerable<AttributeViewModel<TAttribute>> GetAttributes<TAttribute>()
        where TAttribute : Attribute
        => Type.GetAttributes<TAttribute>();

    public override string ToString() => FullyQualifiedName;

    public override bool Equals(object? obj) =>
        ReferenceEquals(this, obj)
        || (obj is ClassViewModel that && this.Type.Equals(that.Type));

    public override int GetHashCode() => this.Type.GetHashCode();

    public static bool operator ==(ClassViewModel? lhs, ClassViewModel? rhs)
    {
        if (lhs is null)
        {
            return rhs is null;
        }

        return lhs.Equals(rhs);
    }

    public static bool operator !=(ClassViewModel? lhs, ClassViewModel? rhs)
    {
        return !(lhs == rhs);
    }


    internal void ClearEntityUsageCache()
    {
        foreach (var prop in _Properties ?? [])
        {
            prop.ClearEntityUsageCache();
        }
    }
}
