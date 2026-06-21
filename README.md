# MultiRDPManager

> 多窗口 RDP 远程桌面管理器 — 基于 FreeRDP，支持批量连接、群控、缩略图监控

## 功能

- **多会话管理** — 同时连接多台 Windows 服务器，左侧列表管理，中间主预览区查看
- **批量导入** — 支持 CSV 格式（名称,IP,用户名,密码）一键导入多台服务器
- **群控模式** — 鼠标/键盘操作同步转发到所有从机，支持主控切换
- **缩略图面板** — 右侧缩略图实时监控所有已连接会话，支持搜索过滤
- **暗色主题** — 现代化暗色 UI，专为运维场景设计

## 技术栈

- **WPF** (.NET 8, win-x64)
- **FreeRDP** — [RoyalApps.Community.FreeRdp.WinForms](https://github.com/royalapplications/royalapps-community-freerdp) v2.0
- **Windows 全局钩子** — `WH_MOUSE_LL` / `WH_KEYBOARD_LL` 实现群控输入转发

## 快速开始

### 1. 下载运行

从 [Releases](../../releases) 下载 `MultiRDPManager.FreeRDP.exe`，双击即可运行（自包含单文件，无需安装 .NET）。

### 2. 自己编译

```bash
git clone https://github.com/duchernya/MultiRDPManager.git
cd MultiRDPManager/src/MultiRDPManager.FreeRDP
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 使用说明

1. 点击 **新建** 或 **导入** 添加服务器（支持 CSV 批量导入）
2. 选中服务器点击 **连接**（或全部连接），右侧自动出现缩略图
3. 在主预览区可直接操作远程桌面
4. 勾选多台服务器右侧的复选框 → 点击 **群控**，选择主控即可
5. 群控模式下，鼠标/键盘操作会同步到所有勾选的从机

## 截图

<div align="center">
  <img src="docs/screenshot.png" width="800" alt="主界面截图">
</div>

## License

MIT
