/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:35:00 +08:00 中国标准时间 星期四
 * 描述：物理存储后端契约（保存 / 打开读 / 删除 / 存在性检查），仅任务处理器可调用
 */

namespace Qlx.BuildingBlocks.Files;

/// <summary>
/// 物理存储后端契约。文件全任务化红线（S18）：仅文件任务处理器
/// （Qlx.Modules.Support/Jobs/Files/*）允许调用本接口，业务服务一律经 IJobScheduler 提交任务。
/// </summary>
public interface IFileStorageBackend
{
    /// <summary>
    /// 保存文件内容。
    /// </summary>
    /// <param name="storageKey">存储键（后端据此定位，禁止含路径穿越片段）。</param>
    /// <param name="content">内容流。</param>
    /// <param name="setReadOnly">保存后是否将物理文件设为只读（保护目录场景，规则 R5）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task SaveAsync(string storageKey, Stream content, bool setReadOnly, CancellationToken cancellationToken);

    /// <summary>
    /// 打开文件读取流（调用方负责 Dispose）。
    /// </summary>
    /// <param name="storageKey">存储键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>文件内容流。</returns>
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>
    /// 删除物理文件（引用计数归零后才允许调用，规则 R6）。
    /// </summary>
    /// <param name="storageKey">存储键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>
    /// 检查物理文件是否存在。
    /// </summary>
    /// <param name="storageKey">存储键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存在返回 true。</returns>
    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken);
}
