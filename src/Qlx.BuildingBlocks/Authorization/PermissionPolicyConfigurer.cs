/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：权限点授权策略自动注册器——按全部模块 Manifest 的 PermissionDefinitions
 *       物化 Permission:{code} 策略，模块新增/停用权限点零接线。
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Authorization;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Modules;

namespace Qlx.BuildingBlocks.Authorization;

/// <summary>
/// 权限点策略自动注册（<see cref="IConfigureOptions{TOptions}"/> 模式）。
/// <para>
/// 依赖 <see cref="IReadOnlyList{T}"/>（<c>AddQlxModules</c> 以列表单例注册，
/// 禁止改注 <c>IEnumerable&lt;IModule&gt;</c>——E-9 教训：集合语义不同会解析为空集）。
/// 每个权限码注册为策略 <c>Permission:{code}</c>，认证 + <see cref="PermissionRequirement"/>，
/// 由 <see cref="PermissionAuthorizationHandler"/> 完成判定。
/// </para>
/// </summary>
/// <param name="modules">全部已发现模块。</param>
public sealed class PermissionPolicyConfigurer(IReadOnlyList<IModule> modules)
    : IConfigureOptions<AuthorizationOptions>
{
    /// <inheritdoc />
    public void Configure(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var module in modules)
        {
            foreach (var permission in module.Manifest.Permissions)
            {
                options.AddPolicy(
                    QlxPolicyNames.Permission(permission.Code),
                    policy => policy
                        .RequireAuthenticatedUser()
                        .AddRequirements(new PermissionRequirement(permission.Code)));
            }
        }
    }
}
