# MultiRDPManager FreeRDP PoC - 交付总结

## TL;DR
基于 FreeRDP 3.26.0 的 WPF RDP 客户端 PoC 已完成。核心 API `freerdp_input_send_mouse_event()` 直接在 RDP 协议层注入输入，**不移动系统光标**，解决群控光标弹跳问题。

## 构建状态
✅ **Build 成功** — 0 error, 0 warning

## 项目结构
```
MultiRDPManager-FreeRDP/
├── lib/                           # FreeRDP 原生 DLL (8个)
│   ├── libfreerdp3.dll            # 核心库
│   ├── libfreerdp-client3.dll     # 客户端辅助
│   ├── libwinpr3.dll              # 运行时
│   ├── libwfreerdp-client3.dll    # Windows 客户端
│   ├── libcrypto-3-x64.dll        # OpenSSL
│   ├── libssl-3-x64.dll           # OpenSSL
│   ├── libgcc_s_seh-1.dll         # MinGW 运行时
│   └── libwinpthread-1.dll        # MinGW 线程
├── src/
│   └── MultiRDPManager.FreeRDP/
│       ├── Interop/
│       │   ├── NativeMethods.cs    # P/Invoke 声明
│       │   ├── FreeRdpTypes.cs     # 结构体/枚举
│       │   └── FreeRdpNative.cs    # 托管封装
│       ├── Services/
│       │   ├── RdpSession.cs       # 单会话管理
│       │   └── RdpConnectionManager.cs  # 多会话+群控
│       ├── Controls/
│       │   └── RdpCanvas.cs        # WPF 渲染控件
│       └── MainWindow.xaml/.cs     # 测试窗口
├── docs/
│   ├── system_design.md
│   ├── class-diagram.mermaid
│   └── sequence-diagram.mermaid
└── build/
    └── CopyLibs.targets
```

## 关键技术决策
1. **直接 P/Invoke** — 不需要 C Wrapper DLL，所有 FreeRDP API 直接从 C# 调用
2. **MSYS2 二进制** — 从 MSYS2 仓库获取预编译 DLL，跳过从源码编译
3. **GDI → WriteableBitmap** — FreeRDP GDI 渲染到内存 buffer → WPF 显示
4. **独立工作线程** — 每个 session 独立线程运行 FreeRDP 事件循环

## 下一步建议
1. **运行测试**：启动程序，输入服务器 IP/凭据，点击 Connect 验证远程桌面能否显示
2. **验证输入不跳**：在 RDP 预览区点击，观察本地光标是否移动（应不动）
3. **群控测试**：连接 2 台以上服务器，开启群控，验证广播输入时光标不跳
4. **集成到现有项目**：将 FreeRDP 方案合并到 MultiRDPManager-v2 替换 MSTSC
