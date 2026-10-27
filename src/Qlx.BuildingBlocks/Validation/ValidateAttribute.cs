/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 23:10:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 23:10:00 +08:00 中国标准时间 星期一
 * 描述：服务接口参数级校验标记——标注在接口方法参数上，由 ValidationDispatchProxy 在方法入口自动执行 ObjectValidator 校验
 */

namespace Qlx.BuildingBlocks.Validation;

/// <summary>
/// 服务接口方法参数的校验标记：标注在**接口方法参数**上（非实现类），
/// 经 <see cref="ValidationDispatchProxy{T}"/> 代理的服务在方法入口自动对该参数执行
/// <see cref="ObjectValidator.ValidateOrThrow(object?, IServiceProvider?, System.Collections.Generic.IDictionary{object, object}?, bool)"/>
/// 全量校验（特性 + <see cref="System.ComponentModel.DataAnnotations.IValidatableObject"/>），失败即抛业务校验异常。
/// </summary>
/// <remarks>
/// 仅用于**仅含 DataAnnotations 特性**的 DTO；若 DTO 已实现
/// <see cref="System.ComponentModel.DataAnnotations.IValidatableObject"/> 或 <see cref="IAsyncValidatableObject"/>，
/// 代理按接口自动识别，无需本标记。
/// 标记须写在接口定义上：DispatchProxy 拦截到的是接口方法的 <see cref="System.Reflection.MethodInfo"/>，实现类上的标注不可见。
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ValidateAttribute : Attribute
{
}
