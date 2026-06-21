using System.Runtime.InteropServices;

namespace MultiRDPManager.FreeRDP.Native;

/// <summary>
/// Win32 API 封装 — 用于截取 wfreerdp.exe 子窗口内容实现缩略图预览
/// </summary>
internal static class Win32
{
    /// <summary>
    /// 捕获指定窗口内容到HDC（即使窗口被遮挡也能正常工作）
    /// </summary>
    /// <param name="hwnd">目标窗口句柄</param>
    /// <param name="hdcBlt">目标设备上下文</param>
    /// <param name="nFlags">标志位（0=完整窗口, 1=仅客户区）</param>
    /// <returns>是否成功</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    /// <summary>
    /// 在指定父窗口中查找符合条件的子窗口
    /// </summary>
    /// <param name="hwndParent">父窗口句柄</param>
    /// <param name="hwndChildAfter">从哪个子窗口之后开始查找（IntPtr.Zero表示从头开始）</param>
    /// <param name="lpszClass">窗口类名（null表示不限制）</param>
    /// <param name="lpszWindow">窗口标题（null表示不限制）</param>
    /// <returns>找到的子窗口句柄，未找到则返回IntPtr.Zero</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    /// <summary>
    /// 枚举指定父窗口的所有子窗口
    /// </summary>
    /// <param name="hwndParent">父窗口句柄</param>
    /// <param name="lpEnumFunc">回调函数</param>
    /// <param name="lParam">用户自定义参数</param>
    /// <returns>是否成功</returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// 获取窗口类名
    /// </summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    /// <summary>
    /// 获取窗口的屏幕坐标矩形
    /// </summary>
    /// <param name="hwnd">窗口句柄</param>
    /// <param name="lpRect">输出矩形</param>
    /// <returns>是否成功</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    /// <summary>
    /// 获取窗口客户区矩形（相对于客户区左上角）
    /// </summary>
    /// <param name="hwnd">窗口句柄</param>
    /// <param name="lpRect">输出矩形</param>
    /// <returns>是否成功</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hwnd, out RECT lpRect);

    /// <summary>
    /// 枚举子窗口回调委托
    /// </summary>
    /// <param name="hwnd">找到的子窗口句柄</param>
    /// <param name="lParam">用户自定义参数</param>
    /// <returns>true表示继续枚举，false表示停止</returns>
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    /// <summary>
    /// 矩形结构（与Win32 RECT布局一致）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        /// <summary>左边界（屏幕坐标或客户区坐标）</summary>
        public int Left;

        /// <summary>上边界</summary>
        public int Top;

        /// <summary>右边界</summary>
        public int Right;

        /// <summary>下边界</summary>
        public int Bottom;

        /// <summary>宽度</summary>
        public int Width => Right - Left;

        /// <summary>高度</summary>
        public int Height => Bottom - Top;
    }

    // ──────────────────────────────────────────────
    // 以下为群控输入转发所需 API
    // ──────────────────────────────────────────────

    #region ─── 窗口消息常量 ───

    /// <summary>键盘按下</summary>
    public const uint WM_KEYDOWN = 0x0100;
    /// <summary>键盘弹起</summary>
    public const uint WM_KEYUP = 0x0101;
    /// <summary>系统键按下（如Alt）</summary>
    public const uint WM_SYSKEYDOWN = 0x0104;
    /// <summary>系统键弹起</summary>
    public const uint WM_SYSKEYUP = 0x0105;
    /// <summary>鼠标左键按下</summary>
    public const uint WM_LBUTTONDOWN = 0x0201;
    /// <summary>鼠标左键弹起</summary>
    public const uint WM_LBUTTONUP = 0x0202;
    /// <summary>鼠标左键双击</summary>
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    /// <summary>鼠标右键按下</summary>
    public const uint WM_RBUTTONDOWN = 0x0204;
    /// <summary>鼠标右键弹起</summary>
    public const uint WM_RBUTTONUP = 0x0205;
    /// <summary>鼠标右键双击</summary>
    public const uint WM_RBUTTONDBLCLK = 0x0206;
    /// <summary>鼠标中键按下</summary>
    public const uint WM_MBUTTONDOWN = 0x0207;
    /// <summary>鼠标中键弹起</summary>
    public const uint WM_MBUTTONUP = 0x0208;
    /// <summary>鼠标中键双击</summary>
    public const uint WM_MBUTTONDBLCLK = 0x0209;
    /// <summary>鼠标移动</summary>
    public const uint WM_MOUSEMOVE = 0x0200;
    /// <summary>鼠标滚轮</summary>
    public const uint WM_MOUSEWHEEL = 0x020A;
    /// <summary>窗口大小变化</summary>
    public const uint WM_SIZE = 0x0005;

    /// <summary>鼠标按键状态：左键</summary>
    public const int MK_LBUTTON = 0x0001;
    /// <summary>鼠标按键状态：右键</summary>
    public const int MK_RBUTTON = 0x0002;
    /// <summary>鼠标按键状态：中键</summary>
    public const int MK_MBUTTON = 0x0010;

    #endregion

    #region ─── PostMessage / SendMessage / ScreenToClient ───

    /// <summary>
    /// 异步发送消息到指定窗口（不等待处理，立即返回）
    /// </summary>
    /// <param name="hWnd">目标窗口句柄</param>
    /// <param name="Msg">消息ID</param>
    /// <param name="wParam">附加参数wParam</param>
    /// <param name="lParam">附加参数lParam</param>
    /// <returns>如果消息已投递返回true</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    #region ─── 窗口扩展样式（WS_EX_*） ───

    public const int GWL_EXSTYLE = -20;
    public const uint WS_EX_COMPOSITED = 0x02000000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    #endregion

    /// <summary>
    /// 同步发送消息到指定窗口（等待处理完毕）
    /// 适用于需要确保消息已被目标窗口处理后再继续的场景
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// 带超时的同步消息发送 — 用于安全地从后台线程跨进程投递消息
    /// 使用 SMTO_ABORTIFHUNG 防止目标窗口卡死时无限等待
    /// </summary>
    /// <param name="hWnd">目标窗口句柄</param>
    /// <param name="Msg">消息ID</param>
    /// <param name="wParam">附加参数wParam</param>
    /// <param name="lParam">附加参数lParam</param>
    /// <param name="fuFlags">超时标志 (SMTO_*)</param>
    /// <param name="uTimeout">超时毫秒数</param>
    /// <param name="lpdwResult">消息处理结果</param>
    /// <returns>非零表示成功</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    /// <summary>正常发送，返回前不阻塞</summary>
    public const uint SMTO_NORMAL = 0x0000;
    /// <summary>阻塞直到消息被处理</summary>
    public const uint SMTO_BLOCK = 0x0001;
    /// <summary>如果目标窗口已挂起则立即放弃</summary>
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    /// <summary>如果目标未挂起则不超时（结合SMTO_ABORTIFHUNG使用）</summary>
    public const uint SMTO_NOTIMEOUTIFNOTHUNG = 0x0008;

    /// <summary>
    /// 将屏幕坐标转换为指定窗口的客户端坐标
    /// </summary>
    /// <param name="hWnd">窗口句柄</param>
    /// <param name="lpPoint">传入屏幕坐标，传出客户端坐标</param>
    /// <returns>是否成功</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    #endregion

    #region ─── 坐标结构 ───

    /// <summary>
    /// 坐标点结构（与Win32 POINT布局一致）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        /// <summary>X坐标</summary>
        public int X;
        /// <summary>Y坐标</summary>
        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    #endregion

    #region ─── System Metrics & Misc ───

    /// <summary>
    /// 获取系统度量信息
    /// </summary>
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const int SM_CXVIRTUALSCREEN = 78;   // 虚拟屏幕宽度
    public const int SM_CYVIRTUALSCREEN = 79;   // 虚拟屏幕高度
    public const int SM_XVIRTUALSCREEN = 76;    // 虚拟屏幕左边界
    public const int SM_YVIRTUALSCREEN = 77;    // 虚拟屏幕上边界

    /// <summary>
    /// 获取当前前台窗口
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    #endregion

    #region ─── 全局低层钩子 (WH_MOUSE_LL / WH_KEYBOARD_LL) ───

    /// <summary>低层鼠标钩子ID</summary>
    public const int WH_MOUSE_LL = 14;
    /// <summary>低层键盘钩子ID</summary>
    public const int WH_KEYBOARD_LL = 13;

    /// <summary>
    /// 安装低层钩子
    /// </summary>
    /// <param name="idHook">钩子类型 (WH_MOUSE_LL / WH_KEYBOARD_LL)</param>
    /// <param name="lpfn">回调函数委托</param>
    /// <param name="hMod">模块句柄</param>
    /// <param name="dwThreadId">线程ID (0=全局)</param>
    /// <returns>钩子句柄</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    /// <summary>
    /// 卸载钩子
    /// </summary>
    /// <param name="hhk">钩子句柄</param>
    /// <returns>是否成功</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    /// <summary>
    /// 传递钩子到下一个钩子链
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// 获取当前模块句柄（用于安装全局钩子）
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    /// <summary>
    /// 获取指定窗口的父窗口句柄
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetParent(IntPtr hWnd);

    /// <summary>
    /// 低层鼠标钩子结构
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        /// <summary>鼠标坐标（屏幕坐标）</summary>
        public POINT pt;
        /// <summary>鼠标数据（滚轮增量等）</summary>
        public uint mouseData;
        /// <summary>标志位</summary>
        public uint flags;
        /// <summary>时间戳</summary>
        public uint time;
        /// <summary>额外信息</summary>
        public IntPtr dwExtraInfo;
    }

    /// <summary>
    /// 低层键盘钩子结构
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        /// <summary>虚拟键码</summary>
        public uint vkCode;
        /// <summary>扫描码</summary>
        public uint scanCode;
        /// <summary>标志位</summary>
        public uint flags;
        /// <summary>时间戳</summary>
        public uint time;
        /// <summary>额外信息</summary>
        public IntPtr dwExtraInfo;
    }

    /// <summary>
    /// 低层钩子回调委托
    /// </summary>
    /// <param name="nCode">钩子代码</param>
    /// <param name="wParam">消息类型</param>
    /// <param name="lParam">消息数据指针</param>
    /// <returns>返回值</returns>
    public delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    #endregion
}
