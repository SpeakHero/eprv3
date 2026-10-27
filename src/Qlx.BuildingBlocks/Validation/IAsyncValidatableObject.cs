/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 22:10:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 22:10:00 +08:00 中国标准时间 星期一
 * 描述：BCL IValidatableObject 的异步对应物——支持在对象级校验中执行异步规则并接入统一验证管线
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Validation;

/// <summary>
/// BCL <see cref="IValidatableObject"/> 的异步对应物，语义与其完全一致：
/// 实现方在 <see cref="ValidateAsync"/> 中执行跨字段 / 对象级（含异步）校验规则，
/// 通过返回空集合或不含失败项的结果集合表示验证通过，返回的每一项即一条失败结果。
/// </summary>
/// <remarks>
/// <para>
/// 服务解析（<see cref="ValidationContext.GetService(Type)"/>）与 <see cref="ValidationContext.Items"/> 的行为与 MVC 模型验证管线一致：
/// 服务容器可解析已注册的 DI 服务，Items 携带调用方附加数据，二者均由统一验证入口原样透传。
/// </para>
/// <para>
/// 实现方不应在 <see cref="IValidatableObject.Validate"/> 与 <see cref="ValidateAsync"/> 之间重复同一规则：
/// 同步验证入口遇到本接口实现会显式拒绝（抛出 <see cref="NotSupportedException"/>），
/// 避免同一规则被静默跳过或重复执行。
/// </para>
/// </remarks>
public interface IAsyncValidatableObject
{
    /// <summary>异步执行对象级校验规则。</summary>
    /// <param name="validationContext">验证上下文（经 <see cref="ValidationContext.GetService(Type)"/> 解析 DI 服务、经 <see cref="ValidationContext.Items"/> 读取附加数据）。</param>
    /// <param name="cancellationToken">取消令牌，由验证入口原样透传。</param>
    /// <returns>失败结果集合（空集合或不含失败项表示验证通过）。</returns>
    Task<IEnumerable<ValidationResult>> ValidateAsync(ValidationContext validationContext, CancellationToken cancellationToken = default);
}
