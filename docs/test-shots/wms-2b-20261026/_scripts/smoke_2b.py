# -*- coding: utf-8 -*-
"""阶段三第二波 2B（04 物流作业全量）端到端冒烟 + 截图取证。

链路：仓库 A/B → 商品（批次）→ 入库 → 出库 → 调拨（审批/发货/在途/收货）
      → 盘点（生成冻结/初盘/复盘/解冻）→ 盘盈盘亏（审批）→ 预警（扫描/处理）
      → 台账/流水/命令 Outbox SQL 核对 → 六页面截图。
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
from datetime import date, datetime

from playwright.sync_api import sync_playwright

BASE = "http://localhost:5173"
API = "http://localhost:5002"
OUT = r"e:\ERPv2\eprv3\docs\test-shots\wms-2b-20261026"
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
    "title": "阶段三第二波 2B（04 物流作业全量）端到端冒烟",
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
DB_NAME = ""
PARAM_SAVED = False


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
        with urllib.request.urlopen(req, timeout=90) as resp:
            status = resp.status
            text = resp.read().decode("utf-8")
    except urllib.error.HTTPError as exc:
        status = exc.code
        text = exc.read().decode("utf-8", "replace")
    entry = {"method": method, "path": path, "status": status}
    api_calls.append(entry)
    if status >= 400:
        entry["body"] = text[:800]
        http_errors.append(entry.copy())
        raise RuntimeError(f"{method} {path} -> HTTP {status}: {text[:500]}")
    parsed = json.loads(text) if text else None
    if isinstance(parsed, dict) and "success" in parsed:
        if not parsed.get("success"):
            raise RuntimeError(f"{method} {path} -> {parsed.get('code')}: {parsed.get('message')}")
        return parsed.get("data")
    return parsed


def api_raw(method, path, payload=None, token=None):
    """底层调用：返回 (status, 解析结果或原文)，不写入 httpErrors（用于可重试场景）。"""
    url = API + path
    headers = {"Accept": "application/json", "Authorization": f"Bearer {token or TOKEN}"}
    body = None
    if payload is not None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=90) as resp:
            status = resp.status
            text = resp.read().decode("utf-8")
    except urllib.error.HTTPError as exc:
        status = exc.code
        text = exc.read().decode("utf-8", "replace")
    api_calls.append({"method": method, "path": path, "status": status})
    try:
        parsed = json.loads(text) if text else None
    except json.JSONDecodeError:
        parsed = text
    return status, parsed


def create_warehouse_with_retry(name, address, remark):
    """新建仓库。开发环境序号存储在 Host 重启后可能回退，撞历史编码时重试直到取到空闲流水。"""
    attempts = []
    for _ in range(60):
        status, parsed = api_raw("POST", "/api/wms/warehouses", {
            "name": name, "whType": 1, "orgId": None,
            "address": address, "keeperId": None, "remark": remark,
        })
        attempts.append(status)
        if status < 400 and isinstance(parsed, dict) and parsed.get("success"):
            data.setdefault("warehouseCodeRetry", []).append(len(attempts))
            return parsed.get("data")
        message = json.dumps(parsed, ensure_ascii=False) if isinstance(parsed, dict) else str(parsed)
        if "IX_wms_warehouses_tenant_id_code" not in message:
            raise RuntimeError(f"POST /api/wms/warehouses -> HTTP {status}: {message[:400]}")
        time.sleep(0.2)
    raise RuntimeError(f"仓库编码连续撞号，重试 {len(attempts)} 次仍失败")


def post_with_docno_retry(path, payload, label, attempts=12):
    """新建单据。开发环境序号存储为内存实现（Host 重启后流水归 1），撞历史单号时重试取下一流水。"""
    trail = []
    for index in range(attempts):
        status, parsed = api_raw("POST", path, payload)
        trail.append(status)
        if status < 400 and isinstance(parsed, dict) and parsed.get("success"):
            data.setdefault("docNoRetry", []).append({label: index + 1})
            return parsed.get("data")
        message = json.dumps(parsed, ensure_ascii=False) if isinstance(parsed, dict) else str(parsed)
        if "Cannot insert duplicate key row" not in message:
            raise RuntimeError(f"POST {path} -> HTTP {status}: {message[:400]}")
        time.sleep(0.2)
    raise RuntimeError(f"{label} 单号连续撞号，重试 {attempts} 次仍失败（状态序列 {trail}）")


def wait_for(fn, timeout=90, interval=1.0, label="条件"):
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
            return {"approvedActions": approved, "finalStatus": 2}
        if status != 1:
            raise RuntimeError(f"审批实例状态异常：{status}（instance={instance_id}）")
        api("POST", f"/api/support/approvals/{instance_id}/approve", {"comment": "2B 冒烟自动通过"})
        approved += 1
        time.sleep(0.4)
    inst = api("GET", f"/api/support/approvals/instance/{instance_id}")
    raise RuntimeError(f"审批未在 {max_times} 次动作内终止：status={inst.get('status')}")


def find_instance(biz_no, biz_type):
    """按业务单号在「我发起的」审批中定位实例；失败回退 SQL 查 wms_approval_requests。

    注意：业务 Id 为混淆字符串，不可转 int；SQL 回退按 biz_type 取本租户最新一条已绑定实例。
    """
    def probe():
        try:
            items = api("GET", "/api/support/approvals/my-initiated")
        except Exception:  # noqa: BLE001
            items = None
        for item in items or []:
            if item.get("bizNo") == biz_no:
                return item.get("instanceId")
        # SQL 回退（权限或索引差异时）
        try:
            rows = run_sql_script(DB_NAME, (
                "$res = @{ req = Run \"SELECT TOP 1 approval_instance_id FROM wms_approval_requests "
                f"WHERE tenant_id=$tenant AND biz_type='{biz_type}' "
                "AND approval_instance_id IS NOT NULL ORDER BY id DESC\" }"))
            items2 = rows.get("req") or []
            if items2 and items2[0].get("approval_instance_id"):
                return items2[0]["approval_instance_id"]
        except Exception:  # noqa: BLE001
            pass
        return None

    return wait_for(probe, timeout=45, interval=1.0, label=f"审批实例（{biz_no}）")


def dispatch_stock_commands(label):
    try:
        res = api("POST", "/api/support/jobs/trigger",
                  {"taskCode": "wms.stock.command.dispatch", "mode": "sync"})
        status = str((res or {}).get("status") or "").lower()
        if status != "success":
            raise RuntimeError(f"分发任务状态 {status}：{res}")
        return {"mode": "sync", "status": status}
    except Exception as exc:  # noqa: BLE001
        print("DISPATCH_FALLBACK", label, exc)
        return {"mode": "fallback", "error": str(exc)}


def run_alert_scan():
    res = api("POST", "/api/support/jobs/trigger",
              {"taskCode": "wms.alert.scan", "mode": "sync"})
    if str((res or {}).get("status") or "").lower() != "success":
        raise RuntimeError(f"预警扫描任务失败：{res}")
    return res


def ledger(item_id, warehouse_id):
    rows = api("GET", f"/api/stk/ledger?itemId={urllib.parse.quote(str(item_id))}"
                      f"&warehouseId={urllib.parse.quote(str(warehouse_id))}")
    # Id 为混淆字符串，统一按字符串比较
    return next((r for r in (rows or []) if str(r["warehouseId"]) == str(warehouse_id)), None)


def txns(item_id, warehouse_id):
    return api("GET", f"/api/stk/txns?itemId={urllib.parse.quote(str(item_id))}"
                     f"&warehouseId={urllib.parse.quote(str(warehouse_id))}")


def txns_all(item_id):
    return api("GET", f"/api/stk/txns?itemId={urllib.parse.quote(str(item_id))}")


def click_search():
    PAGE.locator(".qlx-search").get_by_role("button", name="查询").first.click()
    PAGE.wait_for_timeout(1200)


# ============================ SQL ============================

PS_HEADER = r'''
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
'''

PS_FOOTER = r'''
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
    script = DISCOVER_DB_PS.replace("__WH_CODE__", wh_code)
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    proc = subprocess.run(
        ["powershell", "-NoProfile", "-EncodedCommand", encoded],
        capture_output=True, text=True, encoding="utf-8", timeout=240,
    )
    db = (proc.stdout or "").strip()
    if not db:
        raise RuntimeError(f"未找到包含仓库 {wh_code} 的租户库：{proc.stderr[:500]}")
    return db


def run_sql_script(db, body):
    script = (PS_HEADER.replace("__DB__", db).replace("__TENANT__", str(TENANT_ID))
              + body + PS_FOOTER)
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    proc = subprocess.run(
        ["powershell", "-NoProfile", "-EncodedCommand", encoded],
        capture_output=True, text=True, encoding="utf-8", timeout=180,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"PowerShell 退出码 {proc.returncode}: {proc.stderr[:800]}")
    out = (proc.stdout or "").strip()
    if not out:
        raise RuntimeError(f"SQL 无输出：{proc.stderr[:800]}")
    return json.loads(out)


def build_full_sql_body():
    item = data["itemCode"]
    wh_a = data["warehouseCodeA"]
    wh_b = data["warehouseCodeB"]
    transfer_no = data["transferNo"]
    take_no = data["takeNo"]
    recheck_no = data["recheckTakeNo"]
    adjust_no = data["adjustNo"]
    in_no = data["stockInTaskNo"]
    out_no = data["stockOutTaskNo"]
    # 命令 Outbox 按本轮单据号圈定，避免历史冒烟残留行干扰判定
    outbox_where = (
        "tenant_id=$tenant AND (idempotency_key LIKE '%" + in_no + "%'"
        " OR idempotency_key LIKE '%" + out_no + "%'"
        " OR idempotency_key LIKE '%" + transfer_no + "%'"
        " OR idempotency_key LIKE '%" + adjust_no + "%')")
    return f'''
$res = @{{
  balances = Run "SELECT w.code AS wh_code, b.qty_onhand, b.amount, b.avg_cost, b.qty_locked FROM stk_stock_balances b JOIN stk_items i ON i.id=b.item_id JOIN wms_warehouses w ON w.id=b.warehouse_id WHERE i.tenant_id=$tenant AND i.item_code='{item}' ORDER BY w.code"
  txns = Run "SELECT w.code AS wh_code, t.txn_no, t.source_type, t.direction, t.qty, t.unit_cost, t.amount, t.balance_qty_after, t.batch_no, t.source_doc_no FROM stk_stock_txns t JOIN stk_items i ON i.id=t.item_id JOIN wms_warehouses w ON w.id=t.warehouse_id WHERE i.tenant_id=$tenant AND i.item_code='{item}' ORDER BY t.id"
  outboxGroups = Run "SELECT state, COUNT(*) AS cnt FROM wms_stock_command_outbox WHERE {outbox_where} GROUP BY state"
  outboxRows = Run "SELECT TOP 20 idempotency_key, state, retry_count, last_error, dispatched_at FROM wms_stock_command_outbox WHERE {outbox_where} ORDER BY id DESC"
  transfers = Run "SELECT transfer_no, status FROM wms_stock_transfers WHERE tenant_id=$tenant AND transfer_no='{transfer_no}'"
  transit = Run "SELECT transfer_no, qty, unit_cost, status FROM wms_in_transit_stocks WHERE tenant_id=$tenant AND transfer_no='{transfer_no}'"
  takes = Run "SELECT take_no, round, status FROM wms_stock_takes WHERE tenant_id=$tenant AND take_no IN ('{take_no}', '{recheck_no}')"
  takeLines = Run "SELECT t.take_no, l.line_no, l.book_qty, l.actual_qty, l.diff_qty, l.frozen FROM wms_stock_take_lines l JOIN wms_stock_takes t ON t.id=l.take_id WHERE t.tenant_id=$tenant AND t.take_no IN ('{take_no}', '{recheck_no}') ORDER BY t.take_no, l.line_no"
  adjusts = Run "SELECT adjust_no, type, status, take_id, total_amount FROM wms_inventory_adjust_docs WHERE tenant_id=$tenant AND adjust_no='{adjust_no}'"
  alerts = Run "SELECT a.alert_type, a.threshold, a.actual_value, a.status, w.code AS wh_code FROM wms_inventory_alerts a JOIN wms_warehouses w ON w.id=a.warehouse_id WHERE a.tenant_id=$tenant AND w.code IN ('{wh_a}', '{wh_b}') ORDER BY a.id"
  approvalReqs = Run "SELECT biz_type, status FROM wms_approval_requests WHERE tenant_id=$tenant ORDER BY id"
}}
'''


# ============================ 主流程 ============================

def main():
    global TOKEN, IDENTITY, PAGE, DB_NAME
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

        def step_login():
            session = do_login(shot_login=True)
            shot("01-workbench")
            globals()["IDENTITY"] = session.get("identityToken") or ""
            data["session"] = {
                "licensedModules": session.get("licensedModules"),
                "tenantTokenLength": len(session.get("tenantToken") or ""),
            }
            return data["session"]

        if not run_step("01_login", step_login):
            RESULT["finishedAt"] = datetime.now().isoformat(timespec="seconds")
            RESULT["fatal"] = "登录失败，后续步骤无法执行"
            with open(RESULT_PATH, "w", encoding="utf-8") as fh:
                json.dump(RESULT, fh, ensure_ascii=False, indent=2)
            browser.close()
            return

        # ---------- 1.5 模块许可（环境前置） ----------
        TAX_NO = "91510100MA0000000X"

        def step_license():
            initial = list((data.get("session") or {}).get("licensedModules") or [])
            desired = sorted({*initial, "portal", "bas", "support", "wms", "stk"})
            added = [code for code in desired if code not in initial]
            if added:
                listed = api("GET", f"/api/portal/admin/tenants?page=1&pageSize=20&keyword={TAX_NO}", token=IDENTITY)
                items = listed.get("items") if isinstance(listed, dict) else listed
                target = next((t for t in (items or []) if t.get("taxNo") == TAX_NO), None)
                if target is None:
                    raise RuntimeError(f"门户租户列表未找到税号 {TAX_NO}")
                api("PUT", f"/api/portal/admin/tenants/{target['id']}/modules",
                    {"moduleCodes": desired, "remark": "2B 冒烟环境前置：补齐模块授权"}, token=IDENTITY)
                session = do_login()
                final = list(session.get("licensedModules") or [])
            else:
                final = initial
            if "wms" not in final or "stk" not in final or "support" not in final:
                raise RuntimeError(f"许可缺少 wms/stk/support：{final}")
            data["license"] = {"initial": initial, "added": added, "final": final}
            return data["license"]

        run_step("01b_license", step_license)

        # ---------- 2. 菜单可见性（2B 六页） ----------
        def step_menu():
            snapshot = api("GET", "/api/tenant/menus")
            menus = (snapshot or {}).get("menus") or []
            routes = [m.get("route") for m in menus]
            required = ["/manage/wms/inbound", "/manage/wms/outbound", "/manage/wms/transfers",
                        "/manage/wms/stocktakes", "/manage/wms/adjustments", "/manage/wms/alerts"]
            missing = [r for r in required if r not in routes]
            if missing:
                raise RuntimeError(f"租户菜单缺少 2B 路由：{missing}")
            PAGE.goto(f"{BASE}/manage/wms/inbound", wait_until="networkidle")
            PAGE.wait_for_timeout(1600)
            sidebar = PAGE.locator("aside").inner_text() if PAGE.locator("aside").count() else ""
            labels = ["入库作业", "出库作业", "物资调拨", "库存盘点", "盘盈盘亏", "库存预警"]
            flags = {name: (name in sidebar) for name in labels}
            shot("02-menu")
            missing_items = [k for k, v in flags.items() if not v]
            if missing_items:
                raise RuntimeError(f"侧边栏缺少 2B 菜单：{missing_items}")
            data["tenantMenus"] = {"routes": routes, "sidebarItems": flags}
            return {"routes": required, "sidebarItems": flags}

        run_step("02_menu", step_menu)

        # ---------- 3. 基础主数据 ----------
        def step_base_master():
            categories = api("GET", "/api/bas/categories")
            category = categories[0] if categories else api("POST", "/api/bas/categories", {
                "name": f"2B冒烟分类{STAMP}", "parentId": None, "unitGroupId": None})
            groups = api("GET", "/api/bas/unit-groups")
            group = groups[0] if groups else api("POST", "/api/bas/unit-groups", {"name": f"2B冒烟单位组{STAMP}"})
            units = api("GET", "/api/bas/units")
            unit = units[0] if units else api("POST", "/api/bas/units", {
                "name": "件", "unitGroupId": group["id"], "precision": 0})
            data["categoryId"] = category["id"]
            data["categoryName"] = category["name"]
            data["unitId"] = unit["id"]
            return {"category": category["name"], "unit": unit["name"]}

        run_step("03_base_master", step_base_master)

        # ---------- 4. 仓库 A（调出）+ 库区/库位 ----------
        wh_a_name = f"2B冒烟调出仓{STAMP}"
        wh_b_name = f"2B冒烟调入仓{STAMP}"

        def step_warehouse_a():
            wh = create_warehouse_with_retry(
                wh_a_name, "四川省成都市 2B 冒烟地址 A", "2B 冒烟自动创建")
            zone = api("POST", f"/api/wms/warehouses/{wh['id']}/zones", {
                "code": f"ZA{STAMP}", "name": f"2B冒烟库区A{STAMP}", "sortOrder": 1})
            loc = api("POST", f"/api/wms/warehouses/{wh['id']}/locations?zoneId={urllib.parse.quote(zone['id'])}", {
                "code": f"LA{STAMP}", "barcode": None, "sortOrder": 1})
            data["warehouseIdA"] = wh["id"]
            data["warehouseCodeA"] = wh["code"]
            data["warehouseNameA"] = wh["name"]
            data["zoneId"] = zone["id"]
            data["locationId"] = loc["id"]
            return {"warehouse": wh, "zone": zone, "location": loc}

        run_step("04_warehouse_a", step_warehouse_a)

        def step_warehouse_b():
            wh = create_warehouse_with_retry(
                wh_b_name, "四川省成都市 2B 冒烟地址 B", "2B 冒烟自动创建")
            data["warehouseIdB"] = wh["id"]
            data["warehouseCodeB"] = wh["code"]
            data["warehouseNameB"] = wh["name"]
            return {"warehouse": wh}

        run_step("05_warehouse_b", step_warehouse_b)

        # ---------- 5. 定位租户库（后续 SQL 复用） ----------
        def step_resolve_db():
            globals()["DB_NAME"] = resolve_tenant_db(data["warehouseCodeA"])
            RESULT["sqlDatabase"] = DB_NAME
            return {"database": DB_NAME}

        run_step("05b_resolve_db", step_resolve_db)

        # ---------- 6. 商品（批次启用）创建 + 审批 ----------
        item_name = f"2B冒烟商品{STAMP}"

        def step_item_create():
            req = api("POST", "/api/stk/items", {
                "name": item_name, "spec": "20kg/袋", "barcode": None,
                "categoryId": data["categoryId"], "unitId": data["unitId"],
                "batchEnabled": True, "expiryEnabled": False,
                "taxRate": 0.13, "refCostPrice": 10, "guidePrice": 12,
            })
            inst = req.get("approvalInstanceId")
            if not inst:
                raise RuntimeError(f"商品新建响应缺少 approvalInstanceId：{req}")
            approve_until_terminal(inst, max_times=3)
            item = wait_for(lambda: next((r for r in api("GET", "/api/stk/items")
                                          if r["name"] == item_name and int(r["status"]) == 1), None),
                            timeout=120, interval=1.5, label="商品审批落库")
            data["itemId"] = item["id"]
            data["itemCode"] = item["itemCode"]
            data["itemBatchEnabled"] = item["batchEnabled"]
            return {"itemId": item["id"], "itemCode": item["itemCode"],
                    "batchEnabled": item["batchEnabled"]}

        run_step("06_item_create", step_item_create)

        # ---------- 7. 入库（采购入库 100@10）+ 命令分发 ----------
        batch_no = f"{STAMP}-B1"
        in_doc_no = f"PO{STAMP}"
        data["batchNo"] = batch_no

        def step_stock_in():
            detail = post_with_docno_retry("/api/wms/stock-in-tasks", {
                "sourceType": 1, "sourceDocNo": in_doc_no,
                "warehouseId": data["warehouseIdA"], "remark": "2B 冒烟采购入库 100@10",
                "lines": [{"itemId": data["itemId"], "expectQty": 100,
                           "locationId": data["locationId"], "batchNo": batch_no, "unitCost": 10}],
            }, "入库作业")
            task_id = detail["task"]["id"]
            data["stockInTaskNo"] = detail["task"]["taskNo"]
            api("POST", f"/api/wms/stock-in-tasks/{task_id}/start")
            line_id = detail["lines"][0]["id"]
            done = api("POST", f"/api/wms/stock-in-tasks/{task_id}/complete", {
                "lines": [{"lineId": line_id, "countQty": 100, "confirmQty": 100,
                           "locationId": data["locationId"], "batchNo": batch_no, "unitCost": 10}]})
            if int(done["task"]["status"]) != 3:
                raise RuntimeError(f"入库未完成：{done['task']}")
            dispatch = dispatch_stock_commands("入库")
            bal = wait_for(lambda: (lambda b: b if b and float(b.get("qtyOnhand") or 0) == 100 else None)(
                ledger(data["itemId"], data["warehouseIdA"])), timeout=120, label="入库台账 100")
            data["stockInLedger"] = bal
            return {"taskNo": data["stockInTaskNo"], "dispatch": dispatch,
                    "balance": {"qtyOnhand": bal.get("qtyOnhand"), "avgCost": bal.get("avgCost"),
                                "amount": bal.get("amount")}}

        run_step("07_stock_in", step_stock_in)

        # ---------- 8. 出库（销售出库 30，小额直过）+ 命令分发 ----------
        out_doc_no = f"SO{STAMP}"

        def step_stock_out():
            detail = post_with_docno_retry("/api/wms/stock-out-tasks", {
                "sourceType": 1, "sourceDocNo": out_doc_no,
                "warehouseId": data["warehouseIdA"], "remark": "2B 冒烟销售出库 30",
                "lines": [{"itemId": data["itemId"], "applyQty": 30,
                           "locationId": data["locationId"], "batchNo": batch_no}],
            }, "出库作业")
            task_id = detail["task"]["id"]
            line_id = detail["lines"][0]["id"]
            data["stockOutTaskNo"] = detail["task"]["taskNo"]
            api("POST", f"/api/wms/stock-out-tasks/{task_id}/pick", {
                "lines": [{"lineId": line_id, "pickQty": 30,
                           "locationId": data["locationId"], "batchNo": batch_no}]})
            api("POST", f"/api/wms/stock-out-tasks/{task_id}/check", {
                "handoverTo": "冒烟交接人", "lines": [{"lineId": line_id, "checkQty": 30}]})
            done = api("POST", f"/api/wms/stock-out-tasks/{task_id}/complete")
            if done.get("approvalRequired"):
                raise RuntimeError(f"小额出库不应转大额审批：{done}")
            data["stockOutTotalAmount"] = done.get("totalAmount")
            dispatch = dispatch_stock_commands("出库")
            bal = wait_for(lambda: (lambda b: b if b and float(b.get("qtyOnhand") or 0) == 70 else None)(
                ledger(data["itemId"], data["warehouseIdA"])), timeout=120, label="出库后台账 70")
            return {"taskNo": data["stockOutTaskNo"], "totalAmount": done.get("totalAmount"),
                    "dispatch": dispatch, "balance": {"qtyOnhand": bal.get("qtyOnhand")}}

        run_step("08_stock_out", step_stock_out)

        # ---------- 9. 调拨新建 → 审批 → 生效 ----------
        def step_transfer_create():
            detail = post_with_docno_retry("/api/wms/transfers", {
                "fromWarehouseId": data["warehouseIdA"], "toWarehouseId": data["warehouseIdB"],
                "inTransitDays": 3, "remark": "2B 冒烟调拨 20",
                "lines": [{"itemId": data["itemId"], "qty": 20, "batchNo": batch_no}],
            }, "调拨单")
            transfer = detail["transfer"]
            data["transferId"] = transfer["id"]
            data["transferNo"] = transfer["transferNo"]
            if int(transfer["status"]) != 1:
                raise RuntimeError(f"调拨新建后应待审批：{transfer}")
            inst = find_instance(data["transferNo"], "wms.transfer")
            data["transferApprovalInstanceId"] = inst
            result = approve_until_terminal(inst, max_times=6)
            effective = wait_for(
                lambda: (lambda d: d if int(d["transfer"]["status"]) == 2 else None)(
                    api("GET", f"/api/wms/transfers/{data['transferId']}")),
                timeout=120, label="调拨审批生效")
            return {"transferNo": data["transferNo"], "instanceId": inst,
                    "approvedActions": result["approvedActions"],
                    "status": effective["transfer"]["status"]}

        run_step("09_transfer_create", step_transfer_create)

        def step_transfer_ship():
            detail = api("GET", f"/api/wms/transfers/{data['transferId']}")
            line_id = detail["lines"][0]["id"]
            shipped = api("POST", f"/api/wms/transfers/{data['transferId']}/ship", {
                "lines": [{"lineId": line_id, "shipQty": 20}]})
            if int(shipped["transfer"]["status"]) != 3:
                raise RuntimeError(f"调拨发货后状态不是已发货：{shipped['transfer']}")
            dispatch = dispatch_stock_commands("调拨发货")
            bal = wait_for(lambda: (lambda b: b if b and float(b.get("qtyOnhand") or 0) == 50 else None)(
                ledger(data["itemId"], data["warehouseIdA"])), timeout=120, label="调出仓台账 50")
            transit = api("GET", "/api/wms/transfers/in-transit")
            row = next((r for r in (transit or []) if r.get("transferNo") == data["transferNo"]), None)
            if not row:
                raise RuntimeError(f"在途库存未生成：{transit}")
            data["inTransitRow"] = row
            return {"dispatch": dispatch, "balanceA": bal.get("qtyOnhand"), "inTransit": row}

        run_step("10_transfer_ship", step_transfer_ship)

        def step_transfer_receive():
            detail = api("GET", f"/api/wms/transfers/{data['transferId']}")
            line_id = detail["lines"][0]["id"]
            received = api("POST", f"/api/wms/transfers/{data['transferId']}/receive", {
                "lines": [{"lineId": line_id, "receiveQty": 20, "diffNote": None}]})
            if int(received["transfer"]["status"]) != 4:
                raise RuntimeError(f"调拨收货后状态不是已收货：{received['transfer']}")
            dispatch = dispatch_stock_commands("调拨收货")
            bal = wait_for(lambda: (lambda b: b if b and float(b.get("qtyOnhand") or 0) == 20 else None)(
                ledger(data["itemId"], data["warehouseIdB"])), timeout=120, label="调入仓台账 20")
            transit = api("GET", "/api/wms/transfers/in-transit")
            row = next((r for r in (transit or []) if r.get("transferNo") == data["transferNo"]), None)
            closed = row is None or int(row.get("status") or 0) == 2
            if not closed:
                raise RuntimeError(f"在途库存未关闭：{row}")
            return {"dispatch": dispatch, "balanceB": bal.get("qtyOnhand"),
                    "inTransitClosed": closed, "status": received["transfer"]["status"]}

        run_step("11_transfer_receive", step_transfer_receive)

        # ---------- 10. 盘点计划 → 生成（快照 + 冻结） ----------
        def step_stocktake_generate():
            plan = post_with_docno_retry("/api/wms/stock-take-plans", {
                "planDate": date.today().isoformat(), "scopeType": 1,
                "warehouseId": data["warehouseIdA"], "categoryId": None, "locationId": None,
                "isBlind": False, "remark": "2B 冒烟全仓盘点",
            }, "盘点计划")
            data["planId"] = plan["id"]
            data["planNo"] = plan["planNo"]
            # 生成盘点单可重试：后端已改为先落库（单号冲突在此暴露）再冻结，撞号失败不会遗留已提交的冻结量
            detail = post_with_docno_retry(
                f"/api/wms/stock-take-plans/{plan['id']}/generate", None, "盘点单生成")
            take = detail["take"]
            data["takeId"] = take["id"]
            data["takeNo"] = take["takeNo"]
            data["takeLines"] = detail["lines"]
            bal = ledger(data["itemId"], data["warehouseIdA"])
            if not bal or float(bal.get("qtyLocked") or 0) != 50:
                raise RuntimeError(f"生成盘点单后 qty_locked 不是 50：{bal}")
            data["lockedAfterGenerate"] = bal.get("qtyLocked")
            return {"planNo": data["planNo"], "takeNo": data["takeNo"],
                    "lineCount": len(detail["lines"]), "qtyLocked": bal.get("qtyLocked")}

        run_step("12_stocktake_generate", step_stocktake_generate)

        # ---------- 11. 盘点：开始 → 录入差异 → 完成 → 生成复盘 ----------
        def step_stocktake_initial():
            api("POST", f"/api/wms/stock-takes/{data['takeId']}/start")
            line = next(l for l in data["takeLines"] if str(l["itemId"]) == str(data["itemId"]))
            book = float(line["bookQty"])
            actual = book - 5
            api("POST", f"/api/wms/stock-takes/{data['takeId']}/save-counts", {
                "lines": [{"lineId": line["id"], "actualQty": actual,
                           "diffReason": "2B 冒烟盘亏核实"}]})
            # 初盘完成会生成复盘点单号；撞号时事务回滚且不涉及冻结/解冻，可安全重试
            finished = post_with_docno_retry(
                f"/api/wms/stock-takes/{data['takeId']}/finish", None, "初盘完成")
            recheck_id = finished.get("recheckTakeId")
            if not recheck_id:
                raise RuntimeError(f"初盘有差异但未生成复盘单：{finished}")
            data["recheckTakeId"] = recheck_id
            recheck = api("GET", f"/api/wms/stock-takes/{recheck_id}")
            data["recheckTakeNo"] = recheck["take"]["takeNo"]
            data["recheckLines"] = recheck["lines"]
            bal = ledger(data["itemId"], data["warehouseIdA"])
            if float(bal.get("qtyLocked") or 0) != 50:
                raise RuntimeError(f"初盘完成（待复盘）冻结应保留 50：{bal}")
            return {"book": book, "actual": actual, "diff": actual - book,
                    "recheckTakeNo": data["recheckTakeNo"], "qtyLocked": bal.get("qtyLocked")}

        run_step("13_stocktake_initial", step_stocktake_initial)

        def step_stocktake_recheck():
            recheck_id = data["recheckTakeId"]
            # 复盘单生成即进入「复盘中」（StockTake 构造按 round=2 直接置 Rechecking），无需再次开始
            saves = []
            for line in data["recheckLines"]:
                saves.append({"lineId": line["id"],
                              "actualQty": float(line["bookQty"]) - 5,
                              "diffReason": "2B 冒烟复盘确认"})
            api("POST", f"/api/wms/stock-takes/{recheck_id}/save-counts", {"lines": saves})
            # 复盘完成会生成盘盈亏单号；撞号时 Wms 事务整体回滚（草稿先于解冻落库），可安全重试
            post_with_docno_retry(
                f"/api/wms/stock-takes/{recheck_id}/finish", None, "复盘完成")
            bal = wait_for(lambda: (lambda b: b if b and float(b.get("qtyLocked") or 0) == 0 else None)(
                ledger(data["itemId"], data["warehouseIdA"])), timeout=60, label="盘点解冻")
            docs = api("GET", f"/api/wms/inventory-adjusts?status=1"
                              f"&warehouseId={urllib.parse.quote(str(data['warehouseIdA']))}")
            draft = next((d for d in (docs or []) if d.get("takeId") == recheck_id), None)
            if not draft:
                raise RuntimeError(f"复盘完成未生成盘盈盘亏草稿：{docs}")
            data["adjustId"] = draft["id"]
            data["adjustNo"] = draft["adjustNo"]
            data["adjustType"] = draft["type"]
            return {"qtyLocked": bal.get("qtyLocked"), "adjustNo": draft["adjustNo"],
                    "adjustType": draft["type"], "draftQty": draft.get("totalAmount")}

        run_step("14_stocktake_recheck", step_stocktake_recheck)

        # ---------- 12. 盘盈亏提交审批 → 通过 → 命令分发 ----------
        def step_adjust_approve():
            submitted = api("POST", f"/api/wms/inventory-adjusts/{data['adjustId']}/submit")
            if int(submitted["status"]) != 2:
                raise RuntimeError(f"盘盈亏提交后应待审批：{submitted}")
            inst = find_instance(data["adjustNo"], "wms.inventory.adjust")
            data["adjustApprovalInstanceId"] = inst
            result = approve_until_terminal(inst, max_times=6)
            approved = wait_for(
                lambda: (lambda d: d if int(d["adjust"]["status"]) == 3 else None)(
                    api("GET", f"/api/wms/inventory-adjusts/{data['adjustId']}")),
                timeout=120, label="盘盈亏审批通过")
            dispatch = dispatch_stock_commands("盘盈亏")
            bal = wait_for(lambda: (lambda b: b if b and float(b.get("qtyOnhand") or 0) == 45 else None)(
                ledger(data["itemId"], data["warehouseIdA"])), timeout=120, label="盘亏后台账 45")
            return {"instanceId": inst, "approvedActions": result["approvedActions"],
                    "status": approved["adjust"]["status"], "dispatch": dispatch,
                    "balance": {"qtyOnhand": bal.get("qtyOnhand"), "amount": bal.get("amount"),
                                "avgCost": bal.get("avgCost")}}

        run_step("15_adjust_approve", step_adjust_approve)

        # ---------- 13. 库存预警：阈值 → 扫描 → 处理 ----------
        param_key = "wms.alert.safety.stock"

        def step_alert():
            global PARAM_SAVED
            api("PUT", f"/api/portal/system-parameters/{param_key}",
                {"paramValue": "100", "remark": "2B 冒烟临时阈值（用后删除）"}, token=IDENTITY)
            PARAM_SAVED = True
            scan = run_alert_scan()
            alerts = wait_for(
                lambda: (lambda rows: rows if any(int(r["alertType"]) == 1
                                                  and str(r["warehouseId"]) == str(data["warehouseIdA"])
                                                  for r in rows) else None)(
                    api("GET", f"/api/wms/inventory-alerts?warehouseId={urllib.parse.quote(str(data['warehouseIdA']))}")),
                timeout=90, label="安全库存预警生成")
            alert = next(r for r in alerts if int(r["alertType"]) == 1
                         and str(r["warehouseId"]) == str(data["warehouseIdA"]))
            data["alertId"] = alert["id"]
            data["alertRow"] = alert
            handled = api("POST", f"/api/wms/inventory-alerts/{alert['id']}/handle",
                          {"handledNote": "2B 冒烟已补货核实"})
            if int(handled["status"]) != 2:
                raise RuntimeError(f"预警处理状态异常：{handled}")
            return {"scan": (scan or {}).get("completedResult"),
                    "alert": {"type": alert["alertType"], "threshold": alert["threshold"],
                              "actualValue": alert["actualValue"]},
                    "handledStatus": handled["status"]}

        run_step("16_alert", step_alert)

        # ---------- 14. 六页面截图（2B 前端闭环） ----------
        def step_pages():
            result = {}

            PAGE.goto(f"{BASE}/manage/wms/inbound", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            body = PAGE.locator("body").inner_text()
            shot("03-inbound-list")
            if data["stockInTaskNo"] not in body:
                raise RuntimeError("入库作业页未显示本次入库单号")
            result["inbound"] = True

            PAGE.goto(f"{BASE}/manage/wms/outbound", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            body = PAGE.locator("body").inner_text()
            shot("04-outbound-list")
            if data["stockOutTaskNo"] not in body:
                raise RuntimeError("出库作业页未显示本次出库单号")
            result["outbound"] = True

            PAGE.goto(f"{BASE}/manage/wms/transfers", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            body = PAGE.locator("body").inner_text()
            shot("05-transfer-list")
            if data["transferNo"] not in body:
                raise RuntimeError("调拨页未显示本次调拨单号")
            PAGE.get_by_role("tab", name="在途库存").first.click()
            PAGE.wait_for_timeout(1500)
            body2 = PAGE.locator("body").inner_text()
            shot("06-transfer-in-transit")
            if data["transferNo"] not in body2:
                raise RuntimeError("在途库存页未显示本次调拨单号")
            result["transfer"] = True

            PAGE.goto(f"{BASE}/manage/wms/stocktakes", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            body = PAGE.locator("body").inner_text()
            shot("07-stocktake-plans")
            if data["planNo"] not in body:
                raise RuntimeError("盘点计划页未显示本次计划号")
            PAGE.get_by_role("tab", name="盘点单").first.click()
            PAGE.wait_for_timeout(1500)
            body2 = PAGE.locator("body").inner_text()
            shot("08-stocktake-takes")
            if data["takeNo"] not in body2:
                raise RuntimeError("盘点单页未显示本次盘点单号")
            result["stocktake"] = True

            PAGE.goto(f"{BASE}/manage/wms/adjustments", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            body = PAGE.locator("body").inner_text()
            shot("09-adjustments")
            if data["adjustNo"] not in body:
                raise RuntimeError("盘盈盘亏页未显示本次调整单号")
            result["adjust"] = True

            PAGE.goto(f"{BASE}/manage/wms/alerts", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            body = PAGE.locator("body").inner_text()
            shot("10-alerts")
            if "安全库存" not in body:
                raise RuntimeError("库存预警页未显示安全库存预警类型")
            result["alert"] = True

            PAGE.goto(f"{BASE}/manage/stk/ledger", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            body = PAGE.locator("body").inner_text()
            shot("11-ledger")
            if data["itemCode"] not in body or "45" not in body:
                raise RuntimeError("库存台账页未显示本次商品 45 结存")
            result["ledger"] = True

            PAGE.goto(f"{BASE}/manage/stk/txns", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            body = PAGE.locator("body").inner_text()
            shot("12-txns")
            for keyword in ["采购入库", "销售出库", "调拨出库", "调拨入库", "盘亏"]:
                if keyword not in body:
                    raise RuntimeError(f"出入库流水页缺少来源类型「{keyword}」")
            result["txns"] = True

            PAGE.goto(f"{BASE}/manage/support/approvals", wait_until="networkidle")
            PAGE.wait_for_timeout(1800)
            shot("13-approval-center")
            result["approvalCenter"] = True
            return result

        run_step("17_pages", step_pages)

        # ---------- 15. API 台账/流水终态核对 ----------
        def step_api_verify():
            bal_a = ledger(data["itemId"], data["warehouseIdA"])
            bal_b = ledger(data["itemId"], data["warehouseIdB"])
            rows = txns_all(data["itemId"])
            checks = {
                "balanceA_qty45": float(bal_a.get("qtyOnhand") or 0) == 45,
                "balanceA_amount450": abs(float(bal_a.get("amount") or 0) - 450) < 0.01,
                "balanceA_avg10": abs(float(bal_a.get("avgCost") or 0) - 10) < 0.0001,
                "balanceA_locked0": float(bal_a.get("qtyLocked") or 0) == 0,
                "balanceB_qty20": float(bal_b.get("qtyOnhand") or 0) == 20,
                "balanceB_amount200": abs(float(bal_b.get("amount") or 0) - 200) < 0.01,
                "balanceB_locked0": float(bal_b.get("qtyLocked") or 0) == 0,
            }
            source_types = sorted({int(t["sourceType"]) for t in rows})
            checks["txnSources"] = source_types == [2, 4, 7, 8, 9]
            data["apiFinal"] = {"balanceA": bal_a, "balanceB": bal_b,
                                "sourceTypes": source_types, "txnCount": len(rows)}
            failed = [k for k, v in checks.items() if not v]
            if failed:
                raise RuntimeError(f"API 终态核对未通过：{failed}；sourceTypes={source_types}")
            return {"checks": checks, "sourceTypes": source_types, "txnCount": len(rows)}

        run_step("18_api_verify", step_api_verify)

        # ---------- 16. SQL 直连核对 ----------
        def step_sql():
            body = build_full_sql_body()
            sql = run_sql_script(DB_NAME, body)
            RESULT["sql"] = sql
            checks = {}
            balances = {row["wh_code"]: row for row in (sql.get("balances") or [])}
            bal_a = balances.get(data["warehouseCodeA"]) or {}
            bal_b = balances.get(data["warehouseCodeB"]) or {}
            checks["sql_balA_qty45"] = abs(float(bal_a.get("qty_onhand") or 0) - 45) < 0.01
            checks["sql_balA_amount450"] = abs(float(bal_a.get("amount") or 0) - 450) < 0.01
            checks["sql_balA_avg10"] = abs(float(bal_a.get("avg_cost") or 0) - 10) < 0.0001
            checks["sql_balA_locked0"] = abs(float(bal_a.get("qty_locked") or 0)) < 0.0001
            checks["sql_balB_qty20"] = abs(float(bal_b.get("qty_onhand") or 0) - 20) < 0.01
            checks["sql_balB_amount200"] = abs(float(bal_b.get("amount") or 0) - 200) < 0.01
            checks["sql_balB_locked0"] = abs(float(bal_b.get("qty_locked") or 0)) < 0.0001

            wh_a = data["warehouseCodeA"]
            wh_b = data["warehouseCodeB"]
            a_sources = sorted({int(t["source_type"]) for t in (sql.get("txns") or [])
                                if t.get("wh_code") == wh_a})
            b_sources = sorted({int(t["source_type"]) for t in (sql.get("txns") or [])
                                if t.get("wh_code") == wh_b})
            checks["sql_txnA_purchase2"] = 2 in a_sources
            checks["sql_txnA_sale7"] = 7 in a_sources
            checks["sql_txnA_transferOut8"] = 8 in a_sources
            checks["sql_txnA_loss9"] = 9 in a_sources
            checks["sql_txnB_transferIn4"] = b_sources == [4]

            groups = {int(g["state"]): int(g["cnt"]) for g in (sql.get("outboxGroups") or [])}
            checks["sql_outbox_allDispatched"] = set(groups.keys()) == {2}
            checks["sql_outbox_count>=5"] = int(groups.get(2, 0)) >= 5
            checks["sql_outbox_noError"] = all(not r.get("last_error") for r in (sql.get("outboxRows") or []))

            transfer = (sql.get("transfers") or [{}])[0]
            checks["sql_transfer_status4"] = int(transfer.get("status") or 0) == 4
            transit = (sql.get("transit") or [{}])[0]
            checks["sql_transit_closed2"] = int(transit.get("status") or 0) == 2
            checks["sql_transit_qty20"] = abs(float(transit.get("qty") or 0) - 20) < 0.01

            takes = {row["take_no"]: row for row in (sql.get("takes") or [])}
            checks["sql_take_initial3"] = int((takes.get(data["takeNo"]) or {}).get("status") or 0) == 3
            checks["sql_take_recheck5"] = int((takes.get(data["recheckTakeNo"]) or {}).get("status") or 0) == 5
            lines = sql.get("takeLines") or []
            # 复盘完成即解冻初盘：终态两单明细都不应残留 frozen 标记（冻结中状态已在 12/13 步 qty_locked 核对）
            initial_lines = [l for l in lines if l["take_no"] == data["takeNo"]]
            recheck_lines = [l for l in lines if l["take_no"] == data["recheckTakeNo"]]
            checks["sql_take_initialUnfrozen"] = bool(initial_lines) and all(
                not bool(l.get("frozen")) for l in initial_lines)
            checks["sql_take_recheckUnfrozen"] = bool(recheck_lines) and all(
                not bool(l.get("frozen")) for l in recheck_lines)

            adjust = (sql.get("adjusts") or [{}])[0]
            checks["sql_adjust_type2"] = int(adjust.get("type") or 0) == 2
            checks["sql_adjust_status3"] = int(adjust.get("status") or 0) == 3
            checks["sql_adjust_qty5"] = abs(float(adjust.get("total_amount") or 0) - 50) < 0.01

            alerts = sql.get("alerts") or []
            alert_hit = next((a for a in alerts if a.get("wh_code") == wh_a), None)
            checks["sql_alert_threshold100"] = alert_hit and abs(float(alert_hit.get("threshold") or 0) - 100) < 0.0001
            checks["sql_alert_actual45"] = alert_hit and abs(float(alert_hit.get("actual_value") or 0) - 45) < 0.01
            checks["sql_alert_handled2"] = alert_hit and int(alert_hit.get("status") or 0) == 2

            reqs = sql.get("approvalReqs") or []
            checks["sql_transferReqApplied"] = any(r.get("biz_type") == "wms.transfer"
                                                   and int(r.get("status") or 0) == 2 for r in reqs)
            checks["sql_adjustReqApplied"] = any(r.get("biz_type") == "wms.inventory.adjust"
                                                 and int(r.get("status") or 0) == 2 for r in reqs)

            RESULT["sqlChecks"] = checks
            failed = [k for k, v in checks.items() if not v]
            if failed:
                raise RuntimeError(f"SQL 核对未通过：{failed}")
            return {"checks": checks}

        run_step("19_sql_verify", step_sql)

        browser.close()


if __name__ == "__main__":
    try:
        main()
    finally:
        # 恢复临时系统参数（无论成功失败）
        try:
            if globals().get("PARAM_SAVED") and globals().get("IDENTITY"):
                api("DELETE", "/api/portal/system-parameters/wms.alert.safety.stock", token=IDENTITY)
                RESULT["paramRestored"] = True
        except Exception as exc:  # noqa: BLE001
            RESULT["paramRestored"] = False
            RESULT["paramRestoreError"] = str(exc)
        RESULT["finishedAt"] = datetime.now().isoformat(timespec="seconds")
        RESULT["ok"] = all(s.get("ok") for s in steps.values()) if steps else False
        RESULT["stepSummary"] = {k: bool(v.get("ok")) for k, v in steps.items()}
        with open(RESULT_PATH, "w", encoding="utf-8") as fh:
            json.dump(RESULT, fh, ensure_ascii=False, indent=2)
        print("RESULT", RESULT_PATH)
