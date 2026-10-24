/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：门户菜单 API 封装——GET /api/portal/menus（后端返回扁平 JSON，前端 menuStore 消费 menuTree.ts 组装）
 */

import axios, { AxiosError } from 'axios'
import type { NavGroupDTO, MenuFlatDTO } from '@/utils/menuTree'

/** 菜单扁平 API 响应。 */
export interface MenusFlatResponse {
  navGroups: NavGroupDTO[]
  menus: MenuFlatDTO[]
}

const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '',
  timeout: 15000,
  headers: { Accept: 'application/json' },
})

/**
 * 获取门户菜单扁平数据。
 * @param accessToken Bearer Token（身份级或租户级均可，后端不裁剪权限，前端 menuStore 消费后裁剪）。
 * @throws 网络错误 / 后端返回非 ApiResponse 格式错误。
 */
export async function fetchMenus(accessToken: string): Promise<MenusFlatResponse> {
  try {
    const response = await http.get<{ data: MenusFlatResponse; code: number; message: string }>(
      '/api/portal/menus',
      { headers: { Authorization: `Bearer ${accessToken}` } },
    )
    const body = response.data
    if (body.code !== 0) {
      throw new Error(body.message || '菜单加载失败')
    }
    return body.data
  } catch (error) {
    if (axios.isAxiosError(error)) {
      const desc = (error as AxiosError<Record<string, unknown>>).response?.data?.message
      throw new Error(typeof desc === 'string' && desc ? desc : '菜单加载失败')
    }
    throw error
  }
}
