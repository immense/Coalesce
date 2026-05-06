using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.CodeGeneration.Vue.Utils;
using IntelliTect.Coalesce.TypeDefinition;
using IntelliTect.Coalesce.Utilities;

namespace IntelliTect.Coalesce.CodeGeneration.Vue.Generators;

public class TsModels : StringBuilderFileGenerator<ReflectionRepository>
{
    public TsModels(GeneratorServices services) : base(services)
    {
    }

    public override Task<string> BuildOutputAsync()
    {
        var b = new TypeScriptCodeBuilder(indentSize: 2);
        b.Line("import * as metadata from './metadata.g'");
        b.Line("import { convertToModel, mapToModel, reactiveDataSource } from 'coalesce-vue/lib/model'");
        b.Line("import type { Model, DataSource } from 'coalesce-vue/lib/model'");
        b.Line("import type { ClassType } from 'coalesce-vue/lib/metadata'");
        b.Line();

        foreach (var model in Model.ClientEnums.OrderBy(e => e.ClientTypeName))
        {
            using (b.Block($"export enum {model.ClientTypeName}"))
            {
                foreach (var value in model.EnumValues)
                {
                    b.DocComment(value.Comment ?? value.Description);
                    if (model.IsStringEnum)
                    {
                        b.Line($"{value.Name} = \"{value.Name}\",");
                    }
                    else
                    {
                        b.Line($"{value.Name} = {value.Value},");
                    }
                }
            }

            b.Line();
            b.Line();
        }

        HashSet<ClassViewModel> written = new();
        foreach (var model in Model.ClientClasses
            .OrderByDescending(c => c.IsDbMappedType) // Entity types first
            .ThenBy(e => e.ClientTypeName))
        {
            WriteModel(b, model, written);
        }

        foreach (var model in Model.ClientClasses
            .Where(m => m.ShouldGenerateSummaryDto)
            .OrderBy(e => e.ClientTypeName))
        {
            WriteSummaryModel(b, model);
        }


        using (b.Block("declare module \"coalesce-vue/lib/model\""))
        {
            using (b.Block("interface EnumTypeLookup"))
            {
                foreach (var model in Model.ClientEnums.OrderBy(e => e.ClientTypeName))
                {
                    b.Line($"{model.ClientTypeName}: {model.ClientTypeName}");
                }
            }

            using (b.Block("interface ModelTypeLookup"))
            {
                foreach (var model in Model.ClientClasses.OrderBy(e => e.ClientTypeName))
                {
                    b.Line($"{model.ClientTypeName}: {model.ClientTypeName}");
                }
                foreach (var model in Model.ClientClasses.Where(m => m.ShouldGenerateSummaryDto).OrderBy(e => e.ClientTypeName))
                {
                    b.Line($"{model.SummaryViewModelClassName}: {model.SummaryViewModelClassName}");
                }
            }
        }

        return Task.FromResult(b.ToString());
    }

    private void WriteModel(TypeScriptCodeBuilder b, ClassViewModel model, HashSet<ClassViewModel> written)
    {
        var extends = "Model";
        if (model.ClientBaseTypes.FirstOrDefault() is ClassViewModel baseType)
        {
            extends = $"{baseType.ViewModelClassName}";
            if (!written.Contains(baseType)) WriteModel(b, baseType, written);
        }
        extends += $"<{string.Join(" | ", model.ClientDerivedTypes.Prepend(model).Select(c => $"typeof metadata.{c.ViewModelClassName}"))}>";

        if (!written.Add(model)) return;

        var name = model.ViewModelClassName;

        string iface = $"export interface {name}";
        var derived = model.ClientDerivedTypes.ToList();
        if (derived.Any())
        {
            iface +=
                "<TMeta extends ClassType = " +
                string.Join(" | ", model.ClientDerivedTypes.Prepend(model).Select(c => $"typeof metadata.{c.ViewModelClassName}")) +
                "> extends Model<TMeta>";
        }
        else
        {
            iface +=
                $" extends Model<typeof metadata.{name}>";
        }

        using (b.Block(iface))
        {
            foreach (var prop in model.ClientProperties)
            {
                b.DocComment(prop.Comment ?? prop.Description);
                var typeString = GetModelPropertyType(prop);
                b.Line($"{prop.JsVariable}: {typeString} | null");
            }
        }

        using (b.Block($"export class {name}"))
        {
            WriteConsts(b, model);

            b.DocComment($"Mutates the input object and its descendants into a valid {name} implementation.");
            using (b.Block($"static convert(data?: Partial<{name}>): {name}"))
            {
                b.Line($"return convertToModel<{name}>(data || {{}}, metadata.{name}) ");
            }

            b.DocComment($"Maps the input object and its descendants to a new, valid {name} implementation.");
            using (b.Block($"static map(data?: Partial<{name}>): {name}"))
            {
                b.Line($"return mapToModel<{name}>(data || {{}}, metadata.{name}) ");
            }

            b.Line();
            b.Append($"static [Symbol.hasInstance](x: any) {{ return x?.$metadata === metadata.{name}");
            b.Line(derived.Any() ?
                $" || x?.$metadata.baseTypes?.includes(metadata.{name}); }}" :
                $"; }}"
            );

            b.DocComment($"Instantiate a new {name}, optionally basing it on the given data.");
            using (b.Block($"constructor(data?: Partial<{name}> | {{[k: string]: any}})"))
            {
                b.Line($"Object.assign(this, {name}.map(data || {{}}));");
            }
        }

        var dataSources = model.ClientDataSources(Model);
        if (dataSources.Any())
        {
            using var _ = b.Block($"export namespace {name}");
            using var _2 = b.Block($"export namespace DataSources");

            foreach (var source in dataSources)
            {
                b.DocComment(source.Comment, true);
                var sourceMeta = $"metadata.{name}.dataSources.{source.ClientTypeName.ToCamelCase()}";

                using (b.Block($"export class {source.ClientTypeName} implements DataSource<typeof {sourceMeta}>"))
                {
                    b.Line($"readonly $metadata = {sourceMeta}");

                    WriteConsts(b, source);

                    if (source.DataSourceParameters.Any())
                    {
                        foreach (var param in source.DataSourceParameters)
                        {
                            b.DocComment(param.Comment ?? param.Description);
                            var typeString = new VueType(param.Type).TsType();
                            b.Line($"{param.JsVariable}: {typeString} | null = null");
                        }

                        b.Line();
                        using (b.Block($"constructor(params?: Omit<Partial<{source.ClientTypeName}>, '$metadata'>)"))
                        {
                            b.Line($"if (params) Object.assign(this, params);");
                            b.Line($"return reactiveDataSource(this);");
                        }
                    }
                }
            }
        }

        b.Line();
        b.Line();
    }

    static internal void WriteConsts(TypeScriptCodeBuilder b, ClassViewModel vm)
    {
        if (vm.ClientConsts.Any()) b.Line();

        foreach (var value in vm.ClientConsts)
        {
            b.Line($"static {value.Name.ToCamelCase()} = {value.ValueLiteralForTypeScript("")}");
        }
    }

    private void WriteSummaryModel(TypeScriptCodeBuilder b, ClassViewModel model)
    {
        var name = model.SummaryViewModelClassName;

        using (b.Block($"export interface {name} extends Model<typeof metadata.{name}>"))
        {
            var primaryKey = model.PrimaryKey
                ?? throw new InvalidOperationException($"Summary model generation for {model.FullyQualifiedName} requires a primary key.");

            b.DocComment(primaryKey.Comment ?? primaryKey.Description);
            b.Line($"{primaryKey.JsVariable}: {new VueType(primaryKey.Type.NullableValueUnderlyingType).TsType()} | null");

            foreach (var prop in model.SummaryProperties)
            {
                b.DocComment(prop.LeafProperty.Comment ?? prop.LeafProperty.Description);
                b.Line($"{prop.Name.ToCamelCase()}: {new VueType(prop.Type.NullableValueUnderlyingType).TsType()} | null");
            }
        }

        using (b.Block($"export class {name}"))
        {
            b.DocComment($"Mutates the input object and its descendants into a valid {name} implementation.");
            using (b.Block($"static convert(data?: Partial<{name}>): {name}"))
            {
                b.Line($"return convertToModel<{name}>(data || {{}}, metadata.{name}) ");
            }

            b.DocComment($"Maps the input object and its descendants to a new, valid {name} implementation.");
            using (b.Block($"static map(data?: Partial<{name}>): {name}"))
            {
                b.Line($"return mapToModel<{name}>(data || {{}}, metadata.{name}) ");
            }

            b.Line();
            b.Line($"static [Symbol.hasInstance](x: any) {{ return x?.$metadata === metadata.{name}; }}");

            b.DocComment($"Instantiate a new {name}, optionally basing it on the given data.");
            using (b.Block($"constructor(data?: Partial<{name}> | {{[k: string]: any}})"))
            {
                b.Line($"Object.assign(this, {name}.map(data || {{}}));");
            }
        }

        b.Line();
    }

    private string GetModelPropertyType(PropertyViewModel prop)
    {
        if (prop.UsesDtoReferenceSummary && prop.Object is not null)
        {
            return prop.Object.SummaryViewModelClassName;
        }

        return new VueType(prop.Type.NullableValueUnderlyingType).TsType();
    }
}
