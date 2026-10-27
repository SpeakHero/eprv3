# -*- coding: utf-8 -*-
"""补充取证脚本：登录后横向滚动「出入库流水」表格，截取数量 / 单价 / 金额 / 结存列。

仅做截图取证，不创建或修改任何业务数据，也不修改业务代码。
输出：09-txns-quantity.png（与 smoke_2a.py 生成的 09-txns.png 互补）
"""
import os

from playwright.sync_api import sync_playwright

BASE = "http://localhost:5173"
OUT = r"e:\ERPv2\eprv3\docs\test-shots\stk-wms-2a-20261026"

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1920, "height": 900}, locale="zh-CN")
    page = ctx.new_page()
    page.goto(f"{BASE}/login", wait_until="networkidle")
    page.wait_for_timeout(800)
    page.get_by_placeholder("请输入登录账号").fill("qlxadmin")
    page.get_by_placeholder("请输入登录密码").fill("Qlx@2026")
    page.get_by_placeholder("填写企业名称或税号，可直接进入该企业").fill("91510100MA0000000X")
    page.locator("button[type=submit]").click()
    page.wait_for_timeout(3000)
    page.goto(f"{BASE}/manage/stk/txns", wait_until="networkidle")
    page.wait_for_timeout(2000)
    page.evaluate(
        """() => {
          document.querySelectorAll('.el-table__body-wrapper .el-scrollbar__wrap, .el-table__body-wrapper').forEach((el) => {
            el.scrollLeft = el.scrollWidth
          })
        }"""
    )
    page.wait_for_timeout(800)
    page.screenshot(path=os.path.join(OUT, "09-txns-quantity.png"), full_page=False)
    print("OK", os.path.join(OUT, "09-txns-quantity.png"))
    browser.close()
