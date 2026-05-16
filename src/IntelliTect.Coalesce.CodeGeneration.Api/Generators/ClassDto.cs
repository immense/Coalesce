using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.DataAnnotations;
using IntelliTect.Coalesce.TypeDefinition;
using IntelliTect.Coalesce.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace IntelliTect.Coalesce.CodeGeneration.Api.Generators;

public class ClassDto : StringBuilderCSharpGenerator<ClassViewModel>
{
    public ClassDto(GeneratorServices services) : base(services)
    {
    }

    protected string DtoNamespace
    {
        get
        {
            string namespaceName = Namespace;
            if (!string.IsNullOrWhiteSpace(AreaName))
            {
                namespaceName += "." + AreaName;
            }
            namespaceName += ".Models";
            return namespaceName;
        }
    }

    public override void BuildOutput(CSharpCodeBuilder b)
    {
        var namespaces = new List<string>
        {
            "IntelliTect.Coalesce",
            "IntelliTect.Coalesce.Mapping",
            "IntelliTect.Coalesce.Models",
            "System",
            "System.Collections.Immutable",
            "System.Linq",
            "System.Collections.Generic",
            "System.Security.Claims",
            "System.Text.Json.Serialization",
        };
        foreach (var ns in namespaces.OrderBy(n => n))
        {
            b.Line($"using {ns};");
        }

        b.Line();
        b.Line("#pragma warning disable CS0618");
        b.Line();

        using (b.Block($"namespace {DtoNamespace}"))
        {
            WriteParameterDto(b);
            b.Line();
            WriteResponseDto(b);
            foreach (var contentView in Model.GeneratedResponseContentViews)
            {
                b.Line();
                WriteResponseDto(b, contentView);
            }
            if (Model.ShouldGenerateSummaryDto)
            {
                b.Line();
                WriteSummaryDto(b);
            }
        }

        b.Line();
        b.Line("#pragma warning restore CS0618");
    }

    private void WriteParameterDto(CSharpCodeBuilder b)
    {
        var allVariants = Model.ClientDerivedTypes;
        if (allVariants.Any() && !Model.Type.IsAbstract) allVariants = allVariants.Prepend(Model);
        foreach (var derived in allVariants)
        {
            b.Line($"[JsonDerivedType(typeof({derived.ParameterDtoTypeName}), typeDiscriminator: {derived.ClientTypeName.QuotedStringLiteralForCSharp()})]");
        }

        ClassViewModel baseType = Model.ClientBaseTypes.FirstOrDefault();

        string inheritClause = baseType is not null
            ? $"{baseType.ParameterDtoTypeName}, IGeneratedParameterDto<{Model.FullyQualifiedName}>"
            : $"SparseDto, IGeneratedParameterDto<{Model.FullyQualifiedName}>";

        using (b.Block($"public partial class {Model.ParameterDtoTypeName} : {inheritClause}"))
        {
            b.Line($"public {Model.ParameterDtoTypeName}() {{ }}");

            var orderedProps = Model
                .ClientProperties
                .Where(p => p.IsClientSerializable)
                // PK always first so it is available to guide decisions in IPropertyRestrictions
                .OrderBy(p => !p.IsPrimaryKey)
                    // Scalars before objects
                    .ThenBy(p => p.PureType.HasClassViewModel)
                    // Finally, preserve original field order.
                    .ThenBy(p => p.ClassFieldOrder)
                .ToList();

            var ownProps = orderedProps.Where(p => baseType?.PropertyByName(p.Name) is null);

            b.Line();
            foreach (PropertyViewModel prop in ownProps)
            {
                b.Line($"private {prop.Type.NullableTypeForDto(isInput: true, dtoNamespace: DtoNamespace)} _{prop.Name};");
            }

            b.Line();
            foreach (PropertyViewModel prop in ownProps)
            {
                using (b.Block($"public {prop.Type.NullableTypeForDto(isInput: true, dtoNamespace: DtoNamespace)} {prop.Name}"))
                {
                    b.Line($"get => _{prop.Name};");
                    b.Line($"set {{ _{prop.Name} = value; Changed(nameof({prop.Name})); }}");
                }
            }

            b.DocComment("Map from the current DTO instance to the domain object.");
            using (b.Block($"public void MapTo({Model.FullyQualifiedName} entity, IMappingContext context)"))
            {
                var derivedTypes = Model.ClientDerivedTypes.ToList();
                if (derivedTypes.Any())
                {
                    // Dispatch to derived types, since usages of this DTO in other generated code will
                    // dispatch calls to the base type version of this method rather than the derived types.
                    using (b.Block("switch (this)"))
                    {
                        foreach (var derived in derivedTypes)
                        {
                            b.Line($"case {derived.ParameterDtoTypeName} _{derived.Name}:");
                            b.Indented($"_{derived.Name}.MapTo(entity, context);");
                            b.Indented("return;");
                        }
                    }
                }

                b.Line("var includes = context.Includes;");
                b.Line();

                WriteSetters(b, orderedProps
                    .Where(p => p.SecurityInfo.Edit.IsAllowed())
                    .Select(p =>
                    {
                        var (conditional, setter) = DtoToModelPropertySetter(p, p.SecurityInfo.Edit);
                        conditional = conditional.Prepend($"ShouldMapTo(nameof({p.Name}))");
                        return (conditional, $"entity.{p.Name} = {setter};");
                    }));
            }

            b.DocComment("Map from the current DTO instance to a new instance of the domain object.");
            using (b.Block($"public {(baseType is null ? "" : "new ")}{Model.FullyQualifiedName} MapToNew(IMappingContext context)"))
            {
                var derivedTypes = Model.ClientDerivedTypes.ToList();
                if (derivedTypes.Any())
                {
                    // Dispatch to derived types, since usages of this DTO in other generated code will
                    // dispatch calls to the base type version of this method rather than the derived types.
                    using (b.Block("switch (this)"))
                    {
                        foreach (var derived in derivedTypes)
                        {
                            b.Line($"case {derived.ParameterDtoTypeName} _{derived.Name}:");
                            b.Indented($"return _{derived.Name}.MapToNew(context);");
                        }
                    }
                }

                var properties = orderedProps
                    .Where(p => p.SecurityInfo.Init.IsAllowed())
                    .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

                // Find the best ctor.
                // We want to use the smallest ctor so we can favor default ctors if one exists,
                // since prior to `init` and `required`, this was how Coalesce always worked.
                var bestCtor = Model.Constructors
                    .OrderBy(c => c.Parameters.Count())
                    .Where(c => c.DtoMapToNewConstructorUsage.IsAcceptable)
                    .FirstOrDefault();

                if (bestCtor == null)
                {
                    var reasons = Model.Constructors
                        .Select(c => $"{c.ToStringWithoutReturn()}: {c.DtoMapToNewConstructorUsage.Reason}")
                        .ToList();
                    if (!reasons.Any())
                    {
                        reasons.Add("Type has no public constructors.");
                    }

                    b.Line("// Unacceptable constructors:");
                    foreach (var reason in reasons) b.Append("// ").Line(reason);

                    if (Model.Type.IsAbstract)
                    {
                        // There's no constructor we can use, but also no properties that can even be mapped.
                        b.Line("throw new NotSupportedException(" +
                            $"\"Type {Model.Name} is abstract and therefore will never be instantiated directly by Coalesce.\");");
                    }
                    else if (properties.Count == 0)
                    {
                        // There's no constructor we can use, but also no properties that can even be mapped.
                        b.Line("throw new NotSupportedException(" +
                            $"\"Type {Model.Name} has no initializable properties and so cannot be used as an input to any Coalesce-generated APIs.\");");
                    }
                    else if (properties.All(p => p.Value.SecurityInfo.Init.IsUnused))
                    {
                        // If the type has no default constructor but also isn't used as an input by Coalesce,
                        // don't stop code gen - just gen an exception (that will never be hit at runtime)
                        b.Line("throw new NotSupportedException(" +
                            $"\"Type '{Model.Name}' does not have a constructor suitable for use by Coalesce for new object instantiation. " +
                            "Fortunately, this type appears to never be used in an input position in a Coalesce-generated API.\");");
                    }
                    else
                    {
                        throw new Exception($"Unable to find an appropriate constructor for type {Model.FullyQualifiedName}. The following public constructors were found to be insufficient: \n\n {string.Join("\n", reasons)}");
                    }
                }
                else
                {
                    var ctorUsage = bestCtor.DtoMapToNewConstructorUsage;
                    var ctorParams = ctorUsage.CtorParams;
                    var initParams = ctorUsage.InitParams;

                    if (!ctorParams.Any() && !initParams.Any())
                    {
                        b.Line($"var entity = new {Model.FullyQualifiedName}();");
                        b.Line($"MapTo(entity, context);");
                        b.Line($"return entity;");
                    }
                    else
                    {
                        b.Line("var includes = context.Includes;");
                        b.Line();

                        b.Append($"var entity = new {Model.FullyQualifiedName}(");
                        if (ctorParams.Any()) b.Line();
                        for (int i = 0; i < ctorParams.Count; i++)
                        {
                            b.Indented(InlinePropertyRhs(ctorParams[i]) + (i < ctorParams.Count - 1 ? "," : ""));
                        }
                        b.Append(")");

                        if (initParams.Any())
                        {
                            using (b.Block(closeWith: ";"))
                            {
                                foreach (var prop in initParams)
                                {
                                    b.Line($"{prop.Name} = {InlinePropertyRhs(prop)},");
                                }
                            }
                        }
                        else
                        {
                            b.Append(";");
                        }

                        WriteSetters(b, ctorUsage.SetParams
                            .Select(p =>
                            {
                                var (conditional, setter) = DtoToModelPropertySetter(p, p.SecurityInfo.Init);
                                conditional = conditional.Prepend($"ShouldMapTo(nameof({p.Name}))");
                                return (conditional, $"entity.{p.Name} = {setter};");
                            }));

                        b.Line();
                        b.Line("return entity;");
                    }

                    string InlinePropertyRhs(PropertyViewModel p)
                    {
                        // Init-only props must be set here where we instantiate the type.
                        // They cannot be handled by the record
                        var (conditional, setter) = DtoToModelPropertySetter(p, p.SecurityInfo.Init, modelVar: null);

                        if (conditional.Any())
                        {
                            return $"({string.Join(" && ", conditional)}) ? {setter} : default";
                        }
                        else
                        {
                            return setter;
                        }
                    }
                }
            }

            b.Line();
            b.Line($$"""
                public {{Model.FullyQualifiedName}} MapToModelOrNew({{Model.FullyQualifiedName}} obj, IMappingContext context)
                {
                    if (obj is null) return MapToNew(context);
                    MapTo(obj, context);
                    return obj;
            }
            """);
        }
    }

    private void WriteResponseDto(CSharpCodeBuilder b)
        => WriteResponseDto(b, contentView: null);

    private void WriteResponseDto(CSharpCodeBuilder b, string contentView)
    {
        var fixedContentView = string.IsNullOrWhiteSpace(contentView) ? null : contentView;
        var responseTypeName = GetGeneratedResponseDtoTypeName(Model, fixedContentView);

        var allVariants = fixedContentView is null
            ? Model.ClientDerivedTypes
            : Model.ClientDerivedTypes.Where(d => d.HasResponseDtoTypeForContentView(fixedContentView));

        if (allVariants.Any() && !Model.Type.IsAbstract) allVariants = allVariants.Prepend(Model);
        foreach (var derived in allVariants)
        {
            b.Line($"[JsonDerivedType(typeof({GetGeneratedResponseDtoTypeName(derived, fixedContentView)}), typeDiscriminator: {derived.ClientTypeName.QuotedStringLiteralForCSharp()})]");
        }

        ClassViewModel baseType = Model.ClientBaseTypes.FirstOrDefault();

        string inheritClause = baseType is not null
            ? $"{GetGeneratedResponseDtoTypeName(baseType, fixedContentView)}, IGeneratedResponseDto<{Model.FullyQualifiedName}>"
            : $"IGeneratedResponseDto<{Model.FullyQualifiedName}>";

        using (b.Block($"public partial class {responseTypeName} : {inheritClause}"))
        {
            b.Line($"public {responseTypeName}() {{ }}");

            b.Line();

            var orderedProps = Model
                .ClientProperties
                .Where(p => p.SecurityInfo.Read.IsAllowed())
                .Where(p => fixedContentView is null
                    ? ShouldEmitInBaseResponse(p)
                    : p.IsMappedForContentView(fixedContentView))
                // PK always first so it is available to guide decisions in IPropertyRestrictions
                .OrderBy(p => !p.IsPrimaryKey)
                    // Scalars before objects
                    .ThenBy(p => p.PureType.HasClassViewModel)
                    // Finally, preserve original field order.
                    .ThenBy(p => p.ClassFieldOrder)
                .ToList();

            var ownProps = orderedProps.Where(p => baseType?.PropertyByName(p.Name) is null);
            var flattenedProps = Model.FlattenedResponseProperties
                .Where(p => (fixedContentView is null
                        ? ShouldEmitInBaseResponse(p)
                        : p.IsMappedForContentView(fixedContentView))
                    && baseType?.PropertyByName(p.Name) is null
                    && !(baseType?.FlattenedResponseProperties.Any(fp => fp.Name == p.Name) ?? false))
                .ToList();

            foreach (PropertyViewModel prop in ownProps)
            {
                b.Line($"public {ResponsePropertyType(prop, fixedContentView)} {ResponsePropertyName(prop)} {{ get; set; }}");
            }
            foreach (var prop in flattenedProps)
            {
                b.Line($"public {prop.Type.NullableTypeForDto(isInput: false, dtoNamespace: DtoNamespace)} {ResponsePropertyName(prop)} {{ get; set; }}");
            }

            b.DocComment("Map from the domain object to the properties of the current DTO instance.");
            using (b.Block($"public void MapFrom({Model.FullyQualifiedName} obj, IMappingContext context, IncludeTree tree = null)"))
            {
                b.Line("if (obj is null) return;");

                var derivedTypes = fixedContentView is null
                    ? Model.ClientDerivedTypes.ToList()
                    : Model.ClientDerivedTypes.Where(d => d.HasResponseDtoTypeForContentView(fixedContentView)).ToList();
                if (derivedTypes.Any())
                {
                    // Dispatch to derived types, since usages of this DTO in other generated code will
                    // dispatch calls to the base type version of this method rather than the derived types.
                    using (b.Block("switch (this)"))
                    {
                        foreach (var derived in derivedTypes)
                        {
                            b.Line($"case {GetGeneratedResponseDtoTypeName(derived, fixedContentView)} _{derived.Name}:");
                            b.Indented($"_{derived.Name}.MapFrom(({derived.FullyQualifiedName})obj, context, tree);");
                            b.Indented($"return;");
                        }
                    }
                }

                if (fixedContentView is null)
                {
                    b.Line("var includes = context.Includes;");
                    b.Line();
                }

                WriteSetters(b, orderedProps
                    .Select(p => ModelToDtoPropertySetter(p, fixedContentView))
                    .Concat(flattenedProps.Select(p => ModelToDtoFlattenedPropertySetter(p, fixedContentView))));
            }
        }
    }

    private void WriteSummaryDto(CSharpCodeBuilder b)
    {
        var primaryKey = Model.PrimaryKey
            ?? throw new InvalidOperationException($"Summary DTO generation for {Model.FullyQualifiedName} requires a primary key.");

        using (b.Block($"public partial class {Model.SummaryDtoTypeName} : IGeneratedResponseDto<{Model.FullyQualifiedName}>"))
        {
            b.Line($"public {Model.SummaryDtoTypeName}() {{ }}");
            b.Line();
            b.Line($"public {primaryKey.Type.NullableTypeForDto(isInput: false, dtoNamespace: DtoNamespace)} {ResponsePropertyName(primaryKey)} {{ get; set; }}");

            foreach (var prop in Model.SummaryProperties)
            {
                b.Line($"public {prop.Type.NullableTypeForDto(isInput: false, dtoNamespace: DtoNamespace)} {ResponsePropertyName(prop)} {{ get; set; }}");
            }

            b.DocComment("Map from the domain object to the properties of the current summary DTO instance.");
            using (b.Block($"public void MapFrom({Model.FullyQualifiedName} obj, IMappingContext context, IncludeTree tree = null)"))
            {
                b.Line("if (obj is null) return;");
                b.Line("var includes = context.Includes;");
                b.Line();
                b.Line($"this.{ResponsePropertyName(primaryKey)} = {TransformResponseValue(primaryKey.Type, $"obj.{primaryKey.Name}")};");
                WriteSetters(b, Model.SummaryProperties.Select(ModelToSummaryDtoPropertySetter));
            }
        }
    }



    void WriteSetters(CSharpCodeBuilder b, IEnumerable<(IEnumerable<string> conditionals, string setter)> settersAndConditionals)
    {
        foreach (var conditionGroup in settersAndConditionals
            .GroupBy(s => s.conditionals.FirstOrDefault(), s => s))
        {
            if (conditionGroup.Key is null)
            {
                foreach (var setter in conditionGroup)
                {
                    b.Line(setter.setter);
                }
            }
            else if (
                // There are multiple setters that use `conditionGroup.Key` as at least part of
                // their conditional. Group them together in a single `if` that consumes
                // that shared part of the mapping conditional.
                conditionGroup.Count() > 1 ||
                // Also use a full block if the setter contains multiple statements
                // that would make a braceless block impossible.
                (conditionGroup.Single() is var singleSetter && singleSetter.setter.Count(x => x == ';') > 1)
            )
            {
                using (b.Block($"if ({conditionGroup.Key})"))
                {
                    WriteSetters(b, conditionGroup.Select(g => (g.conditionals.Skip(1), g.setter)));
                }
                b.Line();
            }
            else
            {
                // There's only one setter in `conditionGroup`,
                // such that we can merge all its conditions together.
                b.Append("if (");
                b.Append(string.Join(" && ", singleSetter.conditionals));
                b.Append(") ");
                b.Line(singleSetter.setter);
            }
        }
    }

    /// <summary>
    /// Get a C# boolean expression that could be evaluated to determine if a property is settable,
    /// taking into account the current user and whether the property mapping is incoming or outgoing.
    /// </summary>
    /// <param name="property">The property whose permissions will be evaluated.</param>
    /// <param name="permission">The permission info to pull the required roles from.</param>
    /// <param name="modelVar">The variable that holds the entity/model instance.</param>
    /// <param name="fixedContentView">The fixed content view for action-specific response DTOs, if any.</param>
    /// <returns></returns>
    private IEnumerable<string> GetPropertySetterConditional(
        PropertyViewModel property,
        PropertySecurityPermission permission,
        string modelVar,
        string fixedContentView = null)
    {
        string RoleCheck(string role) => $"context.IsInRoleCached(\"{role.EscapeStringLiteralForCSharp()}\")";
        string IncludesCheck(string include) => $"includes == \"{include.EscapeStringLiteralForCSharp()}\"";

        string roles = string.Join(" && ", permission.RoleLists
            .Select(rl => rl.Count == 1
                // Only add wrapping parens if we need them:
                ? RoleCheck(rl.Single())
                : "(" + string.Join(" || ", rl.Select(RoleCheck)) + ")"
            )
            .Distinct());

        var statement = new List<string>();
        if (!string.IsNullOrEmpty(roles)) statement.Add($"({roles})");

        if (fixedContentView is null)
        {
            var includes = string.Join(" || ", property.DtoIncludes.Select(IncludesCheck));
            var excludes = string.Join(" || ", property.DtoExcludes.Select(IncludesCheck));
            var explicitViewExcludes = string.Join(" || ", property.EffectiveParent.DtoContentViews
                .Where(v => !v.Value && !property.DtoIncludes.Contains(v.Key, StringComparer.Ordinal))
                .Select(v => IncludesCheck(v.Key)));

            if (!string.IsNullOrEmpty(includes)) statement.Add($"({includes})");
            if (!string.IsNullOrEmpty(excludes)) statement.Add($"!({excludes})");
            if (!string.IsNullOrEmpty(explicitViewExcludes)) statement.Add($"!({explicitViewExcludes})");
        }

        foreach (var restriction in property.SecurityInfo.Restrictions)
        {
            var restrictionExpr = $"context.GetPropertyRestriction<{restriction.FullyQualifiedName}>()";
            if (permission.Name == "Read")
            {
                statement.Add($"{restrictionExpr}.UserCanRead(context, nameof({property.Name}), {modelVar ?? "null"})");
            }
            else
            {
                statement.Add($"{restrictionExpr}.UserCanWrite(context, nameof({property.Name}), {modelVar ?? "null"}, {property.Name})");
            }
        }

        return statement;
    }

    /// <summary>
    /// Get the conditional and a C# expression that will map the property from a DTO to a local model object.
    /// </summary>
    private (IEnumerable<string> conditionals, string setter) DtoToModelPropertySetter(
        PropertyViewModel property,
        PropertySecurityPermission permission,
        string modelVar = "entity"
    )
    {
        string name = property.Name;

        string setter;
        if (property.Type.IsDictionary)
        {
            setter = DictionaryDtoToModelExpression(property.Type, name);
        }
        else if (property.Object != null)
        {
            if (property.Type.IsCollection)
            {
                setter = $"{name}?.Select(f => f.MapToNew(context)).{CollectionMaterializerForModel(property.Type)}()";
            }
            else if (modelVar != null)
            {
                setter = $"{name}?.MapToModelOrNew({modelVar}.{name}, context)";
            }
            else
            {
                setter = $"{name}?.MapToNew(context)";
            }
        }
        else if (!property.Type.IsArray && property.Type.IsA(typeof(IList<>)))
        {
            // Lists of scalar values, whose DTO properties will be ICollection<>, preventing direct assignment.
            setter = $"{name}?.{CollectionMaterializerForModel(property.Type)}()";
        }
        else
        {
            var newValue = name;
            if (!property.Type.IsReferenceOrNullableValue && property.Type.CsDefaultValue != "null")
            {
                var fallback = modelVar == null ? "default" : $"{modelVar}.{name}";
                newValue = $"({newValue} ?? {fallback})";
            }
            setter = $"{newValue}";
        }

        var statement = GetPropertySetterConditional(property, permission, modelVar);
        return (statement, setter);
    }

    /// <summary>
    /// Get the conditional and a C# expression that will map the property from a local object to a DTO.
    /// </summary>
    /// <param name="property">The property to map</param>
    /// <param name="fixedContentView">The fixed content view for action-specific response DTOs, if any.</param>
    private (IEnumerable<string> conditionals, string setter) ModelToDtoPropertySetter(PropertyViewModel property, string fixedContentView = null)
    {
        string name = property.Name;
        string dtoName = ResponsePropertyName(property);
        string dtoVar = "this";
        string targetDtoTypeName = GetResponseDtoTypeName(property, fixedContentView);

        string setter;
        string mapCall() => property.Object.IsCustomDto
            ? "" // If we hang an IClassDto off an external type, or another IClassDto, no mapping needed - it is already the desired type.
            : $".MapToDto<{property.Object.FullyQualifiedName}, {targetDtoTypeName}>(context, tree?[nameof({dtoVar}.{dtoName})])";

        if (property.Type.IsDictionary)
        {
            setter = $"{dtoVar}.{dtoName} = {DictionaryModelToDtoExpression(property.Type, $"obj.{name}", fixedContentView)};";
        }
        else if (property.Type.IsCollection)
        {
            if (property.Object != null)
            {
                // Only check the includes tree for things that are in the database.
                // Otherwise, this would break IncludesExternal.
                var sb = new CodeBuilder(3);

                // Set this as a variable once and then use it below. This prevents multiple-evaluation of computed getter-only properties.
                sb.Line($"var propVal{name} = obj.{name};");
                sb.Append($"if (propVal{name} != null");
                if (property.Object.HasDbSet)
                {
                    sb.Append($" && (tree == null || tree[nameof({dtoVar}.{dtoName})] != null)");
                }
                sb.Line(") {");
                using (sb.Indented())
                {
                    sb.Line($"{dtoVar}.{dtoName} = propVal{name}");

                    var defaultOrderBy = property.Object.DefaultOrderBy;
                    if (defaultOrderBy.Count > 0)
                    {
                        var orderByStatements = defaultOrderBy
                            .Select((orderInfo, i) =>
                            {
                                string prefix = i == 0 ? ".OrderBy" : ".ThenBy";

                                if (orderInfo.OrderByDirection == DefaultOrderByAttribute.OrderByDirections.Ascending)
                                {
                                    return $"{prefix}(f => {orderInfo.OrderExpression("f")})";
                                }
                                else
                                {
                                    return $"{prefix}Descending(f => {orderInfo.OrderExpression("f")})";
                                }
                            });

                        sb.Indented(string.Concat(orderByStatements));
                    }

                    sb.Indented($".Select(f => f{mapCall()}).{(property.Type.IsArray ? "ToArray" : "ToList")}();");
                }

                if (property.Object.HasDbSet)
                {
                    // If we know for sure that we're loading these things (becuse the IncludeTree said so),
                    // but EF didn't load any, then add a blank collection so the client will delete any that already exist.
                    sb.Line($"}} else if (propVal{name} == null && tree?[nameof({dtoVar}.{dtoName})] != null) {{");
                    sb.Indented($"{dtoVar}.{dtoName} = new {targetDtoTypeName}[0];");
                    sb.Line("}");
                }
                else
                {
                    sb.Line("}");
                }

                setter = sb.ToString();
            }
            else
            {
                if (
                    property.Type.IsA(typeof(IReadOnlyCollection<>)) ||
                    property.Type.IsA(typeof(ICollection<>))
                )
                {
                    // Collection types which emit properly compatible property types on the DTO.
                    // No coersion to a real collection type required.
                    setter = $"{dtoVar}.{dtoName} = obj.{name};";
                }
                else
                {
                    // Collection is not really a collection. Probably an IEnumerable.
                    // We will have emitted the property type as ICollection,
                    // so we need to do a ToList() so that it can be assigned.
                    setter = $"{dtoVar}.{dtoName} = obj.{name}?.ToList();";
                }
            }

        }
        else if (property.Type.HasClassViewModel)
        {
            // Only check the includes tree for things that are in the database.
            // Otherwise, this would break IncludesExternal.
            string treeCheck = property.Type.ClassViewModel.HasDbSet
                ? $"if (tree == null || tree[nameof({dtoVar}.{dtoName})] != null)"
                : "";

            setter = $@"{treeCheck}
                {dtoVar}.{dtoName} = obj.{name}{mapCall()};
";
        }
        else
        {
            setter = $"{dtoVar}.{dtoName} = {TransformResponseValue(property.Type, $"obj.{name}")};";
        }

        var statement = GetPropertySetterConditional(property, property.SecurityInfo.Read, "obj", fixedContentView);
        return (statement, setter);
    }

    private (IEnumerable<string> conditionals, string setter) ModelToDtoFlattenedPropertySetter(FlattenedResponsePropertyViewModel property, string fixedContentView = null)
        => (
            fixedContentView is null
                ? GetContentViewConditionals(property.DeclaringClass, property.ContentViews, property.ExcludedContentViews)
                : [],
            $"this.{ResponsePropertyName(property)} = {TransformResponseValue(property.Type, property.AccessExpression("obj"))};");

    private (IEnumerable<string> conditionals, string setter) ModelToSummaryDtoPropertySetter(SummaryPropertyViewModel property)
        => (
            GetContentViewConditionals(property.Parent, property.ContentViews, property.ExcludedContentViews),
            $"this.{ResponsePropertyName(property)} = {TransformResponseValue(property.Type, property.AccessExpression("obj"))};");

    private bool ShouldEmitInBaseResponse(PropertyViewModel property)
        => !Model.UseContentViewResponseTypes
            || Model.GeneratedResponseContentViews.Count == 0
            || Model.GeneratedResponseContentViews.Any(property.IsMappedForContentView);

    private bool ShouldEmitInBaseResponse(FlattenedResponsePropertyViewModel property)
        => !Model.UseContentViewResponseTypes
            || Model.GeneratedResponseContentViews.Count == 0
            || Model.GeneratedResponseContentViews.Any(property.IsMappedForContentView);

    private static IEnumerable<string> GetContentViewConditionals(
        ClassViewModel declaringClass,
        IEnumerable<string> includes,
        IEnumerable<string> excludes)
    {
        string IncludesCheck(string include) => $"includes == \"{include.EscapeStringLiteralForCSharp()}\"";

        var includeList = includes.ToList();
        var excludeList = excludes.ToList();
        var explicitViewExcludes = declaringClass.DtoContentViews
            .Where(v => !v.Value && !includeList.Contains(v.Key, StringComparer.Ordinal))
            .Select(v => IncludesCheck(v.Key))
            .ToList();

        if (includeList.Count > 0)
        {
            yield return $"({string.Join(" || ", includeList.Select(IncludesCheck))})";
        }

        if (excludeList.Count > 0)
        {
            yield return $"!({string.Join(" || ", excludeList.Select(IncludesCheck))})";
        }

        if (explicitViewExcludes.Count > 0)
        {
            yield return $"!({string.Join(" || ", explicitViewExcludes)})";
        }
    }

    private string ResponsePropertyType(PropertyViewModel property, string fixedContentView = null)
    {
        if (property.UsesDtoReferenceSummary && property.Object is not null)
        {
            return $"{DtoNamespace}.{property.Object.SummaryDtoTypeName}";
        }

        var typeName = property.Type.NullableTypeForDto(isInput: false, dtoNamespace: DtoNamespace);
        if (fixedContentView is not null && property.Object is not null)
        {
            typeName = typeName.Replace(property.Object.ResponseDtoTypeName, GetResponseDtoTypeName(property, fixedContentView));
        }

        return typeName;
    }

    private string GetResponseDtoTypeName(PropertyViewModel property, string fixedContentView = null)
    {
        if (property.UsesDtoReferenceSummary && property.Object is not null)
        {
            return property.Object.SummaryDtoTypeName;
        }

        if (property.Object is null)
        {
            return string.Empty;
        }

        if (fixedContentView is not null && property.Object.HasResponseDtoTypeForContentView(fixedContentView))
        {
            return property.Object.ResponseDtoTypeNameForContentView(fixedContentView);
        }

        return property.Object.ResponseDtoTypeName;
    }

    private string ResponsePropertyName(PropertyViewModel property)
        => GetResponsePropertyName(property.Name, property.Type);

    private string ResponsePropertyName(FlattenedResponsePropertyViewModel property)
        => GetResponsePropertyName(property.Name, property.Type);

    private string ResponsePropertyName(SummaryPropertyViewModel property)
        => GetResponsePropertyName(property.Name, property.Type);

    private string GetResponsePropertyName(string propertyName, TypeViewModel propertyType)
    {
        if (Model.ResponseDtoDateTimeMode != DtoDateTimeMode.Utc || !propertyType.IsDateTime)
        {
            return propertyName;
        }

        var suffix = Model.ResponseDtoUtcSuffixCasing == UtcSuffixCasing.UpperCase ? "UTC" : "Utc";

        if (propertyName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return propertyName;
        }

        if (propertyName.EndsWith("Utc", StringComparison.OrdinalIgnoreCase))
        {
            return propertyName[..^3] + suffix;
        }

        return propertyName + suffix;
    }

    private string TransformResponseValue(TypeViewModel type, string sourceExpression)
    {
        if (Model.ResponseDtoDateTimeMode != DtoDateTimeMode.Utc || !type.IsDateTime)
        {
            return sourceExpression;
        }

        return sourceExpression.Contains("?.", StringComparison.Ordinal) || type.IsReferenceOrNullableValue
            ? $"{sourceExpression}?.ToUniversalTime()"
            : $"{sourceExpression}.ToUniversalTime()";
    }

    private string GetGeneratedResponseDtoTypeName(ClassViewModel model, string contentView)
        => contentView is not null && model.HasResponseDtoTypeForContentView(contentView)
            ? model.ResponseDtoTypeNameForContentView(contentView)
            : model.ResponseDtoTypeName;

    private static bool IsImmutableDictionary(TypeViewModel type) =>
        type.IsA(typeof(IImmutableDictionary<,>)) || type.IsA(typeof(ImmutableDictionary<,>));

    private static bool IsImmutableList(TypeViewModel type) =>
        type.IsA(typeof(IImmutableList<>)) || type.IsA(typeof(ImmutableList<>));

    private static string CollectionMaterializerForModel(TypeViewModel type) =>
        type.IsArray ? "ToArray" : IsImmutableList(type) ? "ToImmutableList" : "ToList";

    private string DictionaryDtoToModelExpression(TypeViewModel type, string sourceExpression)
    {
        var args = type.GenericArgumentsFor(typeof(IDictionary<,>))
            ?? throw new InvalidOperationException($"Dictionary type '{type}' is missing generic arguments.");

        return $"{sourceExpression}?.{(IsImmutableDictionary(type) ? "ToImmutableDictionary" : "ToDictionary")}(k => k.Key, v => {DictionaryValueDtoToModelExpression(args[1], "v.Value")})";
    }

    private string DictionaryValueDtoToModelExpression(TypeViewModel type, string sourceExpression)
    {
        if (type.IsDictionary)
        {
            return DictionaryDtoToModelExpression(type, sourceExpression);
        }

        var pureType = type.PureType;
        if (pureType.ClassViewModel is not null)
        {
            return $"{sourceExpression}?.MapToNew(context)";
        }

        return sourceExpression;
    }

    private string DictionaryModelToDtoExpression(TypeViewModel type, string sourceExpression, string fixedContentView = null)
    {
        var args = type.GenericArgumentsFor(typeof(IDictionary<,>))
            ?? throw new InvalidOperationException($"Dictionary type '{type}' is missing generic arguments.");

        return $"{sourceExpression}?.ToDictionary(k => k.Key, v => {DictionaryValueModelToDtoExpression(args[1], "v.Value", fixedContentView)})";
    }

    private string DictionaryValueModelToDtoExpression(TypeViewModel type, string sourceExpression, string fixedContentView = null)
    {
        if (type.IsDictionary)
        {
            return $"({type.NullableTypeForDto(isInput: false, dtoNamespace: DtoNamespace)}){DictionaryModelToDtoExpression(type, sourceExpression, fixedContentView)}";
        }

        var pureType = type.PureType;
        if (pureType.ClassViewModel is { } model)
        {
            var dtoTypeName = fixedContentView is not null && model.HasResponseDtoTypeForContentView(fixedContentView)
                ? model.ResponseDtoTypeNameForContentView(fixedContentView)
                : model.ResponseDtoTypeName;
            return $"{sourceExpression}.MapToDto<{model.FullyQualifiedName}, {dtoTypeName}>(context)";
        }

        return TransformResponseValue(type, sourceExpression);
    }
}
