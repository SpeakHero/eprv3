# -*- coding: utf-8 -*-
"""阶段三第二波 2A（05 商品 + 仓库最小切片）端到端冒烟 + 截图取证。

策略：UI 登录拿 tenantToken → context.request/urllib 调 API 造业务数据与审批 →
      UI 页面截图取证 → PowerShell SqlClient 直连 SQL 核对。
仅做验证与取证，不修改任何业务代码。
"""
import base64
import json
import os
import subprocess
import time
import traceback
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime

from playwright.sync_api import sync_playwright

BASE = "http://localhost:5173"
API = "http://localhost:5002"
OUT = r"e:\ERPv2\eprv3\docs\test-shots\stk-wms-2a-20261026"
SCRIPTS = os.path.join(OUT, "_scripts")
RESULT_PATH = os.path.join(SCRIPTS, "smoke-result.json")
TENANT_ID = 106001473065717760
STAMP = time.strftime("%H%M%S")

os.makedirs(SCRIPTS, exist_ok=True)

console_errors = []
page_errors = []
http_errors = []
screenshots = []
api_calls = []
steps = {}
data = {}

RESULT = {
    "title": "阶段三第二波 2A（05 商品 + 仓库最小切片）端到端冒烟",
    "startedAt": datetime.now().isoformat(timespec="seconds"),
    "base": {"web": BASE, "api": API, "tenantId": TENANT_ID},
    "data": data,
    "steps": steps,
    "apiCalls": api_calls,
    "sql": {},
    "screenshots": screenshots,
    "consoleErrors": console_errors,
    "pageErrors": page_errors,
    "httpErrors": http_errors,
}

TOKEN = ""
IDENTITY = ""
PAGE = None


# ============================ 通用工具 ============================

def shot(name, full=True):
    path = os.path.join(OUT, f"{name}.png")
    PAGE.screenshot(path=path, full_page=full)
    screenshots.append(f"{name}.png")
    print("SHOT", name)


def run_step(name, fn):
    started = time.time()
    try:
        detail = fn()
        steps[name] = {"ok": True, "detail": detail, "ms": int((time.time() - started) * 1000)}
        print("STEP_OK", name, json.dumps(detail, ensure_ascii=False)[:400] if detail is not None else "")
        return detail
    except Exception as exc:  # noqa: BLE001
        steps[name] = {
            "ok": False,
            "error": str(exc),
            "trace": traceback.format_exc(limit=6),
            "ms": int((time.time() - started) * 1000),
        }
        print("STEP_FAIL", name, exc)
        try:
            shot(f"99-fail-{name}")
        except Exception:  # noqa: BLE001
            pass
        return None


def api(method, path, payload=None, token=None):
    url = API + path
    headers = {"Accept": "application/json", "Authorization": f"Bearer {token or TOKEN}"}
    body = None
    if payload is not None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    status = 0
    text = ""
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            status = resp.status
            text = resp.read().decode("utf-8")
    except urllib.error.HTTPError as exc:
        status = exc.code
        text = exc.read().decode("utf-8", "replace")
    entry = {"method": method, "path": path, "status": status}
    if status >= 400:
        entry["body"] = text[:800]
        http_errors.append(entry.copy())
    api_calls.append(entry)
    if status >= 400:
        raise RuntimeError(f"{method} {path} -> HTTP {status}: {text[:500]}")
    parsed = json.loads(text) if text else None
    if isinstance(parsed, dict) and "success" in parsed:
        if not parsed.get("success"):
            raise RuntimeError(f"{method} {path} -> {parsed.get('code')}: {parsed.get('message')}")
        return parsed.get("data")
    return parsed


def wait_for(fn, timeout=60, interval=1.0, label="条件"):
    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        try:
            last = fn()
        except Exception as exc:  # noqa: BLE001
            last = f"ERR {exc}"
        else:
            if last:
                return last
        time.sleep(interval)
    raise RuntimeError(f"等待{label}超时（{timeout}s），最后结果：{last}")


def approve_until_terminal(instance_id, max_times=6):
    approved = 0
    for _ in range(max_times):
        inst = api("GET", f"/api/support/approvals/instance/{instance_id}")
        status = inst.get("status")
        if status == 2:
            return {"approvedActions": approved, "finalStatus": 2, "instance": inst}
        if status != 1:
            raise RuntimeError(f"审批实例状态异常：{status}")
        api("POST", f"/api/support/approvals/{instance_id}/approve", {"comment": "2A 冒烟自动通过"})
        approved += 1
        time.sleep(0.4)
    inst = api("GET", f"/api/support/approvals/instance/{instance_id}")
    raise RuntimeError(f"审批未在 {max_times} 次动作内终止：status={inst.get('status')}")


def open_select_option(label_text, option_text):
    """点击搜索栏/表单中 label_text 表单项下的下拉，并选择 option_text。"""
    item = PAGE.locator(".el-form-item").filter(has_text=label_text).first
    item.locator(".el-select").first.click()
    PAGE.wait_for_timeout(400)
    dropdown = PAGE.locator(".el-select-dropdown:visible, .el-popper:visible").last
    dropdown.get_by_text(option_text, exact=True).first.click()
    PAGE.wait_for_timeout(300)


def click_search():
    PAGE.locator(".qlx-search").get_by_role("button", name="查询").first.click()
    PAGE.wait_for_timeout(1200)


# ============================ SQL 核对 ============================

PS_TEMPLATE = r'''
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'
$cs = 'Server=(localdb)\MSSQLLocalDB;Database=__DB__;Integrated Security=true;TrustServerCertificate=true'
$c = New-Object System.Data.SqlClient.SqlConnection($cs)
$c.Open()
function Run([string]$sql) {
  $cmd = $c.CreateCommand()
  $cmd.CommandText = $sql
  $t = New-Object System.Data.DataTable
  $t.Load($cmd.ExecuteReader())
  $rows = @()
  foreach ($row in $t.Rows) {
    $h = @{}
    foreach ($col in $t.Columns) {
      $v = $row[$col]
      if ($v -is [System.DBNull]) { $v = $null }
      $h[$col.ColumnName] = $v
    }
    $rows += $h
  }
  return ,$rows
}
$tenant = __TENANT__
$itemCode = '__ITEM_CODE__'
$whCode = '__WH_CODE__'
$batchNo = '__BATCH_NO__'
$res = @{
  item = Run "SELECT id, item_code, batch_enabled, status FROM stk_items WHERE tenant_id=$tenant AND item_code='$itemCode'"
  warehouse = Run "SELECT id, code, name FROM wms_warehouses WHERE tenant_id=$tenant AND code='$whCode'"
  zones = Run "SELECT COUNT(*) AS cnt FROM wms_zones z JOIN wms_warehouses w ON w.id=z.warehouse_id WHERE w.tenant_id=$tenant AND w.code='$whCode' AND z.is_deleted=0"
  locations = Run "SELECT COUNT(*) AS cnt FROM wms_locations l JOIN wms_warehouses w ON w.id=l.warehouse_id WHERE w.tenant_id=$tenant AND w.code='$whCode' AND l.is_deleted=0"
  balances = Run "SELECT b.qty_onhand, b.amount, b.avg_cost, b.qty_locked FROM stk_stock_balances b JOIN stk_items i ON i.id=b.item_id JOIN wms_warehouses w ON w.id=b.warehouse_id WHERE i.tenant_id=$tenant AND i.item_code='$itemCode' AND w.code='$whCode'"
  txns = Run "SELECT t.txn_no, t.source_type, t.direction, t.qty, t.unit_cost, t.amount, t.balance_qty_after, t.balance_amount_after, t.batch_no, t.source_doc_no FROM stk_stock_txns t JOIN stk_items i ON i.id=t.item_id JOIN wms_warehouses w ON w.id=t.warehouse_id WHERE i.tenant_id=$tenant AND i.item_code='$itemCode' AND w.code='$whCode' ORDER BY t.id"
  opening = Run "SELECT o.batch_no, o.status, o.posted_at FROM stk_opening_batches o WHERE o.tenant_id=$tenant AND o.batch_no='$batchNo'"
  openingItems = Run "SELECT oi.qty, oi.unit_cost, oi.amount FROM stk_opening_items oi JOIN stk_opening_batches o ON o.id=oi.opening_batch_id WHERE o.tenant_id=$tenant AND o.batch_no='$batchNo'"
  stockBatches = Run "SELECT sb.batch_no, sb.qty, sb.avg_cost, sb.amount FROM stk_stock_batches sb JOIN stk_items i ON i.id=sb.item_id JOIN wms_warehouses w ON w.id=sb.warehouse_id WHERE i.tenant_id=$tenant AND i.item_code='$itemCode' AND w.code='$whCode'"
}
$c.Close()
$res | ConvertTo-Json -Depth 6 -Compress
'''


DISCOVER_DB_PS = r'''
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$q = 'Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true'
$c = New-Object System.Data.SqlClient.SqlConnection($q)
$c.Open()
$cmd = $c.CreateCommand()
$cmd.CommandText = "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name"
$r = $cmd.ExecuteReader()
$names = @()
while ($r.Read()) { $names += $r[0] }
$c.Close()
$found = ''
foreach ($n in $names) {
  try {
    $cs = "Server=(localdb)\MSSQLLocalDB;Database=$n;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3"
    $cc = New-Object System.Data.SqlClient.SqlConnection($cs)
    $cc.Open()
    $cc2 = $cc.CreateCommand()
    $cc2.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='wms_warehouses'"
    if ([int]$cc2.ExecuteScalar() -gt 0) {
      $cc2.CommandText = "SELECT COUNT(*) FROM wms_warehouses WHERE code='__WH_CODE__'"
      if ([int]$cc2.ExecuteScalar() -gt 0) { $found = $n }
    }
    $cc.Close()
  } catch { }
  if ($found -ne '') { break }
}
Write-Output $found
'''


def resolve_tenant_db(wh_code):
    """定位存放本次冒烟仓库/商品的租户库（多租户独立库场景）。"""
    script = DISCOVER_DB_PS.replace("__WH_CODE__", wh_code)
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    proc = subprocess.run(
        ["powershell", "-NoProfile", "-EncodedCommand", encoded],
        capture_output=True, text=True, encoding="utf-8", timeout=180,
    )
    db = (proc.stdout or "").strip()
    if not db:
        raise RuntimeError(f"未找到包含仓库 {wh_code} 的租户库：{proc.stderr[:500]}")
    return db


def run_sql_checks(db):
    script = (PS_TEMPLATE
              .replace("__DB__", db)
              .replace("__TENANT__", str(TENANT_ID))
              .replace("__ITEM_CODE__", data["itemCode"])
              .replace("__WH_CODE__", data["warehouseCode"])
              .replace("__BATCH_NO__", data["openingBatchNo"]))
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    proc = subprocess.run(
        ["powershell", "-NoProfile", "-EncodedCommand", encoded],
        capture_output=True, text=True, encoding="utf-8", timeout=120,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"PowerShell 退出码 {proc.returncode}: {proc.stderr[:800]}")
    out = (proc.stdout or "").strip()
    if not out:
        raise RuntimeError(f"SQL 核对无输出：{proc.stderr[:800]}")
    return json.loads(out)


# ============================ 主流程 ============================

def main():
    global TOKEN, PAGE
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        ctx = browser.new_context(viewport={"width": 1440, "height": 900}, locale="zh-CN")
        PAGE = ctx.new_page()

        PAGE.on("console", lambda m: console_errors.append({"type": m.type, "text": m.text, "page": PAGE.url})
                if m.type == "error" else None)
        PAGE.on("pageerror", lambda e: page_errors.append({"error": str(e), "page": PAGE.url}))
        PAGE.on("response", lambda r: http_errors.append(
            {"status": r.status, "url": r.url, "method": r.request.method}) if r.status >= 400 else None)

        # ---------- 1. 登录 ----------
        def do_login(shot_login=False):
            """清空本地会话后重新走 UI 登录，返回最新 PersistedSession。"""
            PAGE.goto(f"{BASE}/login", wait_until="networkidle")
            PAGE.evaluate("() => localStorage.clear()")
            PAGE.goto(f"{BASE}/login", wait_until="networkidle")
            PAGE.wait_for_timeout(800)
            if shot_login:
                shot("00-login")
            PAGE.get_by_placeholder("请输入登录账号").fill("qlxadmin")
            PAGE.get_by_placeholder("请输入登录密码").fill("Qlx@2026")
            PAGE.get_by_placeholder("填写企业名称或税号，可直接进入该企业").fill("91510100MA0000000X")
            PAGE.locator("button[type=submit]").click()
            PAGE.wait_for_timeout(3000)
            PAGE.wait_for_load_state("networkidle")
            PAGE.goto(f"{BASE}/workbench", wait_until="networkidle")
            PAGE.wait_for_timeout(1500)
            session = PAGE.evaluate("() => JSON.parse(localStorage.getItem('qlx.session.v1') || 'null')")
            if not session:
                raise RuntimeError("登录后未找到 localStorage qlx.session.v1")
            token = session.get("tenantToken") or ""
            if not token:
                raise RuntimeError("会话缺少 tenantToken")
            globals()["TOKEN"] = token
            return session

        def session_summary(session):
            return {
                "currentTenant": (session.get("currentTenant") or {}).get("id")
                if isinstance(session.get("currentTenant"), dict) else session.get("currentTenant"),
                "currentTenantRaw": session.get("currentTenant"),
                "licensedModules": session.get("licensedModules"),
                "memberRole": session.get("memberRole"),
                "isPortalAdminAccount": session.get("isPortalAdminAccount"),
                "tenantTokenLength": len(session.get("tenantToken") or ""),
            }

        def step_login():
            session = do_login(shot_login=True)
            shot("01-workbench")
            data["session"] = session_summary(session)
            return {"url": PAGE.url, "session": data["session"]}

        if not run_step("01_login", step_login):
            RESULT["finishedAt"] = datetime.now().isoformat(timespec="seconds")
            RESULT["fatal"] = "登录失败，后续步骤无法执行"
            with open(RESULT_PATH, "w", encoding="utf-8") as fh:
                json.dump(RESULT, fh, ensure_ascii=False, indent=2)
            browser.close()
            return

        # ---------- 1.5 模块授权（环境前置）：补齐 wms / stk 后重新登录取新 tenantToken ----------
        TAX_NO = "91510100MA0000000X"

        def step_license():
            session = PAGE.evaluate("() => JSON.parse(localStorage.getItem('qlx.session.v1') || 'null')")
            identity = (session or {}).get("identityToken") or ""
            if not identity:
                raise RuntimeError("会话缺少 identityToken，无法调用门户授权 API")
            globals()["IDENTITY"] = identity
            initial = list((session or {}).get("licensedModules") or [])
            listed = api("GET", f"/api/portal/admin/tenants?page=1&pageSize=20&keyword={TAX_NO}", token=identity)
            items = listed.get("items") if isinstance(listed, dict) else listed
            target = next((t for t in (items or []) if t.get("taxNo") == TAX_NO), None)
            if target is None:
                raise RuntimeError(f"门户租户列表未找到税号 {TAX_NO}：{items}")
            tenant_hash = target["id"]
            data["tenantHashId"] = tenant_hash
            data["tenantName"] = target.get("name")
            desired = sorted({*initial, "portal", "bas", "support", "wms", "stk"})
            added = [code for code in desired if code not in initial]
            if added:
                api("PUT", f"/api/portal/admin/tenants/{tenant_hash}/modules",
                    {"moduleCodes": desired, "remark": "2A 冒烟环境前置：补齐 wms/stk 模块授权"},
                    token=identity)
            session = do_login()
            final = list(session.get("licensedModules") or [])
            data["license"] = {"initial": initial, "added": added, "final": final,
                               "tenantHashId": tenant_hash}
            if "wms" not in final or "stk" not in final:
                raise RuntimeError(f"重新登录后许可仍缺 wms/stk：{final}")
            return data["license"]

        run_step("01b_license", step_license)

        # ---------- 2. 菜单可见性 ----------
        def step_menu():
            snapshot = api("GET", "/api/tenant/menus")
            menus = (snapshot or {}).get("menus") or []
            nav_rows = (snapshot or {}).get("navGroups") or []
            navs = [{"code": g.get("groupCode"), "name": g.get("groupName"),
                     "moduleCodes": g.get("moduleCodes")} for g in nav_rows]
            routes = [m.get("route") for m in menus]
            names = [m.get("displayName") for m in menus]
            module_codes = sorted({m.get("moduleCode") for m in menus if m.get("moduleCode")})
            data["tenantMenus"] = {"navGroups": navs, "displayNames": names,
                                   "routes": routes, "menuModuleCodes": module_codes}
            # Manifest 菜单校验：wms/stk 的全部菜单路由均已下发
            required_routes = ["/manage/wms/warehouses", "/manage/stk/items", "/manage/stk/openings",
                               "/manage/stk/ledger", "/manage/stk/txns", "/manage/stk/batches",
                               "/manage/stk/cost-adjust"]
            missing_routes = [r for r in required_routes if r not in routes]
            if missing_routes:
                raise RuntimeError(f"租户菜单缺少路由：{missing_routes}（实际：{routes}）")
            # 导航分组校验：承载 wms/stk 的分组（por_navigation_groups: business=业务管理）已下发
            group_hit = [g for g in navs
                         if g.get("moduleCodes") and "wms" in g["moduleCodes"] and "stk" in g["moduleCodes"]]
            if not group_hit:
                raise RuntimeError(f"租户导航分组未包含 wms/stk 分组：{navs}")

            PAGE.goto(f"{BASE}/manage/stk/items", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            body = PAGE.locator("body").inner_text()
            sidebar = PAGE.locator("aside").inner_text() if PAGE.locator("aside").count() else body
            flags = {name: (name in sidebar) for name in
                     ["仓库档案", "商品档案", "期初建账", "库存台账", "出入库流水", "批次库存", "成本调整"]}
            shot("02-menu")
            missing_items = [k for k, v in flags.items() if not v]
            if missing_items:
                raise RuntimeError(f"侧边栏未完整展示菜单项：{missing_items}")
            return {"navGroupHit": group_hit[0], "apiRoutes": routes, "menuModuleCodes": module_codes,
                    "sidebarItems": flags}

        run_step("02_menu", step_menu)

        # ---------- 3. 基础主数据前置：分类 / 单位组 / 单位 ----------
        def step_base_master():
            categories = api("GET", "/api/bas/categories")
            if categories:
                category = categories[0]
                created = False
            else:
                category = api("POST", "/api/bas/categories", {
                    "name": f"冒烟测试分类{STAMP}", "parentId": None, "unitGroupId": None,
                })
                created = True
            groups = api("GET", "/api/bas/unit-groups")
            if groups:
                group = groups[0]
            else:
                group = api("POST", "/api/bas/unit-groups", {"name": f"冒烟测试单位组{STAMP}"})
            units = api("GET", "/api/bas/units")
            if units:
                unit = units[0]
            else:
                unit = api("POST", "/api/bas/units", {
                    "name": "件", "unitGroupId": group["id"], "precision": 0,
                })
            data["categoryId"] = category["id"]
            data["categoryName"] = category["name"]
            data["unitId"] = unit["id"]
            data["unitName"] = unit["name"]
            return {"categoryCreated": created, "category": category["name"],
                    "unitGroup": group["name"], "unit": unit["name"]}

        run_step("03_base_master", step_base_master)

        # ---------- 4. 新建仓库 + 库区 + 库位（API），列表截图 ----------
        warehouse_name = f"冒烟测试仓{STAMP}"

        def step_warehouse():
            wh = api("POST", "/api/wms/warehouses", {
                "name": warehouse_name, "whType": 1, "orgId": None,
                "address": "四川省成都市冒烟测试地址 1 号", "keeperId": None,
                "remark": "2A 冒烟自动创建",
            })
            zone = api("POST", f"/api/wms/warehouses/{wh['id']}/zones", {
                "code": f"Z{STAMP}", "name": f"冒烟库区{STAMP}", "sortOrder": 1,
            })
            loc = api("POST", f"/api/wms/warehouses/{wh['id']}/locations?zoneId={urllib.parse.quote(zone['id'])}", {
                "code": f"L{STAMP}", "barcode": None, "sortOrder": 1,
            })
            zones = api("GET", f"/api/wms/warehouses/{wh['id']}/zones")
            locs = api("GET", f"/api/wms/warehouses/{wh['id']}/locations")
            data["warehouseId"] = wh["id"]
            data["warehouseCode"] = wh["code"]
            data["warehouseName"] = wh["name"]
            data["zoneId"] = zone["id"]
            data["zoneCode"] = zone["code"]
            data["locationId"] = loc["id"]
            data["locationCode"] = loc["code"]
            data["zoneCount"] = len(zones)
            data["locationCount"] = len(locs)
            return {"warehouse": wh, "zone": zone, "location": loc,
                    "zoneCount": len(zones), "locationCount": len(locs)}

        wh_ok = run_step("04_warehouse_create", step_warehouse)

        def step_warehouse_list():
            PAGE.goto(f"{BASE}/manage/wms/warehouses", wait_until="networkidle")
            PAGE.wait_for_timeout(1600)
            body = PAGE.locator("body").inner_text()
            shot("03-warehouse-list")
            if warehouse_name not in body:
                raise RuntimeError("仓库列表未显示新建仓库")
            if wh_ok and data.get("warehouseCode") and data["warehouseCode"] not in body:
                raise RuntimeError("仓库列表未显示后端生成的仓库编码")
            return {"visible": True, "hasCode": bool(data.get("warehouseCode", "") in body)}

        run_step("05_warehouse_list", step_warehouse_list)

        # ---------- 5. 商品表单无编码输入框验证 ----------
        def step_item_form_check():
            PAGE.goto(f"{BASE}/manage/stk/items", wait_until="networkidle")
            PAGE.wait_for_timeout(1500)
            PAGE.get_by_role("button", name="新增商品").first.click()
            PAGE.wait_for_selector(".el-dialog:visible", timeout=10000)
            dlg = PAGE.locator(".el-dialog:visible").last
            labels = [t.strip() for t in dlg.locator(".el-form-item__label").all_inner_texts()]
            placeholders = dlg.locator("input").evaluate_all("els => els.map(e => e.placeholder || '')")
            hint = ""
            try:
                hint = dlg.locator(".qlx-form-dialog__hint").first.inner_text()
            except Exception:  # noqa: BLE001
                hint = ""
            no_code_label = not any("编码" in t for t in labels)
            no_code_input = not any("编码" in t for t in placeholders)
            shot("04a-item-form-no-code")
            dlg.get_by_role("button", name="取消").first.click()
            PAGE.wait_for_timeout(500)
            data["itemForm"] = {"labels": labels, "placeholders": placeholders,
                                "hint": hint, "noCodeLabel": no_code_label,
                                "noCodeInput": no_code_input}
            if not (no_code_label and no_code_input):
                raise RuntimeError(f"商品表单出现编码输入：labels={labels}, placeholders={placeholders}")
            return data["itemForm"]

        run_step("06_item_form_no_code", step_item_form_check)

        # ---------- 6. 新建商品（API）→ 审批 → 通过 → 列表可见 ----------
        item_name = f"冒烟测试商品{STAMP}"

        def step_item_create():
            req = api("POST", "/api/stk/items", {
                "name": item_name,
                "spec": "10kg/箱",
                "barcode": None,
                "categoryId": data["categoryId"],
                "unitId": data["unitId"],
                "batchEnabled": True,
                "expiryEnabled": False,
                "taxRate": 0.13,
                "refCostPrice": 10,
                "guidePrice": 12,
            })
            data["itemCreateRequest"] = req
            data["itemApprovalInstanceId"] = req.get("approvalInstanceId")
            return req

        item_req = run_step("07_item_create", step_item_create)

        def step_item_approve():
            if not item_req:
                raise RuntimeError("商品新建请求未成功，无法审批")
            inst = data["itemApprovalInstanceId"]
            if not inst:
                raise RuntimeError("商品新建响应缺少 approvalInstanceId")
            result = approve_until_terminal(inst, max_times=3)

            def find_item():
                rows = api("GET", "/api/stk/items")
                return next((r for r in rows if r["name"] == item_name), None)

            item = wait_for(find_item, timeout=90, interval=1.5, label="商品审批落库")
            data["itemId"] = item["id"]
            data["itemCode"] = item["itemCode"]
            data["itemStatus"] = item["status"]
            data["itemBatchEnabled"] = item["batchEnabled"]
            data["itemApproval"] = {"approvedActions": result["approvedActions"], "finalStatus": result["finalStatus"]}
            return {"item": item, "approval": data["itemApproval"]}

        run_step("08_item_approve", step_item_approve)

        def step_item_list():
            PAGE.goto(f"{BASE}/manage/stk/items", wait_until="networkidle")
            PAGE.wait_for_timeout(1500)
            PAGE.get_by_placeholder("编码 / 名称 / 规格 / 条码").first.fill(item_name)
            click_search()
            body = PAGE.locator("body").inner_text()
            shot("04-item-list")
            if item_name not in body:
                raise RuntimeError("商品列表未显示审批通过的按钮")
            if not data.get("itemCode") or data["itemCode"] not in body:
                raise RuntimeError(f"商品列表未显示非空商品编码：{data.get('itemCode')}")
            return {"itemCode": data["itemCode"], "visible": True}

        run_step("09_item_list", step_item_list)

        # ---------- 7. 期初建账（API）→ 提交审批 → 列表/审批中心截图 ----------
        opening_batch_no = f"{STAMP}-B01"
        opening_batch_no_placeholder = True

        def step_opening_create():
            detail = api("POST", "/api/stk/openings", {
                "warehouseId": data["warehouseId"],
                "remark": "2A 冒烟期初 10×10",
                "lines": [{
                    "itemId": data["itemId"],
                    "batchNo": opening_batch_no,
                    "qty": 10,
                    "unitCost": 10,
                }],
            })
            data["openingId"] = detail["header"]["id"]
            data["openingBatchNo"] = detail["header"]["batchNo"]
            data["openingStatusDraft"] = detail["header"]["status"]
            data["openingItems"] = detail["items"]
            return detail

        opening = run_step("10_opening_create", step_opening_create)

        def step_opening_list_draft():
            PAGE.goto(f"{BASE}/manage/stk/openings", wait_until="networkidle")
            PAGE.wait_for_timeout(1500)
            body = PAGE.locator("body").inner_text()
            shot("05-opening-list")
            if data.get("openingBatchNo") and data["openingBatchNo"] not in body:
                raise RuntimeError("期初列表未显示新建期初单")
            return {"batchNo": data.get("openingBatchNo"), "bodyHasBatchNo": True}

        run_step("11_opening_list", step_opening_list_draft)

        def step_opening_submit():
            if not opening:
                raise RuntimeError("期初草稿未创建，无法提交")
            req = api("POST", f"/api/stk/openings/{data['openingId']}/submit")
            data["openingApprovalInstanceId"] = req.get("approvalInstanceId")
            if not data["openingApprovalInstanceId"]:
                raise RuntimeError("期初提交响应缺少 approvalInstanceId")
            return req

        run_step("12_opening_submit", step_opening_submit)

        def step_approval_center_shot():
            inst_id = data["openingApprovalInstanceId"]
            todos = api("GET", "/api/support/approvals/my-todos")
            matched = [t for t in (todos or []) if t.get("instanceId") == inst_id]
            if not matched:
                raise RuntimeError(f"审批中心待办未包含期初过账实例 {inst_id}：{todos}")
            PAGE.goto(f"{BASE}/manage/support/approvals", wait_until="networkidle")
            PAGE.wait_for_timeout(2200)
            body = PAGE.locator("body").inner_text()
            shot("07-approval-center")
            data["openingTodo"] = matched[0]
            data["todoCountAtSubmit"] = len(todos or [])
            if inst_id not in body:
                raise RuntimeError(f"审批中心页面未显示待办实例号 {inst_id}")
            if matched[0].get("nodeName") not in body:
                raise RuntimeError(f"审批中心页面未显示节点名 {matched[0].get('nodeName')}")
            return {"instanceId": inst_id, "nodeName": matched[0].get("nodeName"),
                    "todoCount": len(todos or [])}

        run_step("13_approval_center", step_approval_center_shot)

        def step_opening_approve():
            inst = data["openingApprovalInstanceId"]
            result = approve_until_terminal(inst, max_times=6)
            detail = wait_for(
                lambda: (lambda d: d if d["header"]["status"] == 3 else None)(
                    api("GET", f"/api/stk/openings/{data['openingId']}")),
                timeout=90, interval=1.5, label="期初过账落库")
            data["openingStatusPost"] = detail["header"]["status"]
            data["openingPostApproval"] = {"approvedActions": result["approvedActions"],
                                           "finalStatus": result["finalStatus"]}
            return {"finalStatus": result["finalStatus"],
                    "approvedActions": result["approvedActions"],
                    "openingStatus": detail["header"]["status"]}

        run_step("14_opening_approve", step_opening_approve)

        def step_opening_list_posted():
            PAGE.goto(f"{BASE}/manage/stk/openings", wait_until="networkidle")
            PAGE.wait_for_timeout(1600)
            body = PAGE.locator("body").inner_text()
            shot("06-opening-created")
            if data.get("openingBatchNo") and data["openingBatchNo"] not in body:
                raise RuntimeError("期初列表未显示已过账期初单")
            if "已过账" not in body:
                raise RuntimeError("期初列表未显示已过账状态")
            return {"batchNo": data.get("openingBatchNo"), "postedVisible": True}

        run_step("15_opening_posted_list", step_opening_list_posted)

        # ---------- 8. API 核对台账 / 流水 / 批次 ----------
        def step_api_verify():
            balances = api("GET", f"/api/stk/ledger?itemId={urllib.parse.quote(data['itemId'])}&warehouseId={urllib.parse.quote(data['warehouseId'])}")
            txns = api("GET", f"/api/stk/txns?itemId={urllib.parse.quote(data['itemId'])}&warehouseId={urllib.parse.quote(data['warehouseId'])}")
            batches = api("GET", f"/api/stk/batches?itemId={urllib.parse.quote(data['itemId'])}&warehouseId={urllib.parse.quote(data['warehouseId'])}")
            data["apiBalances"] = balances
            data["apiTxns"] = txns
            data["apiBatches"] = batches
            if not balances:
                raise RuntimeError("库存台账 API 无余额行")
            bal = balances[0]
            if float(bal["qtyOnhand"]) != 10.0:
                raise RuntimeError(f"台账数量不是 10：{bal}")
            if bal.get("amount") is None or abs(float(bal["amount"]) - 100.0) > 0.001:
                raise RuntimeError(f"台账金额不是 100：{bal}")
            if bal.get("avgCost") is None or abs(float(bal["avgCost"]) - 10.0) > 0.0001:
                raise RuntimeError(f"台账均价不是 10：{bal}")
            if len(txns) != 1:
                raise RuntimeError(f"流水应为 1 条，实际 {len(txns)}：{txns}")
            txn = txns[0]
            if int(txn["direction"]) != 1 or float(txn["qty"]) != 10.0 or float(txn["balanceQtyAfter"]) != 10.0:
                raise RuntimeError(f"流水字段不符：{txn}")
            if int(txn["sourceType"]) != 1:
                raise RuntimeError(f"流水来源类型不是期初(1)：{txn}")
            if not batches:
                raise RuntimeError("批次商品未生成批次库存")
            if data.get("itemBatchEnabled") and not batches[0].get("batchNo"):
                raise RuntimeError(f"批次库存批次号为空：{batches}")
            return {"balance": bal, "txnCount": len(txns), "txn": txn,
                    "batchCount": len(batches), "batch": batches[0]}

        run_step("16_api_verify", step_api_verify)

        # ---------- 9. 台账 / 流水 / 批次 / 成本调整 页面截图 ----------
        def step_ledger_page():
            PAGE.goto(f"{BASE}/manage/stk/ledger", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            body = PAGE.locator("body").inner_text()
            shot("08-ledger")
            if data.get("itemCode") and data["itemCode"] not in body:
                raise RuntimeError("库存台账页面未展示新建商品编码")
            if "10" not in body:
                raise RuntimeError("库存台账页面未展示数量 10")
            return {"visible": True, "hasItemCode": data["itemCode"] in body}

        run_step("17_ledger_page", step_ledger_page)

        def step_txn_page():
            PAGE.goto(f"{BASE}/manage/stk/txns", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            body = PAGE.locator("body").inner_text()
            shot("09-txns")
            if data.get("itemCode") and data["itemCode"] not in body:
                raise RuntimeError("出入库流水页面未展示新建商品编码")
            if "期初" not in body:
                raise RuntimeError("出入库流水页面未展示期初来源")
            return {"visible": True, "hasItemCode": data["itemCode"] in body}

        run_step("18_txn_page", step_txn_page)

        def step_batch_page():
            PAGE.goto(f"{BASE}/manage/stk/batches", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            item_label = f"{data['itemCode']} · {item_name}"
            open_select_option("商品", item_label)
            open_select_option("仓库", warehouse_name)
            click_search()
            body = PAGE.locator("body").inner_text()
            shot("10-batches")
            if opening_batch_no not in body:
                raise RuntimeError(f"批次库存页面未显示批次号 {opening_batch_no}")
            return {"batchNo": opening_batch_no, "visible": True}

        run_step("19_batch_page", step_batch_page)

        def step_cost_adjust_page():
            PAGE.goto(f"{BASE}/manage/stk/cost-adjust", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            shot("11-cost-adjust")
            body = PAGE.locator("body").inner_text()
            if "成本调整" not in body:
                raise RuntimeError("成本调整页面标题缺失")
            return {"visible": True}

        run_step("20_cost_adjust_page", step_cost_adjust_page)

        # ---------- 10. SQL 直连核对 ----------
        def step_sql():
            if not data.get("itemCode") or not data.get("warehouseCode") or not data.get("openingBatchNo"):
                raise RuntimeError("缺少 SQL 核对所需编码")
            db = resolve_tenant_db(data["warehouseCode"])
            RESULT["sqlDatabase"] = db
            sql = run_sql_checks(db)
            RESULT["sql"] = sql
            checks = {}
            bal = (sql.get("balances") or [{}])[0]
            txn = (sql.get("txns") or [{}])[0]
            opening_row = (sql.get("opening") or [{}])[0]
            opening_item = (sql.get("openingItems") or [{}])[0]
            sb = (sql.get("stockBatches") or [{}])[0]
            checks["balanceQtyOnhand=10"] = bal and float(bal.get("qty_onhand", -1)) == 10.0
            checks["balanceAmount=100"] = bal and abs(float(bal.get("amount", -1)) - 100.0) < 0.001
            checks["balanceAvgCost=10"] = bal and abs(float(bal.get("avg_cost", -1)) - 10.0) < 0.0001
            checks["txnDirection=1"] = txn and int(txn.get("direction", 0)) == 1
            checks["txnQty=10"] = txn and float(txn.get("qty", -1)) == 10.0
            checks["txnBalanceQtyAfter=10"] = txn and float(txn.get("balance_qty_after", -1)) == 10.0
            checks["txnSourceType=1"] = txn and int(txn.get("source_type", 0)) == 1
            checks["openingStatus=3"] = opening_row and int(opening_row.get("status", 0)) == 3
            checks["openingQty=10"] = opening_item and float(opening_item.get("qty", -1)) == 10.0
            checks["openingAmount=100"] = opening_item and abs(float(opening_item.get("amount", -1)) - 100.0) < 0.001
            checks["batchStockQty=10"] = sb and float(sb.get("qty", -1)) == 10.0
            checks["zoneCount>=1"] = (sql.get("zones") or [{}])[0].get("cnt", 0) >= 1
            checks["locationCount>=1"] = (sql.get("locations") or [{}])[0].get("cnt", 0) >= 1
            RESULT["sqlChecks"] = checks
            failed = [k for k, v in checks.items() if not v]
            if failed:
                raise RuntimeError(f"SQL 核对未通过：{failed}")
            return {"checks": checks, "raw": sql}

        run_step("21_sql_verify", step_sql)

        browser.close()


if __name__ == "__main__":
    try:
        main()
    finally:
        RESULT["finishedAt"] = datetime.now().isoformat(timespec="seconds")
        RESULT["ok"] = all(s.get("ok") for s in steps.values()) if steps else False
        RESULT["stepSummary"] = {k: bool(v.get("ok")) for k, v in steps.items()}
        with open(RESULT_PATH, "w", encoding="utf-8") as fh:
            json.dump(RESULT, fh, ensure_ascii=False, indent=2)
        print("RESULT", RESULT_PATH)
