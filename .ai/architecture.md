# 架构决策记录

## ADR-001: 选择 C# WPF 而非 Electron/Web
- **决策**: 使用 C# .NET 8 WPF
- **原因**: 需要深度 Windows API 访问（注册表、服务、COM、进程管理）。Web 方案无法直接操作系统级资源。
- **放弃**: Electron（太重）、Python（UI 体验差）、Rust（开发效率低）

## ADR-002: 使用 Junction 而非 Symbolic Link
- **决策**: 使用 NTFS Junction Point (mklink /J)
- **原因**: Junction 对应用程序完全透明，无需额外权限（symlink 需要开发者模式）。作为修复遗漏的安全兜底。

## ADR-003: robocopy 移动文件
- **决策**: 使用 robocopy /E /MOVE 替代手写文件复制
- **原因**: 内置重试、处理锁定文件、跨卷可靠移动、自动清理源目录。退出码 0-7 均为成功。

## ADR-004: 注册表扫描策略
- **决策**: 分层扫描（已知位置 → 深度搜索）
- **原因**: 全量扫描太慢。先扫描 Uninstall/AppPaths/Services/CLSID 等已知位置，再限深度递归 SOFTWARE。跳过 Installer/SideBySide 等大且不相关的子树。

## ADR-005: 迁移引擎设计
- **决策**: 快照→修复→Junction→回滚 四步安全模型
- **原因**: 任何步骤失败都能回滚。Junction 作为最终保障，即使部分引用修复失败程序也能正常运行。
