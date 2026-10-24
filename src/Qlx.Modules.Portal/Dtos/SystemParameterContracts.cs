/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 描述：系统参数管理 DTO 契约：查询请求 / 保存请求 / 响应项（record + init 不可变）
 */

using Qlx.BuildingBlocks.Api;

namespace Qlx.Modules.Portal.Dtos;

/// <summary>参数分页查询请求（关键字过滤 ParamKey / Description）。</summary>
public sealed class SystemParameterQueryRequest : PagedRequest
{
    /// <summary>关键字（可选，模糊匹配参数键与说明）。</summary>
    public string? Keyword { get; set; }
}

/// <summary>参数保存请求（Upsert：存在即更新，不存在即新建）。</summary>
public sealed record SaveSystemParameterRequest
{
    /// <summary>参数值。</summary>
    public required string ParamValue { get; init; }

    /// <summary>参数说明（可选）。</summary>
    public string? Description { get; init; }
}

/// <summary>参数响应项。</summary>
public sealed record SystemParameterItem
{
    /// <summary>参数键。</summary>
    public required string ParamKey { get; init; }

    /// <summary>参数值。</summary>
    public required string ParamValue { get; init; }

    /// <summary>参数说明。</summary>
    public string? Description { get; init; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>最近更新人。</summary>
    public string? UpdatedBy { get; init; }
}
