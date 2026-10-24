/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:05:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 16:05:00 +08:00 中国标准时间 星期三
 * 描述：Messaging 模块 DI 注册扩展（SMTP + SMS + 消息渠道分发入队）
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.Messaging;

/// <summary>
/// Messaging 模块 DI 注册扩展。
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Messaging 模块（SMTP + SMS 降级桩 + 消息渠道分发入队）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置（读取 Qlx:Messaging:Smtp 节）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddQlxMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SmtpOptions>(
            configuration.GetSection("Qlx:Messaging:Smtp"));

        services.AddSingleton<ISmtpSender, SmtpSender>();
        services.AddSingleton<ISmsSender, NotConfiguredSmsSender>();
        services.AddSingleton<IMessageDispatchEnqueuer, HangfireMessageDispatchEnqueuer>();

        // 启动期校验：若启用但必填项缺失，启动时报错快速暴露
        services.AddOptions<SmtpOptions>()
            .PostConfigure(options =>
            {
                var errors = options.Validate();
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"SMTP 配置校验失败：{string.Join("; ", errors)}");
                }
            });

        return services;
    }
}
