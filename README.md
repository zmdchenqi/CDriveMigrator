# CDriveMigrator — C盘软件智能迁移工具

一键将 C 盘已安装的软件迁移到其他磁盘，**自动修复全部相关配置**，保证迁移后程序正常运行。

## 核心特性

| 功能 | 说明 |
|------|------|
| **智能扫描** | 自动从注册表发现 C 盘所有已安装程序 |
| **深度分析** | 扫描注册表、快捷方式、Windows服务、环境变量、计划任务、内部配置文件 |
| **全自动修复** | 迁移后自动修复所有引用路径，无需手动操作 |
| **Junction 兜底** | 在原路径创建目录联接，即使有遗漏引用也能正常运行 |
| **风险评估** | 智能评估每个程序的迁移风险等级 |
| **一键回滚** | 迁移失败自动回滚，保障数据安全 |

## 迁移流程

```
扫描 → 分析引用 → 停止进程/服务 → 移动文件 → 修复注册表 → 修复快捷方式
     → 修复服务路径 → 修复环境变量 → 修复计划任务 → 修复配置文件
     → 创建 Junction → 重启服务 → 验证
```

## 自动修复的引用类型

1. **注册表** — 深度扫描 HKLM/HKCU/HKCR，修复所有包含旧路径的值
2. **快捷方式** — 修复桌面、开始菜单、快速启动栏的 .lnk 文件
3. **Windows 服务** — 修复服务的 ImagePath
4. **环境变量** — 修复 PATH 及其他包含旧路径的系统/用户环境变量
5. **计划任务** — 导出/修改/重建包含旧路径的计划任务
6. **配置文件** — 修复程序目录内的 .ini/.cfg/.xml/.json/.yaml 等配置文件

## 环境要求

- Windows 10 / 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 管理员权限（必须）

## 快速启动

### 方式一：双击启动
```
右键 start.bat → 以管理员身份运行
```

### 方式二：命令行
```powershell
cd CDriveMigrator
dotnet run -c Release
```

### 方式三：编译后运行
```powershell
dotnet build -c Release
.\bin\Release\net8.0-windows\CDriveMigrator.exe
```

## 使用步骤

1. **扫描** — 点击「扫描 C 盘」，发现所有已安装程序
2. **分析** — 选中程序点击「分析此程序」或点击「分析全部」
3. **查看** — 在右侧面板查看引用详情和风险等级
4. **选择** — 勾选要迁移的程序，选择目标磁盘
5. **迁移** — 点击「开始迁移」，等待完成

## 安全机制

- **回滚快照** — 迁移前保存完整状态快照，失败自动恢复
- **Junction 兜底** — 原路径创建联接，NTFS 层面透明重定向
- **进程管理** — 自动停止相关进程和服务，迁移后重新启动
- **风险评估** — Critical 级别程序需要二次确认
- **环境广播** — 修复环境变量后自动广播 WM_SETTINGCHANGE

## 技术架构

- **语言**: C# / .NET 8.0
- **UI**: WPF (MVVM 架构)
- **文件迁移**: robocopy (可靠跨卷移动)
- **联接创建**: mklink /J (NTFS Junction Point)
- **注册表**: Microsoft.Win32 深度递归扫描
- **快捷方式**: COM Interop (WScript.Shell)
- **服务管理**: ServiceController + 注册表
- **计划任务**: schtasks.exe CLI

## 项目结构

```
CDriveMigrator/
├── Models/Models.cs              # 数据模型
├── Services/
│   ├── ProgramScanner.cs         # 程序扫描器
│   ├── ReferenceAnalyzer.cs      # 引用分析器 (6种类型)
│   ├── MigrationEngine.cs        # 迁移引擎 (修复+回滚)
│   └── JunctionManager.cs        # Junction 管理
├── ViewModels/MainViewModel.cs   # MVVM 视图模型
├── Views/MainWindow.xaml         # 暗色主题 UI
├── Helpers/RelayCommand.cs       # 命令辅助
├── Converters/Converters.cs      # 值转换器
├── App.xaml                      # 应用入口
└── app.manifest                  # UAC 管理员提权
```

## 注意事项

- 迁移前建议先创建系统还原点
- 首次使用建议从不重要的程序开始测试
- 标记为「危险」的程序（如系统组件）请谨慎迁移
- 迁移后请验证程序能否正常打开运行

## License

MIT
