using IntelliTect.Coalesce.CodeGeneration.Generation;
using IntelliTect.Coalesce.TypeDefinition;
using IntelliTect.Coalesce.CodeGeneration.Api.BaseGenerators;
using IntelliTect.Coalesce.Utilities;
using IntelliTect.Coalesce.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.WebUtilities;

namespace IntelliTect.Coalesce.CodeGeneration.Api.Generators;

public class ModelApiController : ApiController
{
    public ModelApiController(GeneratorServices services) : base(services) { }

    public override void BuildOutput(CSharpCodeBuilder b)
    {
        WriteNamespaces(b);

        b.Line();
        using (b.Block($"namespace {GetAreaNamespace()}.Api"))
        {
            WriteControllerRouteAttribute(b);
            b.Line("[Authorize]");
            b.Line("[ServiceFilter(typeof(IApiActionFilter))]");

            b.Line($"public partial class {Model.ApiControllerClassName} ");
            if (Model.DbContext != null)
            {
                b.Indented($": BaseApiController<{Model.BaseViewModel.FullyQualifiedName}, {Model.ParameterDtoTypeName}, {Model.ResponseDtoTypeName}, {Model.DbContext.Type.FullyQualifiedName}>");
            }
            else
            {
                b.Indented($": BaseApiController<{Model.BaseViewModel.FullyQualifiedName}, {Model.ParameterDtoTypeName}, {Model.ResponseDtoTypeName}>");
            }


            // b.Block() has no contents here because we put the base class on a separate line to avoid really long lines.
            b.Line("{");
            using (b.Indented())
            {
                WriteClassContents(b, Model.SecurityInfo);
            }
            b.Line("}");
        }
    }

    private void WriteClassContents(CSharpCodeBuilder b, ClassSecurityInfo securityInfo)
    {
        var primaryKeyParameter = $"{Model.PrimaryKey.Type.FullyQualifiedName} id";
        var dataSourceParameter = $"IDataSource<{Model.BaseViewModel.FullyQualifiedName}> dataSource";
        var behaviorsParameter = $"IBehaviors<{Model.BaseViewModel.FullyQualifiedName}> behaviors";
        string defaultGetIncludes = DefaultIncludesLiteral(Model.DefaultGetDtoIncludes);
        string defaultListIncludes = DefaultIncludesLiteral(Model.DefaultListDtoIncludes);
        string defaultCountIncludes = DefaultIncludesLiteral(Model.DefaultCountDtoIncludes);
        string defaultSaveIncludes = DefaultIncludesLiteral(Model.DefaultSaveDtoIncludes);
        string defaultBulkSaveIncludes = DefaultIncludesLiteral(Model.DefaultBulkSaveDtoIncludes);
        string defaultDeleteIncludes = DefaultIncludesLiteral(Model.DefaultDeleteDtoIncludes);
        string getResponseType = Model.GetStandardActionResponseDtoTypeName(Model.DefaultGetDtoIncludes);
        string listResponseType = Model.GetStandardActionResponseDtoTypeName(Model.DefaultListDtoIncludes);
        string saveResponseType = Model.GetStandardActionResponseDtoTypeName(Model.DefaultSaveDtoIncludes);
        string bulkSaveResponseType = Model.GetStandardActionResponseDtoTypeName(Model.DefaultBulkSaveDtoIncludes);
        string deleteResponseType = Model.GetStandardActionResponseDtoTypeName(Model.DefaultDeleteDtoIncludes);
        bool fixGetIncludes = Model.ShouldUseContentViewResponseType(Model.DefaultGetDtoIncludes);
        bool fixListIncludes = Model.ShouldUseContentViewResponseType(Model.DefaultListDtoIncludes);
        bool fixCountIncludes = Model.ShouldUseContentViewResponseType(Model.DefaultCountDtoIncludes);
        bool fixSaveIncludes = Model.ShouldUseContentViewResponseType(Model.DefaultSaveDtoIncludes);
        bool fixBulkSaveIncludes = Model.ShouldUseContentViewResponseType(Model.DefaultBulkSaveDtoIncludes);
        bool fixDeleteIncludes = Model.ShouldUseContentViewResponseType(Model.DefaultDeleteDtoIncludes);
#pragma warning disable CS0618 // Type or member is obsolete
        var accessModifier = Model.ApiActionAccessModifier;
#pragma warning restore CS0618 // Type or member is obsolete

        if (Model.IsCustomDto)
        {
            var declaredForAttr = $"[DeclaredFor(typeof({Model.FullyQualifiedName}))] ";
            dataSourceParameter = declaredForAttr + dataSourceParameter;
            behaviorsParameter = declaredForAttr + behaviorsParameter;
        }

        using (b.Block(Model.DbContext != null
            ? $"public {Model.ApiControllerClassName}(CrudContext<{Model.DbContext.Type.FullyQualifiedName}> context) : base(context)"
            : $"public {Model.ApiControllerClassName}(CrudContext context) : base(context)"
        ))
        {
            b.Line($"GeneratedForClassViewModel = context.ReflectionRepository.GetClassViewModel<{Model.FullyQualifiedName}>();");
        }

        if (securityInfo.IsReadAllowed())
        {
            // ENDPOINT: /get/{id}
            b.Line();
            b.Line("""[HttpGet("get/{id}")]""");
            b.Line($"{securityInfo.Read.MvcAnnotation()}");
            b.Line($"{accessModifier} virtual Task<ItemResult<{getResponseType}>> Get(");
            b.Indented($"{primaryKeyParameter},");
            b.Indented($"[FromQuery] DataSourceParameters parameters,");
            b.Indented($"{dataSourceParameter})");
            b.Indented($"=> GetImplementation<{getResponseType}>(id, {(fixGetIncludes ? "ApplyFixedIncludes" : "ApplyDefaultIncludes")}(parameters, {defaultGetIncludes}), dataSource);");

            // ENDPOINT: /list
            b.Line();
            b.Line("""[HttpGet("list")]""");
            b.Line($"{securityInfo.Read.MvcAnnotation()}");
            b.Line($"{accessModifier} virtual Task<ListResult<{listResponseType}>> List(");
            b.Indented($"[FromQuery] ListParameters parameters,");
            b.Indented($"{dataSourceParameter})");
            b.Indented($"=> ListImplementation<{listResponseType}>({(fixListIncludes ? "ApplyFixedIncludes" : "ApplyDefaultIncludes")}(parameters, {defaultListIncludes}), dataSource);");

            // ENDPOINT: /count
            b.Line();
            b.Line("""[HttpGet("count")]""");
            b.Line($"{securityInfo.Read.MvcAnnotation()}");
            b.Line($"{accessModifier} virtual Task<ItemResult<int>> Count(");
            b.Indented($"[FromQuery] FilterParameters parameters,");
            b.Indented($"{dataSourceParameter})");
            b.Indented($"=> CountImplementation({(fixCountIncludes ? "ApplyFixedIncludes" : "ApplyDefaultIncludes")}(parameters, {defaultCountIncludes}), dataSource);");
        }

        if (securityInfo.Save.IsAllowed())
        {
            // ENDPOINT: /save
            b.Line();
            b.Line("""[HttpPost("save")]""");
            b.Line("""[Consumes("application/x-www-form-urlencoded", "multipart/form-data")]""");
            b.Line($"{securityInfo.Save.MvcAnnotation()}");
            b.Line($"{accessModifier} virtual Task<ItemResult<{saveResponseType}>> Save(");
            b.Indented($"[FromForm] {Model.ParameterDtoTypeName} dto,");
            b.Indented($"[FromQuery] DataSourceParameters parameters,");
            b.Indented($"{dataSourceParameter},");
            b.Indented($"{behaviorsParameter})");
            b.Indented($"=> SaveImplementation<{saveResponseType}>(dto, {(fixSaveIncludes ? "ApplyFixedIncludes" : "ApplyDefaultIncludes")}(parameters, {defaultSaveIncludes}), dataSource, behaviors);");

            b.Line();
            b.Line("""[HttpPost("save")]""");
            b.Line("""[Consumes("application/json")]""");
            b.Line($"{securityInfo.Save.MvcAnnotation()}");
            b.Line($"{accessModifier} virtual Task<ItemResult<{saveResponseType}>> SaveFromJson(");
            b.Indented($"[FromBody] {Model.ParameterDtoTypeName} dto,");
            b.Indented($"[FromQuery] DataSourceParameters parameters,");
            b.Indented($"{dataSourceParameter},");
            b.Indented($"{behaviorsParameter})");
            b.Indented($"=> SaveImplementation<{saveResponseType}>(dto, {(fixSaveIncludes ? "ApplyFixedIncludes" : "ApplyDefaultIncludes")}(parameters, {defaultSaveIncludes}), dataSource, behaviors);");
        }

        if (Model.DbContext != null && securityInfo.IsReadAllowed() && (securityInfo.Save.IsAllowed() || securityInfo.IsDeleteAllowed()))
        {
            // Bulk saves are only emitted for readable, mutable roots.
            // BulkSaveImplementation still checks save/delete permissions for each entity at runtime,
            // but omitting the endpoint entirely for immutable roots keeps read-only slices read-only.

            // ENDPOINT: /bulkSave
            b.Line();
            b.Line("[HttpPost(\"bulkSave\")]");
            b.Line($"{securityInfo.Read.MvcAnnotation()}");
            b.Line($"{accessModifier} virtual Task<ItemResult<{bulkSaveResponseType}>> BulkSave(");
            b.Indented($"[FromBody] BulkSaveRequest dto,");
            b.Indented($"[FromQuery] DataSourceParameters parameters,");
            b.Indented($"{dataSourceParameter},");
            b.Indented($"[FromServices] IDataSourceFactory dataSourceFactory,");
            b.Indented($"[FromServices] IBehaviorsFactory behaviorsFactory)");
            b.Indented($"=> BulkSaveImplementation<{bulkSaveResponseType}>(dto, {(fixBulkSaveIncludes ? "ApplyFixedIncludes" : "ApplyDefaultIncludes")}(parameters, {defaultBulkSaveIncludes}), dataSource, dataSourceFactory, behaviorsFactory);");
        }

        if (securityInfo.IsDeleteAllowed())
        {
            // ENDPOINT: /delete/{id}
            b.Line();
            b.Line("[HttpPost(\"delete/{id}\")]");
            b.Line($"{securityInfo.Delete.MvcAnnotation()}");
            b.Line($"{accessModifier} virtual Task<ItemResult<{deleteResponseType}>> Delete(");
            b.Indented($"{primaryKeyParameter},");
            b.Indented($"{behaviorsParameter},");
            b.Indented($"{dataSourceParameter})");
            b.Indented($"=> DeleteImplementation<{deleteResponseType}>(id, {(fixDeleteIncludes ? "ApplyFixedIncludes" : "ApplyDefaultIncludes")}(new DataSourceParameters(), {defaultDeleteIncludes}), dataSource, behaviors);");
        }

        if (Model.ClientMethods.Any())
        {
            b.Line();
            b.Line("// Methods from data class exposed through API Controller.");
        }

        foreach (var method in Model.ClientMethods)
        {
            // Write the action for parameterless methods and formdata methods
            using (WriteControllerActionSignature(b, method))
            {
                WriteMethodBody(b, method);
            }

            if (method.HasHttpRequestBody)
            {
                // Write the JSON-accepting endpoint if there is a body.
                using (WriteControllerActionJsonSignature(b, method))
                {
                    WriteMethodBody(b, method);
                }
            }
        }
    }

    private void WriteMethodBody(CSharpCodeBuilder b, MethodViewModel method)
    {
        if (method.IsStatic)
        {
            WriteMethodInvocation(b, method, method.Parent.FullyQualifiedName);
        }
        else
        {
            WriteInstanceMethodTargetLoading(b, method);
            WriteEtagProcessing(b, method);
            WriteMethodInvocation(b, method, "item");
        }

        WriteMethodResultProcessBlock(b, method);
    }

    private static void WriteEtagProcessing(CSharpCodeBuilder b, MethodViewModel method)
    {
        var varyByProperty = method.VaryByProperty;
        if (varyByProperty is null) return;

        b.Line();

        b.Line($"var _currentVaryValue = item.{varyByProperty.Name};");
        using (b.Block("if (_currentVaryValue != default)"))
        {
            string eTagString = varyByProperty.Type switch
            {
                { IsByteArray: true } => "_currentVaryValue",
                { IsString: true } => "System.Text.Encoding.UTF8.GetBytes(_currentVaryValue)",
                _ => "System.Text.Encoding.UTF8.GetBytes(_currentVaryValue.ToString())",
            };

            // Always base64 the etag because otherwise its just too hard to prevent invalid characters leaking in.
            // Quotes, mismatches slashes (which mess up escape sequences), and ASCII control characters all make this hard
            eTagString = $"{typeof(Base64UrlTextEncoder).FullName}.Encode({eTagString})";

            // I'm so sorry about the escape sequence here.
            // We have to have code that replaces quotes within the value with slash-escaped quotes.
            // In order to write that code in C#, we have to escape the literal quotes and slashes.
            // And then we have to escape THOSE slashes AGAIN because we're writing a C# string that
            // will itself output a C# string.
            /* test cases for this (should write some unit tests at some point i guess?)
             * "foo"
             * "foo\""
             * "foo\\\"" // this case fails if we don't also escape slashes. just escaping quotes isnt enough.
             * "foo\\\\\""
             */
            b.Line($"var _expectedEtagHeader = new Microsoft.Net.Http.Headers.EntityTagHeaderValue('\"' + {eTagString} + '\"');");

            if (varyByProperty.IsClientProperty)
            {
                // Max age of zero forces a re-request always,
                // but if the etag matches we can still return a 304.
                b.Line("var _cacheControlHeader = new Microsoft.Net.Http.Headers.CacheControlHeaderValue { Private = true, MaxAge = TimeSpan.Zero };");

                string varyValueComparison = varyByProperty.Type switch
                {
                    // JS dates only have millisecond precision, so truncate the server value to milliseconds.
                    // Truncation is what happens on the JS side to anything more precise than a millisecond.
                    { IsDateTimeOffset: true } =>
                        "_currentVaryValue.AddTicks(-_currentVaryValue.Ticks % TimeSpan.TicksPerMillisecond) == etag",
                    { IsByteArray: true } => "_currentVaryValue.SequenceEqual(etag)",
                    _ => "_currentVaryValue == etag"
                };

                using (b.Block($"if (etag != default && {varyValueComparison})"))
                {
                    // If the client is including the correct etag in the querystring,
                    // we can give them a long term cache duration
                    // so they can avoid having to make any request at all
                    // (with just etag alone, the client still has to check if
                    // the etag is still valid, but including the correct hash
                    // in the querystring this means the client has prior knowledge
                    // about the current version via the VaryByProperty's value).

                    var clientCacheDurationSeconds = method
                        .GetAttribute<ExecuteAttribute>()
                        ?.GetValue(a => a.ClientCacheDurationSeconds);
                    if (clientCacheDurationSeconds != null)
                    {
                        b.Line($"_cacheControlHeader.MaxAge = TimeSpan.FromSeconds({clientCacheDurationSeconds.Value});");
                    }
                    else
                    {
                        b.Line("_cacheControlHeader.MaxAge = TimeSpan.FromDays(30);");
                    }
                }

                b.Line("Response.GetTypedHeaders().CacheControl = _cacheControlHeader;");
            }

            b.Line("Response.GetTypedHeaders().ETag = _expectedEtagHeader;");

            using (b.Block("if (Request.GetTypedHeaders().IfNoneMatch.Any(value => value.Compare(_expectedEtagHeader, true)))"))
            {
                // The client already has the current response cached. Tell them to use it.
                b.Line("return StatusCode(StatusCodes.Status304NotModified);");
            }
        }
        b.Line();
    }

    private static string DefaultIncludesLiteral(string includes)
        => string.IsNullOrWhiteSpace(includes)
            ? "null"
            : includes.QuotedStringLiteralForCSharp();
}
