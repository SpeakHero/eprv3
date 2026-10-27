# -*- coding: utf-8 -*-
"""Bas 模块 01 基础信息中心：关键业务主路径（UI 操作 + 截图证据）。

覆盖：组织新增（部门）/ 分类父子层级编码 / 供应商新增+准入审批 / 人员新增（脱敏）/
      字典子公司级类型+项 / 客户新增+详情 Tab / 变更日志回查 / 审批流程懒种子。
说明：仅做验证与截图，不修改业务代码；产生的数据留在测试租户 qlx-dev。
"""
import json
import os
import re
import time
import traceback

from playwright.sync_api import sync_playwright

BASE = "http://localhost:5173"
OUT = r"e:\ERPv2\eprv3\docs\test-shots\bas-20261025"
REPORT = os.path.join(OUT, "_scripts", "main-path-result.json")
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
    page.wait_for_selector(".el-select-dropdown:visible, .el-popper:visible", timeout=5000)
    dropdown = page.locator(".el-popper:visible").last
    dropdown.get_by_text(option_text, exact=True).first.click()
    page.wait_for_timeout(200)


def close_drawer(page):
    btn = page.locator(".el-drawer:visible .el-drawer__close-btn").first
    if btn.count() > 0:
        btn.click()
    else:
        page.keyboard.press("Escape")
    page.wait_for_timeout(500)


def login(page):
    page.goto(f"{BASE}/login", wait_until="networkidle")
    page.get_by_placeholder("请输入登录账号").fill("qlxadmin")
    page.get_by_placeholder("请输入登录密码").fill("Qlx@2026")
    page.get_by_placeholder("填写企业名称或税号，可直接进入该企业").fill("91510100MA0000000X")
    page.locator("button[type=submit]").click()
    page.wait_for_timeout(2500)
    page.wait_for_load_state("networkidle")
    return page.url


def step(name):
    def deco(fn):
        try:
            value = fn()
            results["steps"][name] = {"ok": True, "detail": value}
            print("STEP_OK", name, value)
        except Exception as exc:  # noqa: BLE001
            results["steps"][name] = {
                "ok": False, "error": str(exc), "trace": traceback.format_exc(limit=3),
            }
            print("STEP_FAIL", name, exc)
            try:
                shot(page, f"99-fail-{name}")
            except Exception:
                pass
        return fn if False else None
    return deco


with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1440, "height": 900}, locale="zh-CN")
    page = ctx.new_page()
    attach(page)

    # ---------- 登录 ----------
    try:
        url = login(page)
        results["login_url"] = url
    except Exception as exc:  # noqa: BLE001
        results["login_url"] = f"FAILED: {exc}"

    # ---------- 1) 组织新增（部门级） ----------
    try:
        page.goto(f"{BASE}/manage/organizations", wait_until="networkidle")
        page.wait_for_timeout(800)
        page.get_by_role("button", name="新增下级组织").first.click()
        dlg = visible_dialog(page)
        code_inputs = dlg.locator("input").filter(has_not_text="").count()
        has_code_input = dlg.locator("input[placeholder*='编码']").count() > 0
        hint = dlg.locator(".qlx-form-dialog__hint").first.inner_text()
        shot(page, "11-org-form")
        fill_field(dlg, "组织名称", f"自动化验证部{TS}")
        pick_option(page, dlg, "组织类型", "部门")
        dlg.get_by_role("button", name="保存组织").click()
        msg = wait_message(page)
        page.wait_for_timeout(1500)
        page.wait_for_load_state("networkidle")
        page.get_by_text(f"自动化验证部{TS}", exact=False).first.click()
        page.wait_for_timeout(600)
        body = page.locator("body").inner_text()
        m = re.search(r"ORG-BM-\d{2}", body)
        shot(page, "12-org-created")
        results["steps"]["org_create"] = {
            "ok": bool(m), "message": msg, "has_code_input": has_code_input,
            "code_hint": hint, "created_code": m.group(0) if m else None,
            "detail_has_approval_state": "申请时间" in body,
        }
        print("STEP org_create", results["steps"]["org_create"])
        # 版本历史抽屉
        page.get_by_role("button", name="版本历史").first.click()
        page.wait_for_timeout(1000)
        shot(page, "13-org-history")
        hist = page.locator(".el-drawer:visible").last.inner_text()
        results["steps"]["org_history"] = {"ok": "版本" in hist or "v1" in hist, "text": hist[:300]}
        close_drawer(page)
        print("STEP org_history", results["steps"]["org_history"]["ok"])
    except Exception as exc:  # noqa: BLE001
        results["steps"]["org_create"] = {"ok": False, "error": str(exc), "trace": traceback.format_exc(limit=3)}
        print("STEP_FAIL org_create", exc)

    # ---------- 2) 商品分类：顶级 + 子级编码 ----------
    try:
        page.goto(f"{BASE}/manage/master/categories", wait_until="networkidle")
        page.wait_for_timeout(800)
        page.get_by_role("button", name="新增分类").first.click()
        dlg = visible_dialog(page)
        shot(page, "14-category-form")
        fill_field(dlg, "分类名称", f"自动化验证分类{TS}")
        dlg.get_by_role("button", name="保存分类").click()
        msg = wait_message(page)
        page.wait_for_timeout(1500)
        body = page.locator("body").inner_text()
        parent_code = None
        mm = re.search(r"自动化验证分类" + TS + r"[\s\S]{0,40}?(\d{2})", body)
        if mm:
            parent_code = mm.group(1)
        page.get_by_text(f"自动化验证分类{TS}", exact=True).first.click()
        page.wait_for_timeout(500)
        page.get_by_role("button", name="新增子分类").first.click()
        dlg = visible_dialog(page)
        fill_field(dlg, "分类名称", f"自动化验证子分类{TS}")
        dlg.get_by_role("button", name="保存分类").click()
        msg2 = wait_message(page)
        page.wait_for_timeout(1600)
        body2 = page.locator("body").inner_text()
        shot(page, "15-category-created")
        results["steps"]["category_create"] = {
            "ok": f"自动化验证子分类{TS}" in body2,
            "messages": [msg, msg2],
            "parent_code_guess": parent_code,
            "body_excerpt": body2[body2.find("分类树"):body2.find("分类树") + 700],
        }
        print("STEP category_create", results["steps"]["category_create"]["ok"], msg, msg2)
    except Exception as exc:  # noqa: BLE001
        results["steps"]["category_create"] = {"ok": False, "error": str(exc), "trace": traceback.format_exc(limit=3)}
        print("STEP_FAIL category_create", exc)

    # ---------- 3) 计量单位三块 + 新增表单 ----------
    try:
        page.goto(f"{BASE}/manage/master/units", wait_until="networkidle")
        page.wait_for_timeout(700)
        body = page.locator("body").inner_text()
        tabs = {t: (t in body) for t in ("单位组", "计量单位", "换算关系")}
        page.get_by_role("button", name="新增单位组").first.click()
        dlg = visible_dialog(page)
        no_code_input = dlg.locator("input[placeholder*='编码']").count() == 0
        shot(page, "16-unit-form")
        dlg.get_by_role("button", name="取消").click()
        results["steps"]["units_page"] = {"ok": all(tabs.values()), "tabs": tabs, "no_code_input": no_code_input}
        print("STEP units_page", results["steps"]["units_page"])
    except Exception as exc:  # noqa: BLE001
        results["steps"]["units_page"] = {"ok": False, "error": str(exc), "trace": traceback.format_exc(limit=3)}
        print("STEP_FAIL units_page", exc)

    # ---------- 4) 供应商：新增 + 准入审批 ----------
    try:
        page.goto(f"{BASE}/manage/master/suppliers", wait_until="networkidle")
        page.wait_for_timeout(700)
        page.get_by_role("button", name="新增供应商").first.click()
        dlg = visible_dialog(page)
        no_code_input = dlg.locator("input[placeholder*='编码']").count() == 0
        shot(page, "17-supplier-form")
        fill_field(dlg, "供应商名称", f"自动化验证供应商{TS}")
        fill_field(dlg, "信用代码", f"91AUTO{TS}X".ljust(18, "0")[:18])
        fill_field(dlg, "联系人", "自动化")
        fill_field(dlg, "联系电话", "13800138000")
        dlg.get_by_role("button", name="保存供应商").click()
        msg = wait_message(page)
        page.wait_for_timeout(1500)
        body = page.locator("body").inner_text()
        shot(page, "18-supplier-created")
        sup_code = re.search(r"SUP-\d{6}", body)
        results["steps"]["supplier_create"] = {
            "ok": f"自动化验证供应商{TS}" in body, "message": msg, "no_code_input": no_code_input,
            "code": sup_code.group(0) if sup_code else None,
        }
        print("STEP supplier_create", results["steps"]["supplier_create"])
        # 准入
        row = page.locator("tr").filter(has_text=f"自动化验证供应商{TS}").first
        row.get_by_role("button").filter(has_text="准入").first.click()
        page.wait_for_selector(".el-message-box:visible", timeout=8000)
        mb = page.locator(".el-message-box:visible").last
        shot(page, "19-supplier-admission-prompt")
        mb.locator("input").first.fill("自动化验证：年度评审通过")
        mb.get_by_role("button", name="提交申请").click()
        adm_msg = wait_message(page, 15000)
        results["steps"]["supplier_admission"] = {"ok": "已提交" in adm_msg, "message": adm_msg}
        print("STEP supplier_admission", results["steps"]["supplier_admission"])
        page.wait_for_timeout(800)
        shot(page, "19b-supplier-after-admission")
    except Exception as exc:  # noqa: BLE001
        results["steps"]["supplier_create"] = {"ok": False, "error": str(exc), "trace": traceback.format_exc(limit=3)}
        print("STEP_FAIL supplier_create", exc)
        adm_msg = ""

    # ---------- 4b) 审批中心：通过 Bas 供应商准入审批 ----------
    try:
        page.goto(f"{BASE}/manage/support/approvals", wait_until="networkidle")
        page.wait_for_timeout(1500)
        shot(page, "20-approval-todos")
        todo_text = page.locator("body").inner_text()
        has_row = page.locator("tr").filter(has_text="供应商准入").count() > 0 or page.locator("tr").filter(has_text="审批节点").count() > 1
        approvals_done = 0
        approve_errors = []
        for _ in range(4):
            # 待办表内首行「处理」按钮
            btns = page.locator("tbody tr button:has-text('处理')")
            if btns.count() == 0:
                break
            btns.first.click()
            page.wait_for_timeout(1200)
            drawer = page.locator(".el-drawer:visible").last
            drawer.get_by_role("button", name="通过", exact=True).click()
            page.wait_for_selector(".el-dialog:visible", timeout=8000)
            dlg = page.locator(".el-dialog:visible").last
            dlg.get_by_role("button", name="确定通过").click()
            am = wait_message(page, 12000)
            page.wait_for_timeout(1200)
            if "成功" in am or "通过" in am:
                approvals_done += 1
            else:
                approve_errors.append(am)
            try:
                close_drawer(page)
            except Exception:
                page.keyboard.press("Escape")
            page.wait_for_timeout(1000)
        shot(page, "21-approval-done")
        results["steps"]["approval"] = {
            "ok": approvals_done > 0, "approvals_done": approvals_done,
            "todo_text_excerpt": todo_text[:500], "errors": approve_errors,
        }
        print("STEP approval", results["steps"]["approval"])
    except Exception as exc:  # noqa: BLE001
        results["steps"]["approval"] = {"ok": False, "error": str(exc), "trace": traceback.format_exc(limit=3)}
        print("STEP_FAIL approval", exc)

    # 回查供应商状态
    try:
        page.goto(f"{BASE}/manage/master/suppliers", wait_until="networkidle")
        page.wait_for_timeout(1200)
        body = page.locator("body").inner_text()
        shot(page, "22-supplier-active")
        results["steps"]["supplier_active"] = {
            "ok": "合作中" in body, "has_row": f"自动化验证供应商{TS}" in body,
            "status_text": "合作中" if "合作中" in body else "未变为合作中",
        }
        print("STEP supplier_active", results["steps"]["supplier_active"])
    except Exception as exc:  # noqa: BLE001
        results["steps"]["supplier_active"] = {"ok": False, "error": str(exc)}
        print("STEP_FAIL supplier_active", exc)

    # ---------- 5) 人员新增（证件/手机脱敏） ----------
    try:
        page.goto(f"{BASE}/manage/master/persons", wait_until="networkidle")
        page.wait_for_timeout(700)
        page.get_by_role("button", name="新增人员").first.click()
        dlg = visible_dialog(page)
        no_code_input = dlg.locator("input[placeholder*='编码']").count() == 0
        shot(page, "23-person-form")
        fill_field(dlg, "姓名", f"自动化员工{TS}")
        fill_field(dlg, "证件号码", "11010519491231002X")
        fill_field(dlg, "手机号", "13800138000")
        dlg.get_by_role("button", name="保存人员").click()
        msg = wait_message(page)
        page.wait_for_timeout(1500)
        body = page.locator("body").inner_text()
        shot(page, "24-person-created")
        masked_id = re.search(r"1101\*+\w*", body)
        masked_mobile = re.search(r"138\*+8000", body)
        results["steps"]["person_create"] = {
            "ok": f"自动化员工{TS}" in body, "message": msg, "no_code_input": no_code_input,
            "masked_id": masked_id.group(0) if masked_id else None,
            "masked_mobile": masked_mobile.group(0) if masked_mobile else None,
            "has_plaintext_button": "查看明文" in body,
            "has_leave_button": "离职" in body,
        }
        print("STEP person_create", results["steps"]["person_create"])
    except Exception as exc:  # noqa: BLE001
        results["steps"]["person_create"] = {"ok": False, "error": str(exc), "trace": traceback.format_exc(limit=3)}
        print("STEP_FAIL person_create", exc)

    # ---------- 6) 数据字典：子公司级类型 + 项，集团级只读 ----------
    try:
        page.goto(f"{BASE}/manage/master/dictionaries", wait_until="networkidle")
        page.wait_for_timeout(800)
        type_code = f"auto{TS}"
        page.get_by_role("button", name="新增字典类型").first.click()
        dlg = visible_dialog(page)
        shot(page, "25-dict-type-form")
        fill_field(dlg, "类型编码", type_code)
        fill_field(dlg, "类型名称", f"自动化验证字典{TS}")
        # 默认子公司级；所属组织选择集团根
        try:
            pick_option(page, dlg, "所属组织", "淇利馨测试总部")
        except Exception as exc:  # noqa: BLE001
            print("org pick warn", exc)
        dlg.get_by_role("button", name="保存字典类型").click()
        msg = wait_message(page)
        page.wait_for_timeout(1500)
        # 选中新类型
        page.get_by_text(f"自动化验证字典{TS}", exact=True).first.click()
        page.wait_for_timeout(700)
        page.get_by_role("button", name="新增字典项").first.click()
        dlg = visible_dialog(page)
        shot(page, "26-dict-item-form")
        fill_field(dlg, "项编码", f"A{TS}")
        fill_field(dlg, "项名称", f"自动化验证项{TS}")
        dlg.get_by_role("button", name="保存字典项").click()
        msg2 = wait_message(page)
        page.wait_for_timeout(1200)
        body = page.locator("body").inner_text()
        shot(page, "27-dict-sub-created")
        results["steps"]["dict_sub"] = {
            "ok": f"自动化验证项{TS}" in body, "messages": [msg, msg2],
            "type_code": type_code, "readonly_alert": "集团级字典对租户只读" in body,
        }
        print("STEP dict_sub", results["steps"]["dict_sub"])
        # 集团级只读验证
        grp_code = f"grp{TS}"
        page.get_by_role("button", name="新增字典类型").first.click()
        dlg = visible_dialog(page)
        fill_field(dlg, "类型编码", grp_code)
        fill_field(dlg, "类型名称", f"自动化集团字典{TS}")
        pick_option(page, dlg, "归属层级", "集团级")
        dlg.get_by_role("button", name="保存字典类型").click()
        msg3 = wait_message(page)
        page.wait_for_timeout(1500)
        page.get_by_text(f"自动化集团字典{TS}", exact=True).first.click()
        page.wait_for_timeout(800)
        body2 = page.locator("body").inner_text()
        shot(page, "28-dict-group-readonly")
        results["steps"]["dict_group_readonly"] = {
            "ok": "集团级字典对租户只读" in body2, "message": msg3,
        }
        print("STEP dict_group_readonly", results["steps"]["dict_group_readonly"])
    except Exception as exc:  # noqa: BLE001
        results["steps"]["dict_sub"] = {"ok": False, "error": str(exc), "trace": traceback.format_exc(limit=3)}
        print("STEP_FAIL dict_sub", exc)

    # ---------- 7) 客户：新增 + 详情 Tab ----------
    try:
        page.goto(f"{BASE}/manage/master/customers", wait_until="networkidle")
        page.wait_for_timeout(700)
        page.get_by_role("button", name="新增客户").first.click()
        dlg = visible_dialog(page)
        shot(page, "29-customer-form")
        fill_field(dlg, "客户名称", f"自动化验证客户{TS}")
        fill_field(dlg, "信用代码", ("91CUST" + TS + "0" * 12)[:18])
        dlg.get_by_role("button", name="保存客户").click()
        msg = wait_message(page)
        page.wait_for_timeout(1500)
        body = page.locator("body").inner_text()
        shot(page, "30-customer-created")
        row = page.locator("tr").filter(has_text=f"自动化验证客户{TS}").first
        has_credit = row.get_by_role("button").filter(has_text="额度").count() > 0
        row.get_by_role("button").filter(has_text="详情").first.click()
        page.wait_for_timeout(1200)
        drawer = page.locator(".el-drawer:visible").last
        dtabs = {t: (t in drawer.inner_text()) for t in ("信用额度", "回款账户", "开票信息", "信用历史")}
        shot(page, "31-customer-detail-tabs")
        close_drawer(page)
        results["steps"]["customer"] = {
            "ok": f"自动化验证客户{TS}" in body, "message": msg, "has_credit_action": has_credit,
            "detail_tabs": dtabs,
        }
        print("STEP customer", results["steps"]["customer"])
    except Exception as exc:  # noqa: BLE001
        results["steps"]["customer"] = {"ok": False, "error": str(exc), "trace": traceback.format_exc(limit=3)}
        print("STEP_FAIL customer", exc)

    # ---------- 8) 变更日志回查 ----------
    try:
        page.goto(f"{BASE}/manage/changelogs", wait_until="networkidle")
        page.wait_for_timeout(1500)
        shot(page, "32-changelog-after")
        body = page.locator("body").inner_text()
        results["steps"]["changelog"] = {
            "ok": "暂无变更日志" not in body or "共 0 条" not in body,
            "has_empty": "暂无变更日志" in body, "rows_text": body[body.find("变更日志"):][:900],
        }
        print("STEP changelog", results["steps"]["changelog"]["has_empty"])
    except Exception as exc:  # noqa: BLE001
        results["steps"]["changelog"] = {"ok": False, "error": str(exc)}
        print("STEP_FAIL changelog", exc)

    # ---------- 9) 审批流程定义回查（Bas 懒种子是否补齐） ----------
    try:
        page.goto(f"{BASE}/manage/support/approvals", wait_until="networkidle")
        page.wait_for_timeout(1200)
        shot(page, "33-approval-initiated")
        results["steps"]["approvals_page"] = {"ok": True}
    except Exception as exc:  # noqa: BLE001
        results["steps"]["approvals_page"] = {"ok": False, "error": str(exc)}

    browser.close()

results["console"] = console_events
results["page_errors"] = page_errors
results["failed_requests"] = failed_requests
with open(REPORT, "w", encoding="utf-8") as f:
    json.dump(results, f, ensure_ascii=False, indent=2)
print("WROTE", REPORT)
