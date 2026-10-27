/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 00:30:00 +08:00 中国标准时间 星期一
 * 描述：编码规则 API（/api/bas/code-rules）——仅查询与启停，规则内容不可修改
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 编码规则控制器。编码规则分段模板由后端统一发号器消费（N-1：前端不传码），
/// 页面能力限于查询与启停，禁止修改规则内容。
/// </summary>
/// <param name="service">编码规则服务。</param>
[ApiController]
[Route("api/bas/code-rules")]
public sealed class CodeRuleController(ICodeRuleService service) : ControllerBase
{
    /// <summary>编码规则扁平列表（业务类型 / 启停状态筛选）。</summary>
    /// <param name="bizType">业务类型（可选）。</param>
    /// <param name="status">启停状态数值（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>编码规则扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<CodeRuleRow>>> GetList(
        string? bizType, int? status, CancellationToken ct)
        => ApiResponse<IReadOnlyList<CodeRuleRow>>.Ok(
            await service.ListAsync(bizType, status, ct));

    /// <summary>启用指定业务类型的编码规则。</summary>
    /// <param name="bizType">业务类型。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>启用后的编码规则扁平行。</returns>
    [HttpPost("{bizType}/enable")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CODERULE_ENABLE", "启用了编码规则", TargetType = "CodeRule")]
    public async Task<ApiResponse<CodeRuleRow>> Enable(string bizType, CancellationToken ct)
        => ApiResponse<CodeRuleRow>.Ok(await service.EnableAsync(bizType, ct));

    /// <summary>停用指定业务类型的编码规则。</summary>
    /// <param name="bizType">业务类型。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>停用后的编码规则扁平行。</returns>
    [HttpPost("{bizType}/disable")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CODERULE_DISABLE", "停用了编码规则", TargetType = "CodeRule")]
    public async Task<ApiResponse<CodeRuleRow>> Disable(string bizType, CancellationToken ct)
        => ApiResponse<CodeRuleRow>.Ok(await service.DisableAsync(bizType, ct));
}
