/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：主数据导入导出组合逻辑——模板清单校验 / 模板下载 / xlsx 导入 / 导出下载
 * 说明：
 *   1. 供应商 / 客户 / 人员 / 商品分类四个页面共用，避免各自复制模板编码与 Blob 处理（B-4 公共层复用）；
 *   2. 模板编码先经后端清单校验（不硬编码失效），导出编码由导入编码推导 .export；
 *   3. 后端错误消息（400/403/409/422）经 API 层透传后由页面/本组合器提示，保持可见。
 */

import { computed, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { importExportApi } from '@/api/basMaster'
import type { ImportResult, ImportTemplateInfo } from '@/types/bas-master'
import { triggerDownload } from '@/utils/bas-master-mappers'

/** 导出可携带的筛选条件（后端导出端点支持的查询参数子集）。 */
export interface MasterExportParams {
  status?: string
  personType?: string
  employStatus?: string
  keyword?: string
}

export interface MasterTransferOptions {
  /** 导入模板编码（如 bas.supplier.import）。 */
  templateCode: string
  /** 导出筛选条件（离开页面状态时按当前筛选导出）。 */
  exportParams?: () => MasterExportParams
  /** 导入成功后刷新列表。 */
  onImported?: () => void | Promise<void>
}

/** 导入导出状态机（页面只接线按钮与弹窗）。 */
export function useMasterTransfer(options: MasterTransferOptions) {
  const importOpen = ref(false)
  const importing = ref(false)
  const exporting = ref(false)
  const downloading = ref(false)
  const importResult = ref<ImportResult | null>(null)
  const templates = ref<ImportTemplateInfo[]>([])

  const template = computed<ImportTemplateInfo | null>(() =>
    templates.value.find((item) => item.templateCode === options.templateCode) ?? null,
  )

  /** 模板列说明（导入弹窗提示文案；清单未加载时给出通用提示）。 */
  const templateHint = computed(() => {
    if (!template.value) {
      return '请先下载标准导入模板，按模板列填写后上传（.xlsx）。'
    }
    const columns = template.value.columns
      .map((column) => `${column.displayName}${column.isRequired ? '*' : ''}`)
      .join(' / ')
    return `模板列：${columns}（* 为必填）。请先下载模板，按列填写后上传 .xlsx 文件。`
  })

  /** 懒加载模板清单（首次打开导入弹窗或下载模板时）。 */
  async function ensureTemplates(): Promise<void> {
    if (templates.value.length > 0) {
      return
    }
    templates.value = await importExportApi.listTemplates()
  }

  /** 打开导入弹窗（先校验模板存在）。 */
  async function openImport(): Promise<void> {
    try {
      await ensureTemplates()
      await importExportApi.resolveImportTemplateCode(options.templateCode)
      importResult.value = null
      importOpen.value = true
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '加载导入模板失败')
    }
  }

  /** 下载标准导入模板（xlsx）。 */
  async function downloadTemplate(): Promise<void> {
    downloading.value = true
    try {
      await ensureTemplates()
      const code = await importExportApi.resolveImportTemplateCode(options.templateCode)
      const file = await importExportApi.downloadTemplate(code)
      triggerDownload(file.blob, file.fileName)
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '下载模板失败')
    } finally {
      downloading.value = false
    }
  }

  /** 按当前筛选导出（.export 模板，敏感字段由后端脱敏）。 */
  async function exportFile(): Promise<void> {
    exporting.value = true
    try {
      const code = importExportApi.resolveExportTemplateCode(options.templateCode)
      const params = options.exportParams?.() ?? {}
      const file = await importExportApi.exportData({ templateCode: code, ...params })
      triggerDownload(file.blob, file.fileName)
      ElMessage.success('导出文件已开始下载')
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '导出失败')
    } finally {
      exporting.value = false
    }
  }

  /** 提交 xlsx 导入（后端返回行级错误，失败行明细在弹窗内回显）。 */
  async function submitImportFile(file: File): Promise<void> {
    importing.value = true
    try {
      const result = await importExportApi.importData(options.templateCode, file)
      importResult.value = result
      if (result.successRows > 0) {
        await options.onImported?.()
      }
      if (result.failedRows > 0) {
        ElMessage.warning(`导入完成：成功 ${result.successRows} 行，失败 ${result.failedRows} 行`)
      } else {
        ElMessage.success(`导入成功：${result.successRows} 行`)
      }
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '导入失败')
    } finally {
      importing.value = false
    }
  }

  return {
    importOpen,
    importing,
    exporting,
    downloading,
    importResult,
    template,
    templateHint,
    openImport,
    downloadTemplate,
    exportFile,
    submitImportFile,
  }
}
