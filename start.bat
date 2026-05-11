@echo off
echo ============================================
echo   CDriveMigrator - C盘软件智能迁移工具
echo ============================================
echo.
echo 正在编译并启动...
echo.
dotnet run --project "%~dp0CDriveMigrator.csproj" -c Release
pause
