# -*- coding: utf-8 -*-
"""Bas 模块 01 基础信息中心：登录 + 9 页逐页截图与文本采集（只读验证）。"""
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

BASE = "http://localhost:5173"
OUT = r"e:\ERPv2\eprv3\docs\test-shots\bas-20261025"
REPORT = os.path.join(OUT, "_scripts", "recon-result.json")

PAGES = [
    ("02-organizations", "/manage/organizations"),
    ("03-categories", "/manage/master/categories"),
    ("04-units", "/manage/master/units"),
    ("05-suppliers", "/manage/master/suppliers"),
    ("06-customers", "/manage/master/customers"),
    ("07-persons", "/manage/master/persons"),
    ("08-dictionaries", "/manage/master/dictionaries"),
    ("09-code-rules", "/manage/master/code-rules"),
    ("10-changelogs", "/manage/changelogs"),
]

console_events = []
failed_requests = []
page_errors = []


def attach(page):
    def on_console(msg):
        if msg.type in ("error", "warning"):
            console_events.append({"type": msg.type, "text": msg.text, "page": page.url})

    page.on("console", on_console)
    page.on("pageerror", lambda err: page_errors.append({"error": str(err), "page": page.url}))
    page.on("response", lambda resp: failed_requests.append({
        "status": resp.status, "url": resp.url, "method": resp.request.method,
    }) if resp.status >= 400 else None)


def shot(page, name):
    path = os.path.join(OUT, f"{name}.png")
    page.screenshot(path=path, full_page=True)
    return path


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        ctx = browser.new_context(viewport={"width": 1440, "height": 900}, locale="zh-CN")
        page = ctx.new_page()
        attach(page)

        page.goto(f"{BASE}/login", wait_until="networkidle")
        shot(page, "00-login")

        page.get_by_placeholder("请输入登录账号").fill("qlxadmin")
        page.get_by_placeholder("请输入登录密码").fill("Qlx@2026")
        page.get_by_placeholder("填写企业名称或税号，可直接进入该企业").fill("91510100MA0000000X")
        page.locator("button[type=submit]").click()
        page.wait_for_timeout(2500)
        page.wait_for_load_state("networkidle")
        print("AFTER_LOGIN_URL", page.url)
        shot(page, "01-workbench")

        info = {"login_url": page.url, "pages": {}}
        if "/select-tenant" in page.url:
            info["select_tenant_text"] = page.locator("body").inner_text()
            # 点击租户卡片 / 进入企业
            candidates = page.get_by_text("淇利馨 ERP 开发租户")
            if candidates.count() > 0:
                candidates.first.click()
                page.wait_for_timeout(2000)
                page.wait_for_load_state("networkidle")
                print("AFTER_SELECT_URL", page.url)

        info["workbench_text"] = page.locator("body").inner_text()

        for name, route in PAGES:
            page.goto(f"{BASE}{route}", wait_until="networkidle")
            page.wait_for_timeout(1200)
            page.wait_for_load_state("networkidle")
            shot(page, name)
            text = page.locator("body").inner_text()
            info["pages"][name] = {"url": page.url, "text": text}
            print("PAGE", name, page.url, "len", len(text))

        browser.close()

    info["console"] = console_events
    info["page_errors"] = page_errors
    info["failed_requests"] = failed_requests
    with open(REPORT, "w", encoding="utf-8") as f:
        json.dump(info, f, ensure_ascii=False, indent=2)
    print("WROTE", REPORT)


if __name__ == "__main__":
    sys.exit(main())
