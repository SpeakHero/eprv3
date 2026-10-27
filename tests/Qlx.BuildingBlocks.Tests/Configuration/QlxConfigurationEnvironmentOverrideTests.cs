/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：环境变量覆盖四级 JSON 配置的优先级测试
 */

using Microsoft.Extensions.Configuration;
using Qlx.BuildingBlocks.Configuration;

namespace Qlx.BuildingBlocks.Tests.Configuration;

/// <summary>
/// 环境变量覆盖测试：<c>Qlx__Ai__Strategy</c> / <c>Qlx__Ai__Providers__zhipu__ApiKey</c>
/// 必须高于 <c>config/common</c> JSON 中同名配置项。
/// </summary>
public sealed class QlxConfigurationEnvironmentOverrideTests
{
    [Fact]
    public void EnvironmentVariablesOverrideJsonConfiguration()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "qlx-cfg-" + Guid.NewGuid().ToString("N"));

        try
        {
            var commonDirectory = Path.Combine(tempDirectory, "common");
            Directory.CreateDirectory(commonDirectory);
            File.WriteAllText(
                Path.Combine(commonDirectory, "ai.json"),
                """
                {
                  "Qlx": {
                    "Ai": {
                      "Strategy": "Fixed",
                      "Providers": {
                        "zhipu": { "Type": "zhipu", "Model": "glm-4.7-flash" }
                      }
                    }
                  }
                }
                """);

            Environment.SetEnvironmentVariable("QLX_CONFIG_ROOT", tempDirectory);
            Environment.SetEnvironmentVariable("Qlx__Ai__Strategy", "RoundRobin");
            Environment.SetEnvironmentVariable("Qlx__Ai__Providers__zhipu__ApiKey", "test-key");

            var configuration = new ConfigurationBuilder()
                .AddQlxConfiguration(tempDirectory, "Development", QlxHostRole.AllInOne)
                .Build();

            Assert.Equal("RoundRobin", configuration["Qlx:Ai:Strategy"]);
            Assert.Equal("test-key", configuration["Qlx:Ai:Providers:zhipu:ApiKey"]);
            Assert.Equal("glm-4.7-flash", configuration["Qlx:Ai:Providers:zhipu:Model"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QLX_CONFIG_ROOT", null);
            Environment.SetEnvironmentVariable("Qlx__Ai__Strategy", null);
            Environment.SetEnvironmentVariable("Qlx__Ai__Providers__zhipu__ApiKey", null);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
