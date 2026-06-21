namespace MultiRDPManager.FreeRDP.Models
{
    /// <summary>
    /// 服务器连接状态枚举
    /// </summary>
    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Disconnecting,
        Error
    }
}
