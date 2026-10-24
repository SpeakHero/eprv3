/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：单据序号存储契约，按序号键原子递增返回下一个序号
 */

namespace Qlx.BuildingBlocks.Numbering;

/// <summary>
/// 单据流水序号存储契约。实现必须保证同一序号键的递增操作原子性。
/// 生产环境经数据库序列 / Redis INCR 实现替换，默认内存实现仅用于开发与单实例部署。
/// </summary>
public interface ISerialSequenceStore
{
    /// <summary>
    /// 获取指定序号键的下一个序号（从 1 开始，原子递增）。
    /// </summary>
    /// <param name="sequenceKey">序号键，由生成器按前缀 + 组织码 + 期间拼装。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下一个序号值。</returns>
    Task<long> GetNextAsync(string sequenceKey, CancellationToken cancellationToken);
}
