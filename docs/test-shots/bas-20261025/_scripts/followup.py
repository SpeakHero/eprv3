# -*- coding: utf-8 -*-
"""补充验证：人员新增（补齐组织/岗位）+ 脱敏与明文查看 + 字典可见性取证 + 变更日志回查。

仅验证与截图，不修改业务代码。
"""
import json
import os
import re
import time
import traceback

from playwright.sync_api import sync_playwright

BASE = "http://localhost:5173"
OUT = r"e:\ERPv2\eprv3\docs\test-shots\bas-20261025"
REPORT = os.path.join(OUT, "_scripts", "followup-result.json")
TS = time.strftime("%H%M%S")

console_events = []
failed_requests = []
page_errors = []
results = {"steps": {}, "messages": [], "screenshots": []}


def attach(page):
    page.on("console", lambda m: console_events.append(
        {"type": m.type, "text": m.text, "page": page.url}) if m.type in ("error", "warning") else None)
    page.on("pageerror", lambda e: page_errors.append({"error": str(e), "page": page.url}))
    page.on("response", lambda r: failed_requests.append(
        {"status": r.status, "url": r.url, "method": r.request.method}) if r.status >= 400 else None)


def shot(page, name, full=True):
    path = os.path.join(OUT, f"{name}.png")
    page.screenshot(path=path, full_page=full)
    results["screenshots"].append(os.path.basename(path))
    return path


def wait_message(page, timeout=10000):
    try:
        page.wait_for_selector(".el-message", state="visible", timeout=timeout)
        texts = [t.strip().replace("\n", " ") for t in page.locator(".el-message").all_inner_texts()]
        if texts:
            results["messages"].extend(texts)
            return texts[-1]
    except Exception:
        pass
    return ""


def visible_dialog(page):
    page.wait_for_selector(".el-dialog:visible", timeout=10000)
    return page.locator(".el-dialog:visible").last


def form_item(dialog, label):
    return dialog.locator(".el-form-item").filter(has_text=label).first


def fill_field(dialog, label, value):
    item = form_item(dialog, label)
    item.locator("input, textarea").first.fill(value)


def pick_option(page, dialog, label, option_text):
    item = form_item(dialog, label)
    item.locator(".el-select").first.click()
    page.wait_for_selector(".el-popper:visible", timeout=5000)
    page.wait_for_timeout(400)
    dropdown = page.locator(".el-popper:visible").last
    item_el = dropdown.locator(".el-select-dropdown__item").filter(has_text=option_text).first
    item_el.click()
    page.wait_for_timeout(300)


def login(page):
    page.goto(f"{BASE}/login", wait_until="networkidle")
    page.get_by_placeholder("请输入登录账号").fill("qlxadmin")
    page.get_by_placeholder("请输入登录密码").fill("Qlx@2026")
    page.get_by_placeholder("填写企业名称或税号，可直接进入该企业").fill("91510100MA0000000X")
    page.locator("button[type=submit]").click()
    page.wait_for_timeout(2500)
    page.wait_for_load_state("networkidle")
    return page.url


with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1440, "height": 900}, locale="zh-CN")
    page = ctx.new_page()
    attach(page)

    try:
        results["login_url"] = login(page)
    except Exception as exc:  # noqa: BLE001
        results["login_url"] = f"FAILED: {exc}"

    # ---------- 1) 人员新增（补齐所属组织 + 岗位） ----------
    try:
        page.goto(f"{BASE}/manage/master/persons", wait_until="networkidle")
        page.wait_for_timeout(1200)
        page.get_by_role("button", name="新增人员").first.click()
        dlg = visible_dialog(page)
        shot(page, "34-person-form-filled")
        fill_field(dlg, "姓名", f"自动化员工{TS}")
        fill_field(dlg, "证件号码", "11010519491231002X")
        fill_field(dlg, "手机号", "13800138000")
        pick_option(page, dlg, "所属组织", "淇利馨测试总部")
        pick_option(page, dlg, "岗位", "后端工程师")
        shot(page, "35-person-form-org-pos")
        dlg.get_by_role("button", name="保存人员").click()
        msg = wait_message(page, 15000)
        page.wait_for_timeout(2000)
        page.wait_for_load_state("networkidle")
        body = page.locator("body").inner_text()
        shot(page, "36-person-created")
        masked_id = re.search(r"\d{4}\*+\w{2,}", body)
        masked_mobile = re.search(r"1\d{2}\*+\d{2,}", body)
        results["steps"]["person_create"] = {
            "ok": f"自动化员工{TS}" in body, "message": msg,
            "masked_id": masked_id.group(0) if masked_id else None,
            "masked_mobile": masked_mobile.group(0) if masked_mobile else None,
            "has_plaintext_button": "查看明文" in body,
            "has_leave_button": "离职" in body,
            "emp_code": (re.search(r"EMP-\d+", body).group(0) if re.search(r"EMP-\d+", body) else None),
        }
        print("STEP person_create", results["steps"]["person_create"])

        # 明文查看
        row = page.locator("tr").filter(has_text=f"自动化员工{TS}").first
        if row.count() > 0:
            row.get_by_role("button").filter(has_text="查看明文").first.click()
            page.wait_for_timeout(1500)
            pmsg = ""
            try:
                pmsg = wait_message(page, 6000)
            except Exception:
                pass
            shot(page, "37-person-plaintext")
            ptext = page.locator("body").inner_text()
            results["steps"]["person_plaintext"] = {
                "ok": "11010519491231002X" in ptext, "message": pmsg,
                "has_full_id": "11010519491231002X" in ptext,
            }
            print("STEP person_plaintext", results["steps"]["person_plaintext"])
            # 关闭明文弹窗/抽屉
            for sel in (".el-dialog:visible .el-dialog__headerbtn", ".el-drawer:visible .el-drawer__close-btn"):
                b = page.locator(sel).first
                if b.count() > 0:
                    b.click()
                    page.wait_for_timeout(500)
    except Exception as exc:  # noqa: BLE001
        results["steps"]["person_create"] = {"ok": False, "error": str(exc), "trace": traceback.format_exc(limit=3)}
        print("STEP_FAIL person_create", exc)

    # ---------- 2) 字典可见性取证 ----------
    try:
        page.goto(f"{BASE}/manage/master/dictionaries", wait_until="networkidle")
        page.wait_for_timeout(1500)
        body = page.locator("body").inner_text()
        shot(page, "38-dict-list")
        results["steps"]["dict_visibility"] = {
            "visible_group_type": "自动化验证集团字典1807" in body,
            "visible_sub_type": "自动化验证字典1807" in body,
            "group_type_rows": page.locator("tr").filter(has_text="自动化验证集团字典1807").count(),
            "sub_type_rows": page.locator("tr").filter(has_text="自动化验证字典1807").count(),
            "alert_readonly": "集团级字典对租户只读" in body,
        }
        print("STEP dict_visibility", results["steps"]["dict_visibility"])
        # 点选集团级类型，看项列表与只读提示
        grp_row = page.locator("tr").filter(has_text="自动化验证集团字典1807").first
        if grp_row.count() > 0:
            grp_row.click()
            page.wait_for_timeout(900)
            shot(page, "39-dict-group-selected")
            results["steps"]["dict_group_selected"] = {
                "has_readonly_alert": "集团级字典对租户只读" in page.locator("body").inner_text(),
                "has_item_button": page.get_by_role("button", name="新增字典项").count() > 0,
            }
            print("STEP dict_group_selected", results["steps"]["dict_group_selected"])
    except Exception as exc:  # noqa: BLE001
        results["steps"]["dict_visibility"] = {"ok": False, "error": str(exc)}
        print("STEP_FAIL dict_visibility", exc)

    # ---------- 3) 变更日志回查（人员新增后） ----------
    try:
        page.goto(f"{BASE}/manage/changelogs", wait_until="networkidle")
        page.wait_for_timeout(1800)
        shot(page, "40-changelog-after-person")
        body = page.locator("body").inner_text()
        rows = page.locator("tbody tr").count()
        results["steps"]["changelog"] = {
            "ok": rows > 0, "rows": rows, "has_empty": "暂无变更日志" in body,
            "mentions_person": "Person" in body or "人员" in body,
            "excerpt": body[:600],
        }
        print("STEP changelog", results["steps"]["changelog"]["rows"])
    except Exception as exc:  # noqa: BLE001
        results["steps"]["changelog"] = {"ok": False, "error": str(exc)}
        print("STEP_FAIL changelog", exc)

    browser.close()

results["console"] = console_events
results["page_errors"] = page_errors
results["failed_requests"] = failed_requests
with open(REPORT, "w", encoding="utf-8") as f:
    json.dump(results, f, ensure_ascii=False, indent=2)
print("WROTE", REPORT)
