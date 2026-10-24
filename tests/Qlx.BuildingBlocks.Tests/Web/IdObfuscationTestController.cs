/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22
 * 描述：Id 混淆 HTTP 集成测试专用控制器——与业务代码解耦，仅用于验证全局 JSON 约定与 ModelBinder。
 *       通过 ApplicationPart 加载到 TestServer，覆盖 JSON Body / 路由 / 查询三种入参通道。
 */

using Microsoft.AspNetCore.Mvc;

namespace Qlx.BuildingBlocks.Tests.Web;

/// <summary>
/// Id 混淆边界测试控制器：验证全局 JSON 元数据约定（Id 命名属性自动混淆）
/// 与全局 ModelBinder（路由/查询 long 自动解码）在真实 HTTP 管线中的行为。
/// </summary>
[ApiController]
[Route("api/test/id-obfuscation")]
public sealed class IdObfuscationTestController : ControllerBase
{
    /// <summary>验证 JSON Body 反序列化：Id / ParentId 自动从混淆串解码为真实 long。</summary>
    [HttpPost("body")]
    public IActionResult PostBody([FromBody] IdPayload payload)
        => Ok(new IdPayload(payload.Id, payload.ParentId));

    /// <summary>验证路由参数 long 自动解码。</summary>
    [HttpGet("route/{id}")]
    public IActionResult GetByRoute(long id)
        => Ok(new RouteEcho(id));

    /// <summary>验证查询参数：可空 orgId 缺省为 null，必填 id 缺省触发 400。</summary>
    [HttpGet("query")]
    public IActionResult GetByQuery([FromQuery] long? orgId, [FromQuery] long id)
        => Ok(new QueryEcho(orgId, id));

    /// <summary>验证多段路由 Id 同时解码。</summary>
    [HttpGet("route/{parentId}/child/{id}")]
    public IActionResult GetByRouteMulti(long parentId, long id)
        => Ok(new MultiRouteEcho(parentId, id));

    /// <summary>验证可空路由 Id 缺省（无该段）时不触发绑定。</summary>
    [HttpGet("route-nullable")]
    public IActionResult GetNullableRoute([FromQuery] long? orgId)
        => Ok(new QueryEcho(orgId, 0));

    /// <summary>验证嵌套对象：每一层的 Id 命名属性都自动混淆，非 Id long 保持数值。</summary>
    [HttpPost("nested-body")]
    public IActionResult PostNestedBody([FromBody] OuterPayload payload)
        => Ok(new OuterPayload(payload.Id, payload.Total, payload.Child));

    /// <summary>验证对象集合：列表元素中的 Id 同样自动混淆。</summary>
    [HttpPost("list-body")]
    public IActionResult PostListBody([FromBody] ListPayload payload)
        => Ok(new ListPayload(payload.Items, payload.Total));
}

/// <summary>JSON Body 载荷：Id 与 ParentId 均需按约定混淆。</summary>
public sealed record IdPayload(long Id, long? ParentId);

/// <summary>路由参数回显载荷。</summary>
public sealed record RouteEcho(long Id);

/// <summary>查询参数回显载荷。</summary>
public sealed record QueryEcho(long? OrgId, long Id);

/// <summary>多段路由回显载荷。</summary>
public sealed record MultiRouteEcho(long ParentId, long Id);

/// <summary>嵌套对象载荷：外层 Id + 非 Id 的 Total，内层 Id/ParentId。</summary>
public sealed record OuterPayload(long Id, long Total, InnerPayload Child);

/// <summary>嵌套子对象载荷：Id 混淆、Amount 为非 Id 数值。</summary>
public sealed record InnerPayload(long Id, long? ParentId, long Amount);

/// <summary>对象集合载荷：每个元素的 Id 均需混淆，Total 为非 Id 数值。</summary>
public sealed record ListPayload(IReadOnlyList<IdPayload> Items, long Total);
