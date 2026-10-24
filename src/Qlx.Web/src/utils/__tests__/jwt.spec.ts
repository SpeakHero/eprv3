/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：utils/jwt.ts 单元测试—— vitest；覆盖合法/非法/边界/claims 映射
 */

import { describe, expect, it } from 'vitest'
import { decodeJwtPayload } from '../jwt'

function makeJwt(payload: Record<string, unknown>): string {
  const header = btoa(JSON.stringify({ alg: 'RS256', typ: 'JWT' }))
  const body = btoa(unescape(encodeURIComponent(JSON.stringify(payload))))
  const sig = btoa('fake-signature')
  return `${header}.${body}.${sig}`
}

describe('decodeJwtPayload', () => {
  it('三段完整 JWT 正常解码', () => {
    const token = makeJwt({
      sub: 'abc',
      tenant_id: '9x',
      qlx_member_role: 'TenantAdmin',
      qlx_permissions: 'bas.supplier.view,bas.supplier.edit',
      tv_m: 1,
      iat: 1_700_000_000,
      exp: 1_700_086_400,
    })
    const claims = decodeJwtPayload(token)!
    expect(claims).not.toBeNull()
    expect(claims.sub).toBe('abc')
    expect(claims.tenantId).toBe('9x')
    expect(claims.memberRole).toBe('TenantAdmin')
    expect(claims.permissions).toEqual(['bas.supplier.view', 'bas.supplier.edit'])
    expect(claims.tvM).toBe(1)
    expect(claims.iat).toBe(1_700_000_000)
  })

  it('门户管理员身份 Token：qlx_portal_admin=1 → isPortalAdmin=true，无 tenant_id', () => {
    const token = makeJwt({
      sub: 'portal-user-1',
      qlx_portal_admin: '1',
      qlx_permissions: 'portal.role.view,portal.role.edit',
      tv_u: 3,
    })
    const claims = decodeJwtPayload(token)!
    expect(claims.isPortalAdmin).toBe(true)
    expect(claims.tenantId).toBeUndefined()
    expect(claims.permissions).toEqual(['portal.role.view', 'portal.role.edit'])
  })

  it('TenantAdmin 旁路：permissions 可能不写 claim → 为 undefined', () => {
    const token = makeJwt({
      sub: 'admin-user',
      tenant_id: '9x',
      qlx_member_role: 'TenantAdmin',
      // 注意：TenantAdmin 后端不写 qlx_permissions
    })
    const claims = decodeJwtPayload(token)!
    expect(claims.memberRole).toBe('TenantAdmin')
    expect(claims.permissions).toBeUndefined()
  })

  it('permissions 空字符串 → undefined', () => {
    const token = makeJwt({ qlx_permissions: '  ,  ,  ' })
    const claims = decodeJwtPayload(token)!
    expect(claims.permissions).toBeUndefined()
  })

  it('非法：空字符串 → null', () => {
    expect(decodeJwtPayload('')).toBeNull()
  })

  it('非法：仅两段 → null', () => {
    expect(decodeJwtPayload('aaa.bbb')).toBeNull()
  })

  it('非法：非字符串 → null', () => {
    // @ts-expect-error 传入非字符串验证防御
    expect(decodeJwtPayload(null)).toBeNull()
    // @ts-expect-error
    expect(decodeJwtPayload(undefined)).toBeNull()
    // @ts-expect-error
    expect(decodeJwtPayload(42)).toBeNull()
  })

  it('payload 段非 base64 → null', () => {
    expect(decodeJwtPayload('aaa.!!!.ccc')).toBeNull()
  })

  it('payload 段 base64 但非 JSON → null', () => {
    const bad = `${btoa('{}')}.${btoa('not-json')}.${btoa('sig')}`
    expect(decodeJwtPayload(bad)).toBeNull()
  })
})
