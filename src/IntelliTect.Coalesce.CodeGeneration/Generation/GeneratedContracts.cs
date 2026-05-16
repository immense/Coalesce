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
    private const string IgnoreAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractIgnoreAttribute";
    private const string DefaultAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractDefaultAttribute";
    private const string DefaultsAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.CoalesceGeneratedContractDefaultsAttribute";
    private const string DtoSourceAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.DtoSourceAttribute";
    private const string NullableAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractNullableAttribute";
    private const string NonNullableAttributeMetadataName = "IntelliTect.Coalesce.DataAnnotations.GeneratedContractNonNullableAttribute";
    private const int ExplicitPolicy = 0;
    private const int PublicScalarPropertiesPolicy = 1;
    private const int AllDeclaredPropertiesPolicy = 2;
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
            var sourceAssemblyName = sourceType.ContainingAssembly?.Name ?? string.Empty;
            var assemblyDefaults = GetAssemblyDefaults(sourceType.ContainingAssembly);
            foreach (var shape in GetShapes(sourceType, assemblyDefaults).Select(s => ResolveConventions(s, sourceType, sourceAssemblyName, assemblyDefaults)))
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

    internal static IEnumerable<ContractShape> GetShapes(INamedTypeSymbol sourceType, AssemblyDefaults assemblyDefaults)
        => sourceType.GetAttributes()
            .Where(IsShapeAttribute)
            .Select(attribute => ParseShape(attribute, assemblyDefaults))
            .Where(shape => shape is not null)
            .Cast<ContractShape>();

    private static bool IsShapeAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() == ShapeAttributeMetadataName;

    internal static AssemblyDefaults GetAssemblyDefaults(IAssemblySymbol? assembly)
    {
        if (assembly is null)
        {
            return AssemblyDefaults.Empty;
        }

        var attribute = assembly.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DefaultsAttributeMetadataName);

        if (attribute is null)
        {
            return AssemblyDefaults.Empty;
        }

        bool? generateConstructors = null;
        string? shapeNamePrefix = null;
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key == "GenerateConstructors" && named.Value.Value is bool boolValue)
            {
                generateConstructors = boolValue;
            }
            else if (named.Key == "ShapeNamePrefix" && named.Value.Value is string stringValue)
            {
                shapeNamePrefix = stringValue;
            }
        }

        return new AssemblyDefaults(generateConstructors, shapeNamePrefix);
    }

    private static ContractShape? ParseShape(AttributeData attribute, AssemblyDefaults assemblyDefaults)
    {
        string shapeName;
        int outputKind;
        string targetAssemblyName;
        string targetNamespace;
        string typeName;

        if (attribute.ConstructorArguments.Length >= 5)
        {
            // Full 5-arg constructor: shapeName, outputKind, assembly, namespace, typeName
            shapeName = (string?)attribute.ConstructorArguments[0].Value ?? string.Empty;
            outputKind = Convert.ToInt32(attribute.ConstructorArguments[1].Value);
            targetAssemblyName = (string?)attribute.ConstructorArguments[2].Value ?? string.Empty;
            targetNamespace = (string?)attribute.ConstructorArguments[3].Value ?? string.Empty;
            typeName = (string?)attribute.ConstructorArguments[4].Value ?? string.Empty;
        }
        else if (attribute.ConstructorArguments.Length == 1)
        {
            // Simplified 1-arg constructor: shapeName only.
            shapeName = (string?)attribute.ConstructorArguments[0].Value ?? string.Empty;
            outputKind = GetInt(attribute, "OutputKind", ClassOutputKind);
            targetAssemblyName = string.Empty;
            targetNamespace = string.Empty;
            typeName = string.Empty;
        }
        else if (attribute.ConstructorArguments.Length == 0)
        {
            // Zero-arg constructor — every value is conventional, including the shape name.
            shapeName = string.Empty;
            outputKind = GetInt(attribute, "OutputKind", ClassOutputKind);
            targetAssemblyName = string.Empty;
            targetNamespace = string.Empty;
            typeName = string.Empty;
        }
        else
        {
            return null;
        }

        // Default policy is AllDeclaredProperties unless explicitly overridden.
        var policy = GetInt(
            attribute,
            nameof(GeneratedContractShapeAttributePlaceholder.Policy),
            AllDeclaredPropertiesPolicy);

        // GenerateConstructors precedence: explicit on shape > assembly default > true.
        bool generateConstructors;
        if (TryGetBool(attribute, nameof(GeneratedContractShapeAttributePlaceholder.GenerateConstructors), out var explicitValue))
        {
            generateConstructors = explicitValue;
        }
        else
        {
            generateConstructors = assemblyDefaults.GenerateConstructors ?? true;
        }

        return new ContractShape(
            shapeName,
            outputKind,
            targetAssemblyName,
            targetNamespace,
            typeName,
            policy,
            GetStringArray(attribute, nameof(GeneratedContractShapeAttributePlaceholder.Members)),
            GetStringArray(attribute, nameof(GeneratedContractShapeAttributePlaceholder.ExcludedMembers)),
            GetStringArray(attribute, nameof(GeneratedContractShapeAttributePlaceholder.Implements)),
            GetTypeArray(attribute, nameof(GeneratedContractShapeAttributePlaceholder.IncludedPropertyAttributes)),
            GetBool(attribute, nameof(GeneratedContractShapeAttributePlaceholder.SettableProperties)),
            GetInt(attribute, nameof(GeneratedContractShapeAttributePlaceholder.NullabilityTransform)),
            generateConstructors);
    }

    /// <summary>
    /// Resolves convention-based defaults for shapes that used the simplified 1-arg constructor.
    /// Empty strings in TargetAssemblyName/TargetNamespace/TypeName are filled from the source type context.
    /// </summary>
    internal static ContractShape ResolveConventions(ContractShape shape, INamedTypeSymbol sourceType, string compilationAssemblyName, AssemblyDefaults assemblyDefaults)
    {
        var resolvedAssembly = shape.TargetAssemblyName;
        var resolvedNamespace = shape.TargetNamespace;
        var resolvedTypeName = shape.TypeName;

        if (string.IsNullOrEmpty(resolvedAssembly))
        {
            resolvedAssembly = compilationAssemblyName;
            // Strip .dll extension if present (can happen when assembly loaded from metadata reference)
            if (resolvedAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                resolvedAssembly = resolvedAssembly.Substring(0, resolvedAssembly.Length - 4);
            }
        }

        if (string.IsNullOrEmpty(resolvedNamespace))
        {
            resolvedNamespace = sourceType.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : sourceType.ContainingNamespace.ToDisplayString();
            // Strip .GeneratedSources suffix - source classes are placed in this
            // sub-namespace by convention, but generated types belong in the parent.
            const string generatedSourcesSuffix = ".GeneratedSources";
            if (resolvedNamespace.EndsWith(generatedSourcesSuffix, StringComparison.Ordinal))
            {
                resolvedNamespace = resolvedNamespace.Substring(0, resolvedNamespace.Length - generatedSourcesSuffix.Length);
            }
        }

        if (string.IsNullOrEmpty(resolvedTypeName))
        {
            resolvedTypeName = InferTypeName(sourceType.Name);
        }

        var resolvedShapeName = shape.ShapeName;
        if (string.IsNullOrEmpty(resolvedShapeName))
        {
            resolvedShapeName = (assemblyDefaults.ShapeNamePrefix ?? string.Empty)
                + ToKebabCase(InferTypeName(sourceType.Name));
        }

        if (resolvedAssembly == shape.TargetAssemblyName
            && resolvedNamespace == shape.TargetNamespace
            && resolvedTypeName == shape.TypeName
            && resolvedShapeName == shape.ShapeName)
        {
            return shape;
        }

        return shape with
        {
            ShapeName = resolvedShapeName,
            TargetAssemblyName = resolvedAssembly,
            TargetNamespace = resolvedNamespace,
            TypeName = resolvedTypeName,
        };
    }

    internal static string InferTypeName(string sourceClassName)
    {
        var suffixes = new[] { "ContractSource", "Source" };
        foreach (var suffix in suffixes)
        {
            if (sourceClassName.EndsWith(suffix, StringComparison.Ordinal) && sourceClassName.Length > suffix.Length)
            {
                return sourceClassName.Substring(0, sourceClassName.Length - suffix.Length);
            }
        }

        return sourceClassName;
    }

    internal static string ToKebabCase(string pascal)
    {
        if (string.IsNullOrEmpty(pascal))
        {
            return pascal;
        }

        var sb = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (i > 0 && char.IsUpper(c))
            {
                var prev = pascal[i - 1];
                var next = i + 1 < pascal.Length ? pascal[i + 1] : (char?)null;
                if (char.IsLower(prev) || char.IsDigit(prev) ||
                    (next.HasValue && char.IsLower(next.Value)))
                {
                    sb.Append('-');
                }
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static int GetInt(AttributeData attribute, string name, int defaultValue = 0)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is not null)
            {
                return Convert.ToInt32(argument.Value.Value);
            }
        }

        return defaultValue;
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

    private static bool GetBool(AttributeData attribute, string name, bool defaultValue = false)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool value)
            {
                return value;
            }
        }

        return defaultValue;
    }

    private static bool TryGetBool(AttributeData attribute, string name, out bool value)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool b)
            {
                value = b;
                return true;
            }
        }

        value = false;
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
                GetDefaultExpression(property),
                GetIncludedPropertyAttributes(property, shape)));
        }

        return properties;
    }

    private static string? GetDefaultExpression(IPropertySymbol property)
    {
        var attribute = property.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DefaultAttributeMetadataName);

        if (attribute is null || attribute.ConstructorArguments.Length == 0)
        {
            return null;
        }

        return attribute.ConstructorArguments[0].Value as string;
    }

    private static IReadOnlyList<string> ResolveMembers(INamedTypeSymbol sourceType, ContractShape shape)
    {
        if (shape.Members.Count > 0)
        {
            return shape.Members;
        }

        if (shape.Policy == PublicScalarPropertiesPolicy || shape.Policy == AllDeclaredPropertiesPolicy)
        {
            var excludedMembers = new HashSet<string>(shape.ExcludedMembers, StringComparer.Ordinal);
            return EnumeratePolicyProperties(sourceType, shape.Policy)
                .Where(property => !excludedMembers.Contains(property.Name))
                .Where(property => !HasIgnoreAttribute(property, shape.ShapeName))
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

    private static IEnumerable<IPropertySymbol> EnumeratePolicyProperties(INamedTypeSymbol sourceType, int policy)
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

                if (policy == PublicScalarPropertiesPolicy && !IsScalarLikeType(property.Type))
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
           && property.GetMethod?.DeclaredAccessibility == Accessibility.Public;

    private static bool HasIgnoreAttribute(IPropertySymbol property, string shapeName)
        => property.GetAttributes().Any(attribute =>
        {
            if (attribute.AttributeClass?.ToDisplayString() != IgnoreAttributeMetadataName)
            {
                return false;
            }

            // Parameterless form applies to every shape.
            if (attribute.ConstructorArguments.Length == 0)
            {
                return true;
            }

            var targetShape = (string?)attribute.ConstructorArguments[0].Value;
            return string.IsNullOrEmpty(targetShape)
                || string.Equals(targetShape, shapeName, StringComparison.Ordinal);
        });

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

        var fullyQualifiedName = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
        return fullyQualifiedName is
            "System.Guid" or
            "System.DateTime" or
            "System.DateTimeOffset" or
            "System.TimeSpan" or
            "System.DateOnly" or
            "System.TimeOnly" or
            "System.Text.Json.JsonElement" or
            "NuGet.Versioning.SemanticVersion" or
            "NuGet.Versioning.NuGetVersion";
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
        public static bool GenerateConstructors { get; set; }
    }

    internal sealed class AssemblyDefaults
    {
        public static readonly AssemblyDefaults Empty = new(null, null);

        public AssemblyDefaults(bool? generateConstructors, string? shapeNamePrefix)
        {
            GenerateConstructors = generateConstructors;
            ShapeNamePrefix = shapeNamePrefix;
        }

        public bool? GenerateConstructors { get; }
        public string? ShapeNamePrefix { get; }
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
        int NullabilityTransform,
        bool GenerateConstructors = false);

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
        string? DefaultExpression,
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

            // Emit constructors for class output when GenerateConstructors is enabled
            if (model.Shape.OutputKind == 0 && model.Shape.GenerateConstructors && model.Properties.Count > 0)
            {
                b.Line();
                BuildConstructors(b, model);
            }
        }
    }

    private static void BuildConstructors(CSharpCodeBuilder b, GeneratedContracts.GeneratedContractFileModel model)
    {
        // Parameterless constructor — emit initializer assignments for
        // properties marked with [GeneratedContractDefault].
        using (b.Block($"public {model.Shape.TypeName}()"))
        {
            foreach (var property in model.Properties)
            {
                if (property.DefaultExpression is { Length: > 0 } expression)
                {
                    b.Line($"{property.Name} = {expression};");
                }
            }
        }
        b.Line();

        // All-args constructor — annotate with [SetsRequiredMembers] so it can satisfy required properties.
        var parameters = model.Properties
            .Select(p => $"{GetTypeName(p)} {p.Name}")
            .ToArray();

        b.Line("[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]");
        using (b.Block($"public {model.Shape.TypeName}({string.Join(", ", parameters)})"))
        {
            foreach (var property in model.Properties)
            {
                b.Line($"this.{property.Name} = {property.Name};");
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
