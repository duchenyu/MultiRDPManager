using System.Collections.Generic;
using System.Runtime.InteropServices;
using MultiRDPManager.FreeRDP.Native;

namespace MultiRDPManager.FreeRDP.Services;

/// <summary>
/// 群控输入钩子 — 使用 WH_MOUSE_LL 和 WH_KEYBOARD_LL 全局钩子
/// 拦截主控窗口的输入，按百分比坐标映射转发到所有从机 wfreerdp 窗口。
/// WindowsFormsHost 的 HWND 直接拦截 Win32 鼠标/键盘消息，
/// WPF 的隧道事件（PreviewMouseDown 等）对此无效，
/// 因此必须使用全局低层钩子在 Win32 层面截获输入。
/// 
/// 鼠标和键盘都走 PostMessage → UI 线程 WndProc → Task.Run(SendMessageTimeout) 路径，
/// 避免在全局钩子回调中阻塞系统输入队列。
/// </summary>
public class GroupControlHook : IDisposable
{
    private IntPtr _mouseHookId = IntPtr.Zero;
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private Win32.LowLevelHookProc? _mouseProc;
    private Win32.LowLevelHookProc? _keyboardProc;

    /// <summary>
    /// 主WPF窗口句柄 — 用于从钩子线程 PostMessage 自定义消息到 UI 线程
    /// UI 线程收到消息后在安全上下文中执行实际的 PostMessage 到从机
    /// </summary>
    public IntPtr MainWindowHwnd { get; set; } = IntPtr.Zero;

    /// <summary>
    /// 自定义消息 ID（WM_APP + 1），由鼠标钩子回调 PostMessage 到主窗口
    /// </summary>
    public const uint WM_APP_FORWARD_MOUSE = 0x8001;

    /// <summary>
    /// 自定义消息 ID（WM_APP + 3），由键盘钩子回调 PostMessage 到主窗口
    /// 分离消息 ID 便于 WndProc 区分不同类型的转发请求
    /// </summary>
    public const uint WM_APP_FORWARD_KEYBOARD = 0x8003;

    /// <summary>
    /// 缓存从机窗口信息（HWND + 预取的客户区尺寸）
    /// </summary>
    private struct SlaveInfo
    {
        public IntPtr Hwnd;
        public int Width;
        public int Height;
    }
    private readonly List<SlaveInfo> _slaves = new();

    /// <summary>
    /// 更新从机列表并预取尺寸
    /// </summary>
    public void UpdateSlaves(List<IntPtr> slaveHwnds)
    {
        lock (_slaves)
        {
            _slaves.Clear();
            foreach (var hwnd in slaveHwnds)
            {
                if (hwnd == IntPtr.Zero) continue;
                if (Win32.GetClientRect(hwnd, out var rect) && rect.Width > 0 && rect.Height > 0)
                {
                    _slaves.Add(new SlaveInfo { Hwnd = hwnd, Width = rect.Width, Height = rect.Height });
                }
            }
        }
    }

    /// <summary>
    /// 当前从机数量（线程安全快照）
    /// </summary>
    public int SlaveCount
    {
        get { lock (_slaves) { return _slaves.Count; } }
    }

    // ── 诊断日志 ──
    private static readonly object _logLock = new();
    private static List<string> _logBuffer = new(1024);

    /// <summary>
    /// 日志文件路径（默认 %TEMP%\MultiRDPManager_DIAG.log）
    /// </summary>
    public string LogFilePath { get; set; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "MultiRDPManager_DIAG.log");

    /// <summary>
    /// 追加一行诊断日志（线程安全，自动在最后写入文件）
    /// </summary>
    private void Log(string message)
    {
        lock (_logLock)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            _logBuffer.Add(line);
            if (_logBuffer.Count >= 10)
                FlushLog();
        }
    }

    /// <summary>
    /// 将缓冲日志写入文件
    /// </summary>
    private void FlushLog()
    {
        lock (_logLock)
        {
            if (_logBuffer.Count == 0) return;
            try
            {
                System.IO.File.AppendAllLines(LogFilePath, _logBuffer);
                _logBuffer.Clear();
            }
            catch { }
        }
    }

    /// <summary>
    /// 主控窗口句柄（FreeRdpControl.Handle，覆盖完整可视区域，用于鼠标边界检测和百分比坐标计算）
    /// </summary>
    public IntPtr MasterHwnd { get; set; } = IntPtr.Zero;

    private bool _isActive;

    /// <summary>
    /// 启动全局低层钩子（鼠标 + 键盘）
    /// 钩子回调必须保持委托引用，防止 GC 回收导致崩溃
    /// </summary>
    public void Start()
    {
        if (_isActive) return;

        // 将回调委托存入字段，防止 GC 回收
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;

        using (var process = System.Diagnostics.Process.GetCurrentProcess())
        using (var module = process.MainModule!)
        {
            IntPtr hMod = Win32.GetModuleHandle(module.ModuleName!);
            _mouseHookId = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, hMod, 0);
            _keyboardHookId = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        }

        _isActive = true;
    }

    /// <summary>
    /// 停止并卸载所有钩子
    /// </summary>
    public void Stop()
    {
        if (!_isActive) return;

        if (_mouseHookId != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
        if (_keyboardHookId != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }

        _isActive = false;
        FlushLog(); // 停止钩子时刷新缓冲日志
    }

    /// <summary>
    /// 转发限流信号量 — 确保点击事件按顺序逐个处理，避免并发冲突
    /// </summary>
    private readonly System.Threading.SemaphoreSlim _forwardSemaphore = new(1, 1);

    /// <summary>
    /// 键盘转发限流信号量 — 独立于鼠标，防止键盘事件被鼠标阻塞
    /// </summary>
    private readonly System.Threading.SemaphoreSlim _keyboardForwardSemaphore = new(1, 1);

    /// <summary>
    /// 开启群控后调用 — 向所有从机发送 WM_SETFOCUS 
    /// wfreerdp 的 SuspendInput 在失焦后为 TRUE，会丢弃所有输入。
    /// 只有收到 WM_SETFOCUS 才会设为 FALSE 并初始化输入通道。
    /// 可以从机窗口从未收到过焦点，导致所有转发输入被静默丢弃。
    /// </summary>
    public void WarmUpFocus()
    {
        const uint flags = Win32.SMTO_ABORTIFHUNG | Win32.SMTO_NOTIMEOUTIFNOTHUNG;
        const uint timeoutMs = 500;
        const uint WM_SETFOCUS = 0x0007;

        List<SlaveInfo> snapshot;
        lock (_slaves)
        {
            snapshot = new List<SlaveInfo>(_slaves);
        }
        if (snapshot.Count == 0) return;

        int count = 0;
        foreach (var slave in snapshot)
        {
            try
            {
                // 发送 WM_SETFOCUS → wfreerdp 设置 SuspendInput=FALSE, 初始化输入通道
                Win32.SendMessageTimeout(slave.Hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero,
                    flags, timeoutMs, out _);
                count++;
            }
            catch { }
        }
        if (count > 0)
            Log($"FOCUS: sent WM_SETFOCUS to {count}/{snapshot.Count} slaves");
    }

    /// <summary>
    /// 在 UI 线程上调用 — 使用 SendMessageTimeout 从后台线程转发输入到所有从机
    /// 
    /// 为什么用 SendMessageTimeout 而非 PostMessage：
    /// PostMessage 仅将消息放入目标线程的消息队列，但 wfreerdp 在窗口不可见（off-screen）时
    /// 可能暂停/节流其消息泵，导致消息永远不被派发。
    /// SendMessage 跨进程时会通过系统机制将消息直接投递到目标窗口过程，保证送达。
    /// 
    /// 使用后台线程执行 SendMessageTimeout 避免阻塞 UI 线程。
    /// 对于 DOWN 事件，立即跟一个 UP 事件，防止 wfreerdp 的 SetCapture 长期持有鼠标捕获。
    /// 
    /// 关键改进：
    /// 1. SendMessageTimeout — 同步投递 + 超时保护
    /// 2. WM_MOUSEMOVE 预热 — 在点击前发送鼠标移动，初始化 wfreerdp 输入状态机
    /// 3. 限流信号量 — 顺序处理点击事件，避免并发冲突
    /// </summary>
    public void ForwardToSlaves(double pctX, double pctY, uint msg)
    {
        int wParamFlags = 0;
        uint pairedMsg = 0; // 无配对
        bool isDebugMsg = (msg == Win32.WM_MOUSEMOVE);
        switch (msg)
        {
            case Win32.WM_LBUTTONDOWN:
            case Win32.WM_LBUTTONDBLCLK:
                wParamFlags = Win32.MK_LBUTTON;
                pairedMsg = Win32.WM_LBUTTONUP; // DOWN → 立即跟 UP
                break;
            case Win32.WM_LBUTTONUP:
                wParamFlags = Win32.MK_LBUTTON;
                break;
            case Win32.WM_RBUTTONDOWN:
            case Win32.WM_RBUTTONDBLCLK:
                wParamFlags = Win32.MK_RBUTTON;
                pairedMsg = Win32.WM_RBUTTONUP;
                break;
            case Win32.WM_RBUTTONUP:
                wParamFlags = Win32.MK_RBUTTON;
                break;
        }
        IntPtr wParamForSlave = new IntPtr(wParamFlags);

        // 在锁下获取从机列表快照（避免在后台线程持锁）
        List<SlaveInfo> snapshot;
        lock (_slaves)
        {
            snapshot = new List<SlaveInfo>(_slaves);
        }

        if (snapshot.Count == 0)
        {
            if (!isDebugMsg)
                Log($"FWD: no slaves to forward (msg=0x{msg:X4})");
            return;
        }

        if (!isDebugMsg)
            Log($"FWD: msg=0x{msg:X4} pct=({pctX:F4},{pctY:F4}) slaves={snapshot.Count}");

        // 使用后台线程执行 SendMessageTimeout + 限流（不阻塞 UI 线程）
        // 限流确保连续点击不会产生并发冲突
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await _forwardSemaphore.WaitAsync();
                try
                {
                    const uint flags = Win32.SMTO_ABORTIFHUNG | Win32.SMTO_NOTIMEOUTIFNOTHUNG;
                    const uint timeoutMs = 1000;
                    IntPtr wParamMove = IntPtr.Zero;
                    int successCount = 0;
                    int failCount = 0;

                    foreach (var slave in snapshot)
                    {
                        int slaveX = (int)(pctX * slave.Width);
                        int slaveY = (int)(pctY * slave.Height);
                        slaveX = Math.Clamp(slaveX, 0, Math.Max(0, slave.Width - 1));
                        slaveY = Math.Clamp(slaveY, 0, Math.Max(0, slave.Height - 1));
                        IntPtr lParamForSlave = new IntPtr((slaveY << 16) | (slaveX & 0xFFFF));
                        string hwndStr = $"0x{slave.Hwnd.ToInt64():X8}";

                        try
                        {
                            // ── 第1步: WM_MOUSEMOVE 预热 ──
                            // 注意：WM_MOUSEMOVE 的返回值可能是0（wfreerdp 可合法返回0），不纳入成功判定
                            Win32.SendMessageTimeout(slave.Hwnd, Win32.WM_MOUSEMOVE, wParamMove, lParamForSlave,
                                flags, timeoutMs, out _);

                            // ── 第2步: DOWN/UP 等实际消息 ──
                            IntPtr ret2 = Win32.SendMessageTimeout(slave.Hwnd, msg, wParamForSlave, lParamForSlave,
                                flags, timeoutMs, out _);

                            // ── 第3步: 如果是 DOWN 事件，立即发送 UP 事件 ──
                            IntPtr ret3 = IntPtr.Zero;
                            if (pairedMsg != 0)
                            {
                                ret3 = Win32.SendMessageTimeout(slave.Hwnd, pairedMsg, wParamForSlave, lParamForSlave,
                                    flags, timeoutMs, out _);
                            }

                            // 检查返回值：只判断实际消息（非 WM_MOUSEMOVE）的返回值
                            // WM_MOUSEMOVE 可以合法返回0，不纳入成功判定
                            if (ret2 != IntPtr.Zero && (pairedMsg == 0 || ret3 != IntPtr.Zero))
                            {
                                successCount++;
                            }
                            else
                            {
                                failCount++;
                                if (!isDebugMsg)
                                    Log($"FWD-RET {hwndStr}: down={ret2.ToInt64():X4} up={ret3.ToInt64():X4}");
                            }
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            if (!isDebugMsg)
                                Log($"FWD-ERR {hwndStr}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    if (!isDebugMsg)
                        Log($"FWD: done msg=0x{msg:X4} ok={successCount}/{snapshot.Count} fail={failCount}");
                }
                catch (Exception ex)
                {
                    if (!isDebugMsg)
                        Log($"FWD-SEM: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    _forwardSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                // Task.Run 顶层异常防护（TaskSchedulerException 等）
                Log($"FWD-TASK: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 键盘转发到从机（由 WndProc 在 UI 线程触发，后台线程执行 SendMessageTimeout）
    /// </summary>
    public void ForwardKeyboardToSlaves(uint msg, uint vkCode, uint scanCode, uint flags)
    {
        List<SlaveInfo> snapshot;
        lock (_slaves)
        {
            snapshot = new List<SlaveInfo>(_slaves);
        }

        if (snapshot.Count == 0) return;

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (!_keyboardForwardSemaphore.Wait(3000))
                {
                    Log($"KEY-FWD: semaphore timeout, skipping msg=0x{msg:X4} vk=0x{vkCode:X2}");
                    return;
                }
                try
                {
                    const uint smFlags = Win32.SMTO_ABORTIFHUNG | Win32.SMTO_NOTIMEOUTIFNOTHUNG;
                    const uint timeoutMs = 1000;
                    IntPtr wParamForSlave = new IntPtr(vkCode);
                    int lParamFlags = (int)((scanCode << 16) | (flags << 24));
                    IntPtr lParamForSlave = new IntPtr(lParamFlags);
                    int fwd = 0;

                    foreach (var slave in snapshot)
                    {
                        if (slave.Hwnd == IntPtr.Zero) continue;
                        try
                        {
                            Win32.SendMessageTimeout(slave.Hwnd, msg, wParamForSlave, lParamForSlave,
                                smFlags, timeoutMs, out _);
                            fwd++;
                        }
                        catch { }
                    }
                    if (fwd > 0)
                        Log($"KEY-FWD: msg=0x{msg:X4} vk=0x{vkCode:X2} fwd={fwd}/{snapshot.Count}");
                }
                finally
                {
                    _keyboardForwardSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                // Task.Run 顶层异常防护
                Log($"KEY-FWD-TASK: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 鼠标钩子回调 — 仅检测并转发到 UI 线程，不直接 PostMessage
    /// 避免 wfreerdp 的 SetCapture 偷走鼠标输入
    /// </summary>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode < 0 || !_isActive || MasterHwnd == IntPtr.Zero)
                return Win32.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);

            if (!Win32.GetWindowRect(MasterHwnd, out var masterRect))
                return Win32.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);

            var hookStruct = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            bool isOver = hookStruct.pt.X >= masterRect.Left && hookStruct.pt.X <= masterRect.Right &&
                          hookStruct.pt.Y >= masterRect.Top && hookStruct.pt.Y <= masterRect.Bottom;

            if (!isOver || masterRect.Width <= 0 || masterRect.Height <= 0)
                return Win32.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);

            // ── 关键改动：不在这里 PostMessage，改为发自定义消息给主窗口 ──
            // 主窗口在 UI 线程上处理自定义消息 → 执行 PostMessage + 还原鼠标捕获
            uint msg = (uint)wParam;
            bool isClick = (msg == Win32.WM_LBUTTONDOWN || msg == Win32.WM_RBUTTONDOWN);
            if (isClick && MainWindowHwnd != IntPtr.Zero)
            {
                // 将鼠标事件信息编码到 lParam: 低16位=X, 高16位=Y
                int x = hookStruct.pt.X - masterRect.Left;
                int y = hookStruct.pt.Y - masterRect.Top;
                IntPtr lParamData = new IntPtr((y << 16) | (x & 0xFFFF));
                // PostMessage 到主窗口（而不是直接到从机）
                // 这不会触发 SetCapture，因为消息是发到我们自己的窗口
                Win32.PostMessage(MainWindowHwnd, WM_APP_FORWARD_MOUSE, new IntPtr((int)msg), lParamData);
                Log($"MOUSE-Q msg=0x{msg:X4} pos=({x},{y})");
            }
        }
        catch (Exception ex)
        {
            Log($"MOUSE-ERR: {ex.GetType().Name}: {ex.Message}");
        }

        return Win32.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// 键盘钩子回调 — 群控开启时，PostMessage 键盘事件到主窗口 UI 线程处理
    /// 
    /// 修复：原实现直接在钩子回调中调用 SendMessageTimeout（阻塞1000ms），
    /// 这会在系统键盘消息队列中产生延迟，导致从机窗口挂起时 UI 卡顿。
    /// 改为 PostMessage → UI 线程 WndProc → Task.Run(SendMessageTimeout)，
    /// 与鼠标转发路径一致，避免阻塞全局钩子回调。
    /// </summary>
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode < 0 || !_isActive || MainWindowHwnd == IntPtr.Zero)
                return Win32.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);

            uint msg = (uint)wParam;
            if (msg != Win32.WM_KEYDOWN && msg != Win32.WM_KEYUP &&
                msg != Win32.WM_SYSKEYDOWN && msg != Win32.WM_SYSKEYUP)
                return Win32.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);

            // 检查前台窗口
            var foreground = Win32.GetForegroundWindow();
            bool isOurApp = (foreground == MainWindowHwnd) || IsChildOf(foreground, MainWindowHwnd);
            if (!isOurApp)
                return Win32.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);

            var hookStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);

            // 编码键盘事件到 lParam: 低16位=scanCode, 高16位=flags
            // wParam = 消息类型 (WM_KEYDOWN/WM_KEYUP/WM_SYSKEYDOWN/WM_SYSKEYUP)
            // lParam → 低16位 vkCode, 中16位 scanCode, 高8位 flags
            IntPtr lParamData = new IntPtr((int)((hookStruct.flags << 24) | (hookStruct.scanCode << 16) | hookStruct.vkCode));
            Win32.PostMessage(MainWindowHwnd, WM_APP_FORWARD_KEYBOARD, wParam, lParamData);

            Log($"KEY-Q msg=0x{msg:X4} vk=0x{hookStruct.vkCode:X2} sc=0x{hookStruct.scanCode:X2}");
        }
        catch (Exception ex)
        {
            Log($"KEY-ERR: {ex.GetType().Name}: {ex.Message}");
        }

        return Win32.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// 检查 window 是否是 parent 的子窗口（递归向上查找父窗口链）
    /// </summary>
    /// <param name="window">待检查的窗口句柄</param>
    /// <param name="parent">目标父窗口句柄</param>
    /// <returns>如果是 parent 的子窗口返回 true</returns>
    private bool IsChildOf(IntPtr window, IntPtr parent)
    {
        if (window == IntPtr.Zero || parent == IntPtr.Zero) return false;
        if (window == parent) return true;

        IntPtr current = window;
        while (current != IntPtr.Zero)
        {
            current = Win32.GetParent(current);
            if (current == parent) return true;
        }
        return false;
    }

    /// <summary>
    /// 释放资源 — 停止并卸载所有钩子
    /// </summary>
    public void Dispose()
    {
        Stop();
        FlushLog(); // 关闭前写入所有缓冲日志
    }
}
