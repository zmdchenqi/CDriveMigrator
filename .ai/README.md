# CDriveMigrator

C盘软件智能迁移工具 — 扫描已安装程序，分析全部引用，一键迁移到其他磁盘并自动修复所有配置。

## 技术栈
- C# / .NET 8.0 / WPF (MVVM)
- robocopy, mklink /J, COM Interop, schtasks
- NuGet: System.ServiceProcess.ServiceController

## 快速启动
```
dotnet run -c Release
```

## 目录结构
- `Models/` — 数据模型
- `Services/` — 核心服务（扫描、分析、迁移、Junction）
- `ViewModels/` — MVVM 逻辑
- `Views/` — WPF 界面
- `Helpers/` — 命令辅助类
- `Converters/` — 值转换器
