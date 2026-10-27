# -*- coding: utf-8 -*-
"""缺陷复测取证（仅验证与截图，不修改业务代码）。

覆盖三处已修复缺陷：
1. 供应商准入审批通过后状态转「合作中」（审批人 Id 混淆 + Outbox 消费者派发）
2. 数据字典子公司级对租户管理员可见
3. 人员新增「员工」类型下所属组织/岗位必填校验
"""
import json
import os
import time

from playwright.sync_api import sync_playwright

BASE = "http://localhost:5173"
OUT = r"e:\ERPv2\eprv3\docs\test-shots\bas-20261025"
REPORT = os.path.join(OUT, "_scripts", "retest-result.json")

failed_requests = []
page_errors = []
results = {"steps": {}}


def attach(page):
    page.on("pageerror", lambda e: page_errors.append({"error": str(e), "page": page.url}))
    page.on("response", lambda r: failed_requests.append(
        {"status": r.status, "url": r.url, "method": r.request.method}) if r.status >= 400 else None)


def shot(page, name):
    path = os.path.join(OUT, f"{name}.png")
    page.screenshot(path=path, full_page=True)
    results.setdefault("screenshots", []).append(os.path.basename(path))


def login(page):
    page.goto(f"{BASE}/login", wait_until="networkidle")
    page.get_by_placeholder("请输入登录账号").fill("qlxadmin")
    page.get_by_placeholder("请输入登录密码").fill("Qlx@2026")
    page.get_by_placeholder("填写企业名称或税号，可直接进入该企业").fill("91510100MA0000000X")
    page.locator("button[type=submit]").click()
    page.wait_for_timeout(2500)
    page.wait_for_load_state("networkidle")


with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1440, "height": 900}, locale="zh-CN")
    page = ctx.new_page()
    attach(page)

    login(page)
    results["login_url"] = page.url

    # ---------- 1) 供应商准入后状态 ----------
    try:
        page.goto(f"{BASE}/manage/master/suppliers", wait_until="networkidle")
        page.wait_for_timeout(1500)
        body = page.locator("body").inner_text()
        shot(page, "41-supplier-active-after-approval")
        row = page.locator("tr").filter(has_text="自动化验证供应商180626").first
        row_text = row.inner_text().replace("\n", " ") if row.count() > 0 else ""
        results["steps"]["supplier_active"] = {
            "row": row_text,
            "has_active": "合作中" in row_text,
            "has_pending": "待准入" in row_text,
            "body_has_supplier": "自动化验证供应商180626" in body,
        }
    except Exception as exc:  # noqa: BLE001
        results["steps"]["supplier_active"] = {"error": str(exc)}

    # ---------- 2) 字典子公司级可见 ----------
    try:
        page.goto(f"{BASE}/manage/master/dictionaries", wait_until="networkidle")
        page.wait_for_timeout(1500)
        body = page.locator("body").inner_text()
        shot(page, "42-dict-subsidiary-visible")
        results["steps"]["dict_visible"] = {
            "has_auto180626": "auto180626" in body or "自动化验证字典180626" in body,
            "has_qa1807": "qa1807" in body or "自动化验证字典1807" in body,
            "has_group_1807": "qagrp1807" in body or "自动化验证集团字典1807" in body,
        }
    except Exception as exc:  # noqa: BLE001
        results["steps"]["dict_visible"] = {"error": str(exc)}

    # ---------- 3) 人员员工类型必填校验 ----------
    try:
        page.goto(f"{BASE}/manage/master/persons", wait_until="networkidle")
        page.wait_for_timeout(1200)
        page.get_by_role("button", name="新增人员").first.click()
        dlg = page.locator(".el-dialog:visible").last
        dlg.wait_for(timeout=8000)
        dlg.locator(".el-form-item").filter(has_text="姓名").first.locator("input").first.fill("必填校验验证")
        dlg.get_by_role("button", name="保存人员").click()
        page.wait_for_timeout(1200)
        errors = [t.strip() for t in dlg.locator(".el-form-item__error").all_inner_texts() if t.strip()]
        shot(page, "43-person-employee-required")
        labels = [t.replace("\n", " ") for t in dlg.locator(".el-form-item").all_inner_texts()]
        org_pos_required = [l for l in labels if ("所属组织" in l or "岗位" in l) and "不能为空" in l]
        results["steps"]["person_required"] = {
            "errors": errors,
            "org_pos_errors": org_pos_required,
            "dialog_still_open": dlg.is_visible(),
        }
        page.keyboard.press("Escape")
    except Exception as exc:  # noqa: BLE001
        results["steps"]["person_required"] = {"error": str(exc)}

    # ---------- 4) 审批待办可见（修复后应为空态或历史已办） ----------
    try:
        page.goto(f"{BASE}/manage/approvals/todos", wait_until="networkidle")
        page.wait_for_timeout(1500)
        shot(page, "44-approval-todos-empty")
        results["steps"]["approval_todos"] = {"url": page.url}
    except Exception as exc:  # noqa: BLE001
        results["steps"]["approval_todos"] = {"error": str(exc)}

    results["failed_requests"] = failed_requests
    results["page_errors"] = page_errors
    browser.close()

with open(REPORT, "w", encoding="utf-8") as f:
    json.dump(results, f, ensure_ascii=False, indent=2)

print(json.dumps(results, ensure_ascii=False, indent=2))
