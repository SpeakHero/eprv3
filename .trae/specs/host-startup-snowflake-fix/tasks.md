# Tasks：host-startup-snowflake-fix

- [ ] T1 读 `.trae/rules/global-csharp-standard.md`，按规范落地 Program.cs 租约预初始化块
- [ ] T2 WorkerIdLeaseHostedService.StartAsync 幂等短路 + 注释/文件头更新
- [ ] T3 新增 WorkerIdLeaseHostedService 单测（幂等 + 正常获取两用例）
- [ ] T4 `dotnet build` + `dotnet test` 全量回归
- [ ] T5 后台启动 Qlx.Host，确认迁移/Manifest 同步/就绪探测
- [ ] T6 执行冒烟矩阵（登录 → 角色 CRUD/409 → 权限三端点 → 成员双向端点），记录响应
- [ ] T7 汇总冒烟报告（通过项 / 问题项 / ID 形态观察项），文字汇报不建新文件
