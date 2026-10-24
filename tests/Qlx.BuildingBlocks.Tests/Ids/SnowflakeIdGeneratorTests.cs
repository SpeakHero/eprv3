/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：雪花 ID 生成器单元测试（并发唯一性、单调递增、workerId 边界）
 */

using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.Tests.Ids;

public sealed class SnowflakeIdGeneratorTests
{
    [Fact]
    public void ConcurrentGenerationProducesUniqueIds()
    {
        var generator = new SnowflakeIdGenerator(workerId: 7);
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 10_000, _ => ids.Add(generator.NextId()));

        Assert.Equal(10_000, ids.Distinct().Count());
    }

    [Fact]
    public void SequentialIdsAreMonotonicallyIncreasing()
    {
        var generator = new SnowflakeIdGenerator(workerId: 3);

        var previous = generator.NextId();
        for (var i = 0; i < 1_000; i++)
        {
            var current = generator.NextId();
            Assert.True(current > previous, $"第 {i} 个 ID 未单调递增");
            previous = current;
        }
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(SnowflakeIdGenerator.MaxWorkerId + 1)]
    public void WorkerIdOutOfRangeThrows(long workerId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnowflakeIdGenerator(workerId));
    }

    [Fact]
    public void GeneratedIdEncodesWorkerId()
    {
        const long workerId = 513;
        var generator = new SnowflakeIdGenerator(workerId);

        var id = generator.NextId();
        var encodedWorkerId = (id >> SnowflakeIdGenerator.SequenceBits) & SnowflakeIdGenerator.MaxWorkerId;

        Assert.Equal(workerId, encodedWorkerId);
    }
}
