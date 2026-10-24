/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：JWT payload Base64Url 解码器（前端纯函数，零副作用；后端 OpenIddict 已验签，前端不解签名）
 * 规范：utils/jwt.ts —— 无外部依赖，auth store 消费
 */

/** JWT claims 统一结构（已将后端下划线风格映射为前端 camelCase 友好的别名）。 */
export interface TokenClaims {
  /** 主体标识（用户 id 或租户 id 混淆串）。 */
  sub?: string
  /** 租户 id 混淆串（仅租户级 Token 有此 claim）。 */
  tenantId?: string
  /** 租户内角色：TenantAdmin / OrdinaryMember。 */
  memberRole?: string
  /** 本次进入所选租户角色 id（Hashids 混淆串）。 */
  memberRoleId?: string
  /** 权限码列表（逗号分隔字符串或已 Split 的数组）。 */
  permissions?: string[]
  /** 数据范围：All / DeptAndChildren / Dept / Self。 */
  dataScope?: string
  /** 数据范围部门 id。 */
  dataScopeDeptId?: string
  /** 门户管理员标识（身份级 Token，值 "1" 表示是平台管理员）。 */
  isPortalAdmin?: boolean
  /** 租户成员 Token 版本号（用于失效旧 Token）。 */
  tvM?: number
  /** 门户用户 Token 版本号（同上）。 */
  tvU?: number
  /** 签发时间戳（秒）。 */
  iat?: number
  /** 过期时间戳（秒）。 */
  exp?: number
  /** 签发者。 */
  iss?: string
  /** 受众。 */
  aud?: string
  /** 原始 claims（后端下划线风格字段，未映射的保留）。 */
  raw?: Record<string, unknown>
}

/**
 * 解码 JWT payload 段：Base64Url → Base64 → UTF-8 → JSON。
 * @param token 完整 JWT（三段式 "xxx.yyy.zzz"）；格式非法或 payload 非 JSON 时返回 null。
 */
export function decodeJwtPayload(token: string): TokenClaims | null {
  if (!token || typeof token !== 'string') {
    return null
  }
  const parts = token.split('.')
  if (parts.length !== 3) {
    return null
  }
  let payloadText: string
  try {
    payloadText = base64UrlDecode(parts[1])
  } catch {
    return null
  }
  if (!payloadText) {
    return null
  }
  let raw: Record<string, unknown>
  try {
    raw = JSON.parse(payloadText) as Record<string, unknown>
  } catch {
    return null
  }
  return mapClaims(raw)
}

/** 从后端下划线风格 claim 名映射为前端友好别名；未映射字段保留 raw。 */
function mapClaims(raw: Record<string, unknown>): TokenClaims {
  const claims: TokenClaims = {
    sub: stringOf(raw.sub),
    tenantId: stringOf(raw.tenant_id),
    memberRole: stringOf(raw.qlx_member_role),
    memberRoleId: stringOf(raw.qlx_member_role_id) ?? stringOf(raw.member_role_id),
    permissions: splitPermissions(stringOf(raw.qlx_permissions)),
    dataScope: stringOf(raw.qlx_data_scope),
    dataScopeDeptId: stringOf(raw.qlx_data_scope_dept),
    isPortalAdmin: raw.qlx_portal_admin === '1' || raw.is_portal_admin === '1',
    tvM: numberOf(raw.tv_m),
    tvU: numberOf(raw.tv_u),
    iat: numberOf(raw.iat),
    exp: numberOf(raw.exp),
    iss: stringOf(raw.iss),
    aud: stringOf(raw.aud),
    raw,
  }
  return claims
}

/** 后端 qlx_permissions claim 是逗号分符字符串；TenantAdmin 可能不写。 */
function splitPermissions(raw: string | undefined): string[] | undefined {
  if (!raw) {
    return undefined
  }
  const list = raw
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
  return list.length > 0 ? list : undefined
}

function stringOf(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined
}

function numberOf(value: unknown): number | undefined {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value
  }
  if (typeof value === 'string') {
    const n = Number(value)
    if (Number.isFinite(n)) {
      return n
    }
  }
  return undefined
}

/** Base64Url → Base64 → UTF-8 解码；非法输入抛错（由上层 catch 转 null）。 */
function base64UrlDecode(input: string): string {
  // 补齐 padding
  let base64 = input.replace(/-/g, '+').replace(/_/g, '/')
  switch (base64.length % 4) {
    case 2: base64 += '=='
      break
    case 3: base64 += '='
      break
  }
  // Node 和浏览器都支持 atob；vitest 的 node 环境也支持
  const binary = atob(base64)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i)
  }
  return new TextDecoder().decode(bytes)
}
