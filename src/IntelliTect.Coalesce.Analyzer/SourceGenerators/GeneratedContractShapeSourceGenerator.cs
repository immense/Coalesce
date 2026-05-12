using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace IntelliTect.Coalesce.Analyzer.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public sealed class GeneratedContractShapeSourceGenerator : IIncrementalGenerator
{
    private const string ShapeAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractShapeAttribute";
    private const string AliasAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractAliasAttribute";
    private const string DtoSourceAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.DtoSourceAttribute";
    private const string NullableAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractNullableAttribute";
    private const string NonNullableAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractNonNullableAttribute";
    private const string CoalesceAssemblyName = "IntelliTect.Coalesce";
    private const int ExplicitPolicy = 0;
    private const int PublicScalarPropertiesPolicy = 1;
    private const int ClassOutputKind = 0;
    private const int InterfaceOutputKind = 1;
    private const int NullableReferenceTypesTransform = 1;
    private const int NullableValueTypesTransform = 2;
    private const string MissingPropertyDiagnosticId = "COALESCEGC001";
    private const string DuplicateShapeDiagnosticId = "COALESCEGC002";

    private static readonly DiagnosticDescriptor MissingPropertyDiagnostic = new(
        MissingPropertyDiagnosticId,
        "Generated contract shape references a missing property",
        "{0}",
        "Coalesce.GeneratedContracts",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateShapeDiagnostic = new(
        DuplicateShapeDiagnosticId,
        "Generated contract shape is declared more than once",
        "{0}",
        "Coalesce.GeneratedContracts",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var currentCompilationShapeTypes = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ShapeAttributeMetadataName,
                static (_, _) => true,
                static (syntaxContext, _) => (INamedTypeSymbol)syntaxContext.TargetSymbol)
            .Collect();

        var inputs = context.CompilationProvider
            .Combine(currentCompilationShapeTypes);

        context.RegisterSourceOutput(inputs, static (productionContext, input) =>
            Execute(productionContext, input.Left, input.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> currentCompilationShapeTypes)
    {
        if (string.IsNullOrWhiteSpace(compilation.AssemblyName))
        {
            return;
        }

        var targetAssemblyName = NormalizeAssemblyName(compilation.AssemblyName!);
        var emittedTypes = new HashSet<string>(StringComparer.Ordinal);
        var processedSourceTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var sourceType in currentCompilationShapeTypes)
        {
            EmitShapes(sourceType);
        }

        foreach (var sourceType in EnumerateReferencedCandidateTypes(compilation))
        {
            EmitShapes(sourceType);
        }

        void EmitShapes(INamedTypeSymbol sourceType)
        {
            if (!processedSourceTypes.Add(sourceType))
            {
                return;
            }

            foreach (var shape in GetShapes(sourceType)
                .Where(shape => string.Equals(
                    NormalizeAssemblyName(shape.TargetAssemblyName),
                    targetAssemblyName,
                    StringComparison.Ordinal))
                .Where(shape => ShouldGenerateShape(sourceType, shape)))
            {
                var typeKey = $"{shape.TargetNamespace}.{shape.TypeName}";
                if (!emittedTypes.Add(typeKey))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DuplicateShapeDiagnostic,
                        sourceType.Locations.FirstOrDefault(),
                        $"Generated contract '{typeKey}' is declared more than once. The duplicate declaration was found on '{sourceType.ToDisplayString()}'."));
                    continue;
                }

                IReadOnlyList<ContractPropertyModel> properties;
                try
                {
                    properties = ResolveProperties(sourceType, shape);
                }
                catch (InvalidOperationException ex)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingPropertyDiagnostic,
                        sourceType.Locations.FirstOrDefault(),
                        ex.Message));
                    continue;
                }

                var model = new GeneratedContractFileModel(shape, properties);
                context.AddSource(
                    GetHintName(shape),
                    SourceText.From(Render(model), Encoding.UTF8));
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateReferencedCandidateTypes(Compilation compilation)
    {
        var visitedAssemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!ShouldInspectAssembly(assembly) || !visitedAssemblies.Add(assembly))
            {
                continue;
            }

            foreach (var type in EnumerateNamedTypes(assembly.GlobalNamespace))
            {
                yield return type;
            }
        }
    }

    private static bool ShouldInspectAssembly(IAssemblySymbol assembly)
    {
        if (assembly.Name == CoalesceAssemblyName)
        {
            return false;
        }

        return assembly.Modules.Any(module =>
            module.ReferencedAssemblySymbols.Any(reference => reference.Name == CoalesceAssemblyName));
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(INamespaceSymbol @namespace)
    {
        foreach (var member in @namespace.GetMembers())
        {
            foreach (var type in EnumerateNamedTypes(member))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(INamespaceOrTypeSymbol symbol)
    {
        switch (symbol)
        {
            case INamespaceSymbol @namespace:
                foreach (var member in @namespace.GetMembers())
                {
                    foreach (var type in EnumerateNamedTypes(member))
                    {
                        yield return type;
                    }
                }
                break;
            case INamedTypeSymbol type:
                yield return type;
                foreach (var nestedType in type.GetTypeMembers())
                {
                    foreach (var discovered in EnumerateNamedTypes(nestedType))
                    {
                        yield return discovered;
                    }
                }
                break;
        }
    }

    private static bool ShouldGenerateShape(INamedTypeSymbol sourceType, ContractShape shape)
    {
        if (shape.OutputKind != ClassOutputKind)
        {
            return true;
        }

        return shape.TypeName != sourceType.Name
            || shape.TargetNamespace != sourceType.ContainingNamespace.ToDisplayString();
    }

    internal static IEnumerable<ContractShape> GetShapes(INamedTypeSymbol sourceType)
        => sourceType.GetAttributes()
            .Where(IsShapeAttribute)
            .Select(ParseShape)
            .Where(static shape => shape is not null)
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
            GetBool(attribute, nameof(GeneratedContractShapeAttributePlaceholder.SettableProperties)),
            GetInt(attribute, nameof(GeneratedContractShapeAttributePlaceholder.NullabilityTransform)));
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
                GetDtoSource(property)));
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
            path!,
            GetString(attribute, nameof(DtoSourceAttributePlaceholder.OrderBy)),
            GetInt(attribute, nameof(DtoSourceAttributePlaceholder.OrderByDirection)));
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

        var transform = shape.NullabilityTransform;
        if (transform == 0)
        {
            return false;
        }

        return ((transform & NullableReferenceTypesTransform) != 0 && property.Type.IsReferenceType)
            || ((transform & NullableValueTypesTransform) != 0 && IsNonNullableValueType(property.Type));
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

    private static bool IsNonNullableValueType(ITypeSymbol type)
    {
        if (!type.IsValueType)
        {
            return false;
        }

        return type is not INamedTypeSymbol named
            || named.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
    }

    internal static string Render(GeneratedContractFileModel model)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.Append("namespace ").Append(model.Shape.TargetNamespace).AppendLine(";");
        builder.AppendLine();

        var declarationKind = model.Shape.OutputKind == InterfaceOutputKind ? "interface" : "partial class";
        var implements = model.Shape.Implements.Count > 0
            ? " : " + string.Join(", ", model.Shape.Implements.Select(NormalizeTypeName))
            : string.Empty;

        builder.Append("public ").Append(declarationKind).Append(' ').Append(model.Shape.TypeName).Append(implements).AppendLine();
        builder.AppendLine("{");

        foreach (var property in model.Properties)
        {
            if (property.DtoSource is { } dtoSource)
            {
                builder.Append("    ").AppendLine(BuildDtoSourceAttribute(dtoSource));
            }

            builder.Append("    ").AppendLine(BuildPropertyLine(model, property));
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string BuildPropertyLine(GeneratedContractFileModel model, ContractPropertyModel property)
    {
        var typeName = GetTypeName(property);
        if (model.Shape.OutputKind == InterfaceOutputKind)
        {
            var accessor = model.Shape.SettableProperties ? "{ get; set; }" : "{ get; }";
            return $"{typeName} {property.Name} {accessor}";
        }

        var required = NeedsRequiredKeyword(property) ? "required " : string.Empty;
        var initializer = HasCollectionInitializer(property) ? " = [];" : string.Empty;
        return $"public {required}{typeName} {property.Name} {{ get; set; }}{initializer}";
    }

    private static string BuildDtoSourceAttribute(DtoSourceMetadata dtoSource)
    {
        var args = new List<string>
        {
            SymbolDisplay.FormatLiteral(dtoSource.Path, quote: true)
        };

        if (!string.IsNullOrWhiteSpace(dtoSource.OrderBy))
        {
            args.Add($"OrderBy = {SymbolDisplay.FormatLiteral(dtoSource.OrderBy!, quote: true)}");
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

    private static string GetTypeName(ContractPropertyModel property)
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
            return display.Substring(0, display.Length - 1);
        }

        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            return named.TypeArguments[0].ToDisplayString(TypeDisplayFormat);
        }

        return display;
    }

    private static bool NeedsRequiredKeyword(ContractPropertyModel property)
    {
        if (property.ForceNullable || HasCollectionInitializer(property))
        {
            return false;
        }

        return property.Property.Type.IsReferenceType &&
               property.Property.NullableAnnotation != NullableAnnotation.Annotated;
    }

    private static bool HasCollectionInitializer(ContractPropertyModel property)
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

    private static string NormalizeAssemblyName(string assemblyName)
    {
        if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            assemblyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(assemblyName);
        }

        return assemblyName;
    }

    private static string GetHintName(ContractShape shape)
        => $"{shape.TargetNamespace}.{shape.TypeName}.g.cs"
            .Replace("global::", string.Empty)
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(':', '_');

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
                .ToImmutableArray();
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

    internal sealed class ContractShape
    {
        public ContractShape(
            string shapeName,
            int outputKind,
            string targetAssemblyName,
            string targetNamespace,
            string typeName,
            int policy,
            IReadOnlyList<string> members,
            IReadOnlyList<string> excludedMembers,
            IReadOnlyList<string> implements,
            bool settableProperties,
            int nullabilityTransform)
        {
            ShapeName = shapeName;
            OutputKind = outputKind;
            TargetAssemblyName = targetAssemblyName;
            TargetNamespace = targetNamespace;
            TypeName = typeName;
            Policy = policy;
            Members = members;
            ExcludedMembers = excludedMembers;
            Implements = implements;
            SettableProperties = settableProperties;
            NullabilityTransform = nullabilityTransform;
        }

        public string ShapeName { get; }
        public int OutputKind { get; }
        public string TargetAssemblyName { get; }
        public string TargetNamespace { get; }
        public string TypeName { get; }
        public int Policy { get; }
        public IReadOnlyList<string> Members { get; }
        public IReadOnlyList<string> ExcludedMembers { get; }
        public IReadOnlyList<string> Implements { get; }
        public bool SettableProperties { get; }
        public int NullabilityTransform { get; }
    }

    internal sealed class GeneratedContractFileModel
    {
        public GeneratedContractFileModel(ContractShape shape, IReadOnlyList<ContractPropertyModel> properties)
        {
            Shape = shape;
            Properties = properties;
        }

        public ContractShape Shape { get; }
        public IReadOnlyList<ContractPropertyModel> Properties { get; }
    }

    internal sealed class ContractPropertyModel
    {
        public ContractPropertyModel(
            string name,
            IPropertySymbol property,
            bool forceNullable,
            bool forceNonNullable,
            DtoSourceMetadata? dtoSource)
        {
            Name = name;
            Property = property;
            ForceNullable = forceNullable;
            ForceNonNullable = forceNonNullable;
            DtoSource = dtoSource;
        }

        public string Name { get; }
        public IPropertySymbol Property { get; }
        public bool ForceNullable { get; }
        public bool ForceNonNullable { get; }
        public DtoSourceMetadata? DtoSource { get; }
    }

    internal sealed class DtoSourceMetadata
    {
        public DtoSourceMetadata(string path, string? orderBy, int orderByDirection)
        {
            Path = path;
            OrderBy = orderBy;
            OrderByDirection = orderByDirection;
        }

        public string Path { get; }
        public string? OrderBy { get; }
        public int OrderByDirection { get; }
    }

    private static class GeneratedContractShapeAttributePlaceholder
    {
        public static int Policy { get; set; }
        public static string[] Members { get; set; } = [];
        public static string[] ExcludedMembers { get; set; } = [];
        public static string[] Implements { get; set; } = [];
        public static bool SettableProperties { get; set; }
        public static int NullabilityTransform { get; set; }
    }

    private static class DtoSourceAttributePlaceholder
    {
        public static string? OrderBy { get; set; }
        public static int OrderByDirection { get; set; }
    }
}
