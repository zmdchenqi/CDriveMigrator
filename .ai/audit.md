# 代码审计报告

## A001 — 注册表写入权限
- **类型**: 安全
- **严重程度**: 🟡中等
- **描述**: MigrationEngine 修改注册表时，部分键可能因权限不足无法写入
- **处理**: 已通过 try-catch 捕获并记录到 FailedFixes，Junction 兜底保证运行
- **状态**: 已处理

## A002 — 进程杀死风险
- **类型**: 逻辑
- **严重程度**: 🟡中等
- **描述**: KillRelatedProcesses 使用 entireProcessTree: true，可能影响子进程
- **处理**: 仅杀死 MainModule.FileName 匹配的进程，范围受控
- **状态**: 可接受
