#nullable enable

using IntelliTect.Coalesce.CodeGeneration.Analysis.Base;
using IntelliTect.Coalesce.CodeGeneration.Analysis.Roslyn;
using IntelliTect.Coalesce.DataAnnotations;
using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.TypeDefinition;
using IntelliTect.Coalesce.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace IntelliTect.Coalesce.CodeGeneration.Api.Generators;

public class GeneratedContracts : CompositeGenerator<ReflectionRepository>
{
    private const string ShapeAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractShapeAttribute";
    private const string AliasAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractAliasAttribute";
    private const string DtoSourceAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.DtoSourceAttribute";
    private const string NullableAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractNullableAttribute";
    private const string NonNullableAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractNonNullableAttribute";
    private const int ExplicitPolicy = 0;
    private const int PublicScalarPropertiesPolicy = 1;
    private const int ClassOutputKind = 0;
    private const int InterfaceOutputKind = 1;
    internal const string GeneratedContractsRelativePath = "Generated/Contracts";

    public GeneratedContracts(CompositeGeneratorServices services) : base(services)
    {
    }

    private GenerationContext GenerationContext => Services.GenerationContext;

    public override IEnumerable<IGenerator> GetGenerators()
    {
        var discoveredTypes = GetDiscoveredTypes();
        if (discoveredTypes.Count == 0)
        {
            yield break;
        }

        var projectLookup = BuildProjectDirectoryLookup(GenerationContext);
        var emittedTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sourceType in discoveredTypes)
        {
            foreach (var shape in GetShapes(sourceType))
            {
                var projectDirectory = ResolveProjectDirectory(projectLookup, shape, sourceType);
                var typeKey = $"{shape.TargetNamespace}.{shape.TypeName}";
                if (!emittedTypes.Add(typeKey))
                {
                    throw new InvalidOperationException(
                        $"Generated contract '{typeKey}' is declared more than once. " +
                        $"The duplicate declaration was found on '{sourceType.ToDisplayString()}'.");
                }

                yield return Generator<GeneratedContractFile>()
                    .WithModel(new GeneratedContractFileModel(
                        shape,
                        ResolveProperties(sourceType, shape),
                        sourceType.ToDisplayString()))
                    .WithOutputPath(Path.Combine(projectDirectory, GeneratedContractsRelativePath, $"{shape.TypeName}.g.cs"));
            }
        }
    }

    public override IEnumerable<ICleaner> GetCleaners()
    {
        foreach (var projectDirectory in BuildProjectDirectoryLookup(GenerationContext).Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Cleaner<DirectoryCleaner>()
                .WithDepth(SearchOption.AllDirectories)
                .Configure(Path.Combine(projectDirectory, GeneratedContractsRelativePath));
        }
    }

    private IReadOnlyList<INamedTypeSymbol> GetDiscoveredTypes()
    {
        var discoveryProjects = new[] { GenerationContext.DataProject, GenerationContext.WebProject }
            .OfType<RoslynProjectContext>()
            .GroupBy(project => project.ProjectFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Project: group.First(), Locator: (RoslynTypeLocator)group.First().TypeLocator))
            .ToList();

        if (discoveryProjects.Count == 0)
        {
            return [];
        }

        var mergedTypes = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var (_, locator) in discoveryProjects)
        {
            foreach (var type in locator.GetAllTypes(includeReferencedAssemblies: true))
            {
                var key = type.ToDisplayString(SymbolTypeViewModel.DefaultDisplayFormat);
                mergedTypes.TryAdd(key, type);
            }
        }

        var masterLocator = discoveryProjects[^1].Locator;

        return mergedTypes.Values
            .Select(type =>
            {
                var key = type.ToDisplayString(SymbolTypeViewModel.DefaultDisplayFormat);
                return masterLocator.FindTypeByMetadataName(key) ?? type;
            })
            .Where(type => type.GetAttributes().Any(IsShapeAttribute))
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> BuildProjectDirectoryLookup(GenerationContext generationContext)
    {
        var projectLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        var visitedProjectFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddProject(generationContext.DataProject);
        AddProject(generationContext.WebProject);
        return projectLookup;

        void AddProject(ProjectContext? projectContext)
        {
            if (projectContext?.ProjectFilePath is not { Length: > 0 } projectFilePath)
            {
                return;
            }

            ParseProject(projectFilePath);
        }

        void ParseProject(string projectFilePath)
        {
            var fullPath = Path.GetFullPath(projectFilePath);
            if (!visitedProjectFiles.Add(fullPath) || !File.Exists(fullPath))
            {
                return;
            }

            var projectDirectory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException($"Unable to determine project directory for '{fullPath}'.");

            var projectDocument = XDocument.Load(fullPath);
            var assemblyName = projectDocument
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "AssemblyName")
                ?.Value
                ?.Trim();

            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                assemblyName = Path.GetFileNameWithoutExtension(fullPath);
            }

            projectLookup[assemblyName] = projectDirectory;

            foreach (var projectReference in projectDocument
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include)))
            {
                var normalizedReference = projectReference!
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                ParseProject(Path.Combine(projectDirectory, normalizedReference));
            }
        }
    }

    private static string ResolveProjectDirectory(
        IReadOnlyDictionary<string, string> projectLookup,
        ContractShape shape,
        INamedTypeSymbol sourceType)
    {
        if (projectLookup.TryGetValue(shape.TargetAssemblyName, out var projectDirectory))
        {
            return projectDirectory;
        }

        throw new InvalidOperationException(
            $"Unable to resolve generated contract target assembly '{shape.TargetAssemblyName}' " +
            $"for shape '{shape.TypeName}' on '{sourceType.ToDisplayString()}'.");
    }

    internal static IEnumerable<ContractShape> GetShapes(INamedTypeSymbol sourceType)
        => sourceType.GetAttributes()
            .Where(IsShapeAttribute)
            .Select(ParseShape)
            .Where(shape => shape is not null)
            .Cast<ContractShape>();

    private static bool IsShapeAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() == ShapeAttributeMetadataName;

    private static ContractShape? ParseShape(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length < 5)
        {
            return null;
        }

        return new ContractShape(
            (string?)attribute.ConstructorArguments[0].Value ?? string.Empty,
            Convert.ToInt32(attribute.ConstructorArguments[1].Value),
            (string?)attribute.ConstructorArguments[2].Value ?? string.Empty,
            (string?)attribute.ConstructorArguments[3].Value ?? string.Empty,
            (string?)attribute.ConstructorArguments[4].Value ?? string.Empty,
            GetInt(attribute, nameof(GeneratedContractShapeAttributePlaceholder.Policy)),
            GetStringArray(attribute, nameof(GeneratedContractShapeAttributePlaceholder.Members)),
            GetStringArray(attribute, nameof(GeneratedContractShapeAttributePlaceholder.ExcludedMembers)),
            GetStringArray(attribute, nameof(GeneratedContractShapeAttributePlaceholder.Implements)),
            GetTypeArray(attribute, nameof(GeneratedContractShapeAttributePlaceholder.IncludedPropertyAttributes)),
            GetBool(attribute, nameof(GeneratedContractShapeAttributePlaceholder.SettableProperties)),
            GetInt(attribute, nameof(GeneratedContractShapeAttributePlaceholder.NullabilityTransform)));
    }

    private static int GetInt(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is not null)
            {
                return Convert.ToInt32(argument.Value.Value);
            }
        }

        return 0;
    }

    private static IReadOnlyList<string> GetStringArray(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key != name || argument.Value.Kind != TypedConstantKind.Array)
            {
                continue;
            }

            return argument.Value.Values
                .Where(value => value.Value is string)
                .Select(value => (string)value.Value!)
                .ToArray();
        }

        return [];
    }

    private static IReadOnlyList<string> GetTypeArray(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key != name || argument.Value.Kind != TypedConstantKind.Array)
            {
                continue;
            }

            return argument.Value.Values
                .Where(value => value.Kind == TypedConstantKind.Type && value.Value is ITypeSymbol)
                .Select(value => ((ITypeSymbol)value.Value!).ToDisplayString())
                .ToArray();
        }

        return [];
    }

    private static bool GetBool(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool value)
            {
                return value;
            }
        }

        return false;
    }

    private static string? GetString(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is string value)
            {
                return value;
            }
        }

        return null;
    }

    internal static IReadOnlyList<ContractPropertyModel> ResolveProperties(INamedTypeSymbol sourceType, ContractShape shape)
    {
        var memberNames = ResolveMembers(sourceType, shape);
        if (memberNames.Count == 0)
        {
            if (shape.OutputKind == InterfaceOutputKind)
            {
                return [];
            }

            throw new InvalidOperationException(
                $"Generated contract '{shape.TypeName}' on '{sourceType.ToDisplayString()}' must declare at least one member.");
        }

        var properties = new List<ContractPropertyModel>(memberNames.Count);
        foreach (var memberName in memberNames)
        {
            var property = FindProperty(sourceType, memberName)
                ?? throw new InvalidOperationException(
                    $"Generated contract '{shape.TypeName}' on '{sourceType.ToDisplayString()}' references missing property '{memberName}'.");

            properties.Add(new ContractPropertyModel(
                GetAlias(property, shape.ShapeName) ?? property.Name,
                property,
                ShouldForceNullable(property, shape),
                HasAttribute(property, NonNullableAttributeMetadataName, shape.ShapeName),
                GetDtoSource(property),
                GetIncludedPropertyAttributes(property, shape)));
        }

        return properties;
    }

    private static IReadOnlyList<string> ResolveMembers(INamedTypeSymbol sourceType, ContractShape shape)
    {
        if (shape.Members.Count > 0)
        {
            return shape.Members;
        }

        if (shape.Policy == PublicScalarPropertiesPolicy)
        {
            var excludedMembers = new HashSet<string>(shape.ExcludedMembers, StringComparer.Ordinal);
            return EnumeratePolicyProperties(sourceType)
                .Where(property => !excludedMembers.Contains(property.Name))
                .Select(property => property.Name)
                .ToArray();
        }

        if (shape.Policy == ExplicitPolicy)
        {
            return [];
        }

        throw new InvalidOperationException(
            $"Generated contract '{shape.TypeName}' on '{sourceType.ToDisplayString()}' uses unknown policy value '{shape.Policy}'.");
    }

    private static IEnumerable<IPropertySymbol> EnumeratePolicyProperties(INamedTypeSymbol sourceType)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = sourceType; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (!seen.Add(property.Name) || !IsPolicyProperty(property))
                {
                    continue;
                }

                yield return property;
            }
        }
    }

    private static bool IsPolicyProperty(IPropertySymbol property)
        => !property.IsStatic
           && property.Parameters.Length == 0
           && property.DeclaredAccessibility == Accessibility.Public
           && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
           && property.SetMethod?.DeclaredAccessibility == Accessibility.Public
           && IsScalarLikeType(property.Type);

    private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string memberName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetMembers(memberName).OfType<IPropertySymbol>().FirstOrDefault(prop =>
                !prop.IsStatic &&
                prop.DeclaredAccessibility == Accessibility.Public &&
                prop.Parameters.Length == 0);

            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static string? GetAlias(IPropertySymbol property, string shapeName)
        => property.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == AliasAttributeMetadataName)
            .Where(attribute => string.Equals((string?)attribute.ConstructorArguments[0].Value, shapeName, StringComparison.Ordinal))
            .Select(attribute => (string?)attribute.ConstructorArguments[1].Value)
            .FirstOrDefault(alias => !string.IsNullOrWhiteSpace(alias));

    private static DtoSourceMetadata? GetDtoSource(IPropertySymbol property)
    {
        var attribute = property.GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == DtoSourceAttributeMetadataName);

        if (attribute is null || attribute.ConstructorArguments.Length == 0)
        {
            return null;
        }

        var path = (string?)attribute.ConstructorArguments[0].Value;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new DtoSourceMetadata(
            path,
            GetString(attribute, nameof(DtoSourceAttribute.OrderBy)),
            GetInt(attribute, nameof(DtoSourceAttribute.OrderByDirection)));
    }

    private static IReadOnlyList<AttributeData> GetIncludedPropertyAttributes(IPropertySymbol property, ContractShape shape)
    {
        if (shape.IncludedPropertyAttributes.Count == 0)
        {
            return [];
        }

        var includedAttributeTypes = new HashSet<string>(shape.IncludedPropertyAttributes, StringComparer.Ordinal);
        return property.GetAttributes()
            .Where(attribute => attribute.AttributeClass is not null)
            .Where(attribute => includedAttributeTypes.Contains(attribute.AttributeClass!.ToDisplayString()))
            .ToArray();
    }

    private static bool HasAttribute(IPropertySymbol property, string attributeMetadataName, string shapeName)
        => property.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == attributeMetadataName &&
            string.Equals((string?)attribute.ConstructorArguments[0].Value, shapeName, StringComparison.Ordinal));

    private static bool ShouldForceNullable(IPropertySymbol property, ContractShape shape)
    {
        if (HasAttribute(property, NonNullableAttributeMetadataName, shape.ShapeName))
        {
            return false;
        }

        if (HasAttribute(property, NullableAttributeMetadataName, shape.ShapeName))
        {
            return true;
        }

        var transform = (GeneratedContractNullabilityTransform)shape.NullabilityTransform;
        if (transform == GeneratedContractNullabilityTransform.None)
        {
            return false;
        }

        return (transform.HasFlag(GeneratedContractNullabilityTransform.NullableReferenceTypes) && property.Type.IsReferenceType)
            || (transform.HasFlag(GeneratedContractNullabilityTransform.NullableValueTypes) && IsNonNullableValueType(property.Type));
    }

    private static bool IsScalarLikeType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        if (type.SpecialType is
            SpecialType.System_Boolean or
            SpecialType.System_Byte or
            SpecialType.System_Char or
            SpecialType.System_Decimal or
            SpecialType.System_Double or
            SpecialType.System_Int16 or
            SpecialType.System_Int32 or
            SpecialType.System_Int64 or
            SpecialType.System_SByte or
            SpecialType.System_Single or
            SpecialType.System_String or
            SpecialType.System_UInt16 or
            SpecialType.System_UInt32 or
            SpecialType.System_UInt64)
        {
            return true;
        }

        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            return IsScalarLikeType(named.TypeArguments[0]);
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            return IsScalarLikeType(arrayType.ElementType);
        }

        if (TryGetCollectionElementType(type, out var elementType))
        {
            return IsScalarLikeType(elementType);
        }

        var fullyQualifiedName = type.ToDisplayString();
        return fullyQualifiedName is
            "System.Guid" or
            "System.DateTime" or
            "System.DateTimeOffset" or
            "System.TimeSpan" or
            "System.DateOnly" or
            "System.TimeOnly" or
            "System.Text.Json.JsonElement";
    }

    private static bool TryGetCollectionElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        elementType = null!;

        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        var enumerableInterface = type.AllInterfaces.FirstOrDefault(interfaceType =>
            interfaceType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>");

        if (enumerableInterface?.TypeArguments.Length == 1)
        {
            elementType = enumerableInterface.TypeArguments[0];
            return true;
        }

        return false;
    }

    private static class GeneratedContractShapeAttributePlaceholder
    {
        public static int Policy { get; set; }
        public static string[] Members { get; set; } = [];
        public static string[] ExcludedMembers { get; set; } = [];
        public static string[] Implements { get; set; } = [];
        public static Type[] IncludedPropertyAttributes { get; set; } = [];
        public static bool SettableProperties { get; set; }
        public static int NullabilityTransform { get; set; }
    }

    internal sealed record ContractShape(
        string ShapeName,
        int OutputKind,
        string TargetAssemblyName,
        string TargetNamespace,
        string TypeName,
        int Policy,
        IReadOnlyList<string> Members,
        IReadOnlyList<string> ExcludedMembers,
        IReadOnlyList<string> Implements,
        IReadOnlyList<string> IncludedPropertyAttributes,
        bool SettableProperties,
        int NullabilityTransform);

    internal sealed record GeneratedContractFileModel(
        ContractShape Shape,
        IReadOnlyList<ContractPropertyModel> Properties,
        string SourceTypeName);

    internal sealed record ContractPropertyModel(
        string Name,
        IPropertySymbol Property,
        bool ForceNullable,
        bool ForceNonNullable,
        DtoSourceMetadata? DtoSource,
        IReadOnlyList<AttributeData> IncludedPropertyAttributes);

    internal sealed record DtoSourceMetadata(
        string Path,
        string? OrderBy,
        int OrderByDirection);

    private static bool IsNonNullableValueType(ITypeSymbol type)
    {
        if (!type.IsValueType)
        {
            return false;
        }

        return type is not INamedTypeSymbol named
            || named.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
    }
}

internal sealed class GeneratedContractFile : StringBuilderCSharpGenerator<GeneratedContracts.GeneratedContractFileModel>
{
    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public GeneratedContractFile(GeneratorServices services) : base(services)
    {
    }

    public override void BuildOutput(CSharpCodeBuilder b)
        => BuildOutput(b, Model);

    internal static SyntaxTree CreateSyntaxTree(
        GeneratedContracts.GeneratedContractFileModel model,
        CSharpParseOptions? parseOptions = null,
        string? path = null)
        => CSharpSyntaxTree.ParseText(
            SourceText.From(Render(model)),
            parseOptions ?? CSharpParseOptions.Default,
            path ?? string.Empty);

    internal static string Render(
        GeneratedContracts.GeneratedContractFileModel model,
        int indentationSize = 4)
    {
        var b = new CSharpCodeBuilder();
        BuildOutput(b, model);
        var output = b.ToString();

        var syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(output));
        var root = syntaxTree.GetRoot();

        using var workspace = new AdhocWorkspace();
        var options = workspace.Options
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, Environment.NewLine)
            .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, false)
            .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, indentationSize)
            .WithChangedOption(FormattingOptions.SmartIndent, LanguageNames.CSharp, FormattingOptions.IndentStyle.Smart)
            .WithChangedOption(CSharpFormattingOptions.WrappingKeepStatementsOnSingleLine, true);

        root = Formatter.Format(root, workspace, options);
        return root.ToFullString();
    }

    private static void BuildOutput(CSharpCodeBuilder b, GeneratedContracts.GeneratedContractFileModel model)
    {
        b.Line("// <auto-generated />");
        b.Line("#nullable enable");
        b.Line();
        b.Line($"namespace {model.Shape.TargetNamespace};");
        b.Line();

        var declarationKind = model.Shape.OutputKind == 1 ? "interface" : "partial class";
        var implements = model.Shape.Implements.Count > 0
            ? " : " + string.Join(", ", model.Shape.Implements.Select(NormalizeTypeName))
            : string.Empty;

        using (b.Block($"public {declarationKind} {model.Shape.TypeName}{implements}"))
        {
            foreach (var property in model.Properties)
            {
                foreach (var line in BuildPropertyLines(model, property))
                {
                    b.Line(line);
                }
            }
        }
    }

    private static IEnumerable<string> BuildPropertyLines(
        GeneratedContracts.GeneratedContractFileModel model,
        GeneratedContracts.ContractPropertyModel property)
    {
        foreach (var attributeLine in BuildPropertyAttributeLines(property))
        {
            yield return attributeLine;
        }

        var typeName = GetTypeName(property);
        if (model.Shape.OutputKind == 1)
        {
            var accessor = model.Shape.SettableProperties ? "{ get; set; }" : "{ get; }";
            yield return $"{typeName} {property.Name} {accessor}";
            yield break;
        }

        var required = NeedsRequiredKeyword(property) ? "required " : string.Empty;
        var initializer = HasCollectionInitializer(property) ? " = [];" : string.Empty;
        yield return $"public {required}{typeName} {property.Name} {{ get; set; }}{initializer}";
    }

    private static IEnumerable<string> BuildPropertyAttributeLines(GeneratedContracts.ContractPropertyModel property)
    {
        if (property.DtoSource is { } dtoSource)
        {
            yield return BuildDtoSourceAttribute(dtoSource);
        }

        foreach (var attribute in property.IncludedPropertyAttributes)
        {
            yield return BuildAttribute(attribute);
        }
    }

    private static string BuildDtoSourceAttribute(GeneratedContracts.DtoSourceMetadata dtoSource)
    {
        var args = new List<string>
        {
            SymbolDisplay.FormatLiteral(dtoSource.Path, quote: true)
        };

        if (!string.IsNullOrWhiteSpace(dtoSource.OrderBy))
        {
            args.Add($"OrderBy = {SymbolDisplay.FormatLiteral(dtoSource.OrderBy, quote: true)}");
        }

        if (dtoSource.OrderByDirection != 0)
        {
            var direction = dtoSource.OrderByDirection == 1 ? "Descending" : "Ascending";
            args.Add(
                "OrderByDirection = " +
                $"global::IntelliTect.Coalesce.DataAnnotations.DefaultOrderByAttribute.OrderByDirections.{direction}");
        }

        return $"[global::IntelliTect.Coalesce.DataAnnotations.DtoSource({string.Join(", ", args)})]";
    }

    private static string BuildAttribute(AttributeData attribute)
    {
        var typeName = attribute.AttributeClass?.ToDisplayString(TypeDisplayFormat)
            ?? throw new InvalidOperationException("Unable to resolve generated contract attribute type.");

        var arguments = attribute.ConstructorArguments
            .Select(FormatAttributeArgument)
            .Concat(attribute.NamedArguments.Select(argument => $"{argument.Key} = {FormatAttributeArgument(argument.Value)}"))
            .ToArray();

        return arguments.Length == 0
            ? $"[{typeName}]"
            : $"[{typeName}({string.Join(", ", arguments)})]";
    }

    private static string FormatAttributeArgument(TypedConstant value)
    {
        if (value.IsNull)
        {
            return "null";
        }

        if (value.Kind == TypedConstantKind.Array)
        {
            var elementTypeName = value.Type is IArrayTypeSymbol arrayType
                ? arrayType.ElementType.ToDisplayString(TypeDisplayFormat)
                : "object";

            return $"new {elementTypeName}[] {{ {string.Join(", ", value.Values.Select(FormatAttributeArgument))} }}";
        }

        if (value.Kind == TypedConstantKind.Type && value.Value is ITypeSymbol typeSymbol)
        {
            return $"typeof({typeSymbol.ToDisplayString(TypeDisplayFormat)})";
        }

        if (value.Type?.TypeKind == TypeKind.Enum)
        {
            return FormatEnumAttributeArgument(value);
        }

        return FormatPrimitiveAttributeArgument(value.Value!);
    }

    private static string FormatEnumAttributeArgument(TypedConstant value)
    {
        if (value.Type is not INamedTypeSymbol enumType)
        {
            return FormatPrimitiveAttributeArgument(value.Value!);
        }

        var namedMember = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, value.Value));

        if (namedMember is not null)
        {
            return $"{enumType.ToDisplayString(TypeDisplayFormat)}.{namedMember.Name}";
        }

        return $"({enumType.ToDisplayString(TypeDisplayFormat)}){FormatPrimitiveAttributeArgument(value.Value!)}";
    }

    private static string FormatPrimitiveAttributeArgument(object value)
        => value switch
        {
            string stringValue => SymbolDisplay.FormatLiteral(stringValue, quote: true),
            char charValue => SymbolDisplay.FormatLiteral(charValue, quote: true),
            bool boolValue => boolValue ? "true" : "false",
            float floatValue when float.IsNaN(floatValue) => "global::System.Single.NaN",
            float floatValue when float.IsPositiveInfinity(floatValue) => "global::System.Single.PositiveInfinity",
            float floatValue when float.IsNegativeInfinity(floatValue) => "global::System.Single.NegativeInfinity",
            float floatValue => floatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "F",
            double doubleValue when double.IsNaN(doubleValue) => "global::System.Double.NaN",
            double doubleValue when double.IsPositiveInfinity(doubleValue) => "global::System.Double.PositiveInfinity",
            double doubleValue when double.IsNegativeInfinity(doubleValue) => "global::System.Double.NegativeInfinity",
            double doubleValue => doubleValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "D",
            decimal decimalValue => decimalValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "M",
            long longValue => longValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            ulong ulongValue => ulongValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL",
            uint uintValue => uintValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "U",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException($"Unable to format generated contract attribute argument '{value}'.")
        };

    private static string GetTypeName(GeneratedContracts.ContractPropertyModel property)
    {
        var display = property.Property.Type.ToDisplayString(TypeDisplayFormat);
        if (property.ForceNullable)
        {
            return MakeNullable(display, property.Property.Type);
        }

        if (property.ForceNonNullable)
        {
            return MakeNonNullable(display, property.Property.Type);
        }

        return display;
    }

    private static string MakeNullable(string display, ITypeSymbol type)
    {
        if (display.EndsWith("?", StringComparison.Ordinal))
        {
            return display;
        }

        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return display;
        }

        return display + "?";
    }

    private static string MakeNonNullable(string display, ITypeSymbol type)
    {
        if (display.EndsWith("?", StringComparison.Ordinal))
        {
            return display[..^1];
        }

        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            return named.TypeArguments[0].ToDisplayString(TypeDisplayFormat);
        }

        return display;
    }

    private static bool NeedsRequiredKeyword(GeneratedContracts.ContractPropertyModel property)
    {
        if (property.ForceNullable || HasCollectionInitializer(property))
        {
            return false;
        }

        return property.Property.Type.IsReferenceType &&
               property.Property.NullableAnnotation != NullableAnnotation.Annotated;
    }

    private static bool HasCollectionInitializer(GeneratedContracts.ContractPropertyModel property)
    {
        if (property.ForceNullable)
        {
            return false;
        }

        return IsCollectionType(property.Property.Type);
    }

    private static bool IsCollectionType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        return type.AllInterfaces.Any(interfaceType =>
            interfaceType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>");
    }

    private static string NormalizeTypeName(string typeName)
        => typeName.StartsWith("global::", StringComparison.Ordinal)
            ? typeName
            : $"global::{typeName}";
}

internal static class DirectoryCleanerExtensions
{
    public static DirectoryCleaner Configure(this DirectoryCleaner cleaner, string targetPath)
    {
        cleaner.TargetPath = targetPath;
        return cleaner;
    }
}
