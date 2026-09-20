using Microsoft.Extensions.Logging;
using Sharp7;

namespace Longhua.Collector.S7;

/// <summary>基于 Sharp7 的 DB 读取封装。</summary>
public sealed class PlcDbReader : IDisposable
{
    private readonly string _ip;
    private readonly int _port;
    private readonly int _rack;
    private readonly int _slot;
    private readonly ILogger _logger;
    private readonly S7Client _client = new();
    private bool _connected;

    public PlcDbReader(string ip, int port, int rack, int slot, ILogger logger)
    {
        _ip = ip;
        _port = port;
        _rack = rack;
        _slot = slot;
        _logger = logger;
    }

    public Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_connected && _client.Connected)
        {
            return Task.CompletedTask;
        }

        // Sharp7 ConnectTo 使用默认 102；非标准端口时先置参数
        if (_port != 102)
        {
            _logger.LogWarning("Sharp7 通常使用端口 102；当前配置 Port={Port}，仍尝试连接。", _port);
        }

        var rc = _client.ConnectTo(_ip, _rack, _slot);
        if (rc != 0)
        {
            throw new InvalidOperationException($"S7 连接失败 Ip={_ip} Rack={_rack} Slot={_slot} Code={rc} { _client.ErrorText(rc)}");
        }

        _connected = true;
        _logger.LogInformation("S7 已连接 {Ip} Rack={Rack} Slot={Slot}", _ip, _rack, _slot);
        return Task.CompletedTask;
    }

    public Task ReadDbAsync(int db, int start, byte[] buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_connected)
        {
            throw new InvalidOperationException("S7 未连接");
        }

        var rc = _client.DBRead(db, start, buffer.Length, buffer);
        if (rc != 0)
        {
            _connected = false;
            throw new InvalidOperationException($"DBRead 失败 DB={db} Start={start} Len={buffer.Length} Code={rc} {_client.ErrorText(rc)}");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_connected)
        {
            _client.Disconnect();
            _connected = false;
        }
    }
}
