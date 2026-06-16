namespace Azdo.Core.Polling;

/// <summary>Connection/health state shown in the status bar (≈ <c>polling.ConnectionState</c>).</summary>
public enum ConnectionState
{
    Connecting,
    Connected,
    Disconnected,
    Error,
}
