/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 10:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 10:00:00 +08:00 中国标准时间 星期五
 * 描述：OpenAPI 文档装配扩展（仅 Development）：Scalar UI、Bearer 安全方案、Hashids Id schema 修正、Produces 补全
 */

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using Scalar.AspNetCore;

using System.Text.Json.Serialization.Metadata;

namespace Qlx.Host.OpenApi;

/// <summary>
/// OpenAPI 文档装配扩展。文档能力仅服务开发期联调，生产环境不注册任何文档端点（请求自然 404）。
/// 多版本预留：文档名 v1，物理路径 <c>/openapi/{documentName}.json</c>；未来新增版本只需
/// 再注册一份 <c>AddOpenApi("v2", ...)</c> 并在 Scalar 追加文档引用即可并存。
/// </summary>
public static class QlxOpenApiExtensions
{
    /// <summary>当前文档版本名（URL 段与 Scalar 默认引用共用）。</summary>
    private const string DocumentName = "v1";

    /// <summary>Bearer 安全方案在 Components 中的注册键。</summary>
    private const string BearerSchemeId = "Bearer";

    // 文件流端点返回 File(...) 且未标注 [Produces]，ApiExplorer 无法推断真实契约；
    // Produces 补全必须跳过，避免把文件流误标为 application/json（新增同类端点时同步补充）
    private static readonly HashSet<string> s_fileStreamRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET api/support/files/download/{id}",
        "GET api/support/files/preview/{id}",
    };

    /// <summary>
    /// 注册 OpenAPI 文档生成（仅 Development）。装配内容：文档信息、Bearer 安全方案、
    /// Hashids Id schema 修正（与 QlxIdJsonMetadata / HashidsLongModelBinder 双通道约定严格对齐）、
    /// 未显式标注 [Produces] 操作的 application/json 补全。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="environment">宿主环境；非 Development 直接跳过注册。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxOpenApi(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment())
        {
            return services;
        }

        _ = services.AddOpenApi(DocumentName, options =>
        {
            _ = options.AddDocumentTransformer(static (document, context, cancellationToken) =>
            {
                document.Info ??= new OpenApiInfo();
                document.Info.Title = "Qlx ERP v3 API";
                document.Info.Version = DocumentName;
                document.Info.Description = "ERPv3 模块化单体 API。访问令牌经 POST /connect/token 获取（OpenIddict）；" +
                    "业务 Id（内部雪花 long）在 HTTP 边界一律为 Hashids 混淆字符串（schema 以 format: hashids 标记）。";

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerSchemeId] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "OpenIddict access token。经 POST /connect/token 获取，按 Authorization: Bearer {token} 携带。",
                };
                document.Security ??= [];
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference(BearerSchemeId, document), [] },
                });
                return Task.CompletedTask;
            });

            _ = options.AddOperationTransformer(static (operation, context, cancellationToken) =>
            {
                ApplyHashidsParameters(operation, context);
                ApplyProducesFallback(operation, context);
                return Task.CompletedTask;
            });

            _ = options.AddSchemaTransformer(static (schema, context, cancellationToken) =>
            {
                // 与 QlxIdJsonMetadata.IsBusinessId 同一约定：long / long? 且命名为 Id / *Id 的属性
                // 在 JSON 边界为 Hashids 字符串，schema 同步修正，避免文档把混淆 Id 暴露为 int64
                JsonPropertyInfo? property = context.JsonPropertyInfo;
                if (property is null)
                {
                    return Task.CompletedTask;
                }

                var isLong = property.PropertyType == typeof(long) || property.PropertyType == typeof(long?);
                var isIdName = string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)
                    || property.Name.EndsWith("Id", StringComparison.Ordinal);
                if (isLong && isIdName && schema is OpenApiSchema concreteSchema)
                {
                    var keepNull = concreteSchema.Type?.HasFlag(JsonSchemaType.Null) == true;
                    concreteSchema.Type = keepNull ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
                    concreteSchema.Format = "hashids";
                }
                return Task.CompletedTask;
            });
        });

        return services;
    }

    /// <summary>
    /// 映射 OpenAPI JSON 与 Scalar UI（仅 Development；生产环境不映射任何文档端点，请求自然 404）。
    /// </summary>
    /// <param name="app">应用管线。</param>
    /// <returns>应用管线。</returns>
    public static WebApplication MapQlxApiDocs(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        _ = app.MapOpenApi();
        _ = app.MapScalarApiReference("/docs");
        return app;
    }

    // 路由/查询中的 long / long? 由 HashidsLongModelBinder 全局解码，文档参数同步修正为 Hashids 字符串
    private static void ApplyHashidsParameters(OpenApiOperation operation, OpenApiOperationTransformerContext context)
    {
        if (operation.Parameters is null || operation.Parameters.Count == 0)
        {
            return;
        }

        foreach (IOpenApiParameter parameter in operation.Parameters)
        {
            if (parameter is not OpenApiParameter concrete)
            {
                continue;
            }

            ApiParameterDescription? description = context.Description.ParameterDescriptions
                .FirstOrDefault(p => string.Equals(p.Name, concrete.Name, StringComparison.OrdinalIgnoreCase));
            if (description is null
                || (description.Type != typeof(long) && description.Type != typeof(long?))
                || (description.Source != BindingSource.Path && description.Source != BindingSource.Query))
            {
                continue;
            }

            if (concrete.Schema is not OpenApiSchema parameterSchema)
            {
                concrete.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "hashids" };
                continue;
            }

            var keepNull = parameterSchema.Type?.HasFlag(JsonSchemaType.Null) == true;
            parameterSchema.Type = keepNull ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
            parameterSchema.Format = "hashids";
        }
    }

    // 未显式标注 [Produces] 的操作补全 application/json；SSE / 文件流 / 无内容响应跳过
    private static void ApplyProducesFallback(OpenApiOperation operation, OpenApiOperationTransformerContext context)
    {
        var hasExplicitProduces = context.Description.ActionDescriptor.FilterDescriptors
            .Select(filter => filter.Filter)
            .OfType<ProducesAttribute>()
            .Any();
        if (hasExplicitProduces)
        {
            return;
        }

        var routeKey = $"{context.Description.HttpMethod} {context.Description.RelativePath}";
        if (s_fileStreamRoutes.Contains(routeKey))
        {
            return;
        }

        if (operation.Responses is null)
        {
            return;
        }

        foreach ((string? statusCode, IOpenApiResponse? response) in operation.Responses)
        {
            if (statusCode is "204" or "304" || response is not OpenApiResponse concrete)
            {
                continue;
            }

            concrete.Content ??= new Dictionary<string, OpenApiMediaType>();
            if (concrete.Content.Count == 0)
            {
                concrete.Content["application/json"] = new OpenApiMediaType();
            }
        }
    }
}
