using System.Threading.Channels;
using Longhua.Collector.Abstractions;
using Longhua.Collector.Mqtt;
using Longhua.Collector.S7;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Longhua.Collector.Configuration;

public sealed class DataSourceFactory
{
    private readonly ChannelWriter<RawFrame> _writer;
    private readonly CollectorMetrics _metrics;
    private readonly ILoggerFactory _loggerFactory;
    private readonly CollectorOptions _options;

    public DataSourceFactory(
        ChannelWriter<RawFrame> writer,
        CollectorMetrics metrics,
        ILoggerFactory loggerFactory,
        IOptions<CollectorOptions> options)
    {
        _writer = writer;
        _metrics = metrics;
        _loggerFactory = loggerFactory;
        _options = options.Value;
    }

    public IDataSource Create(SourceOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Id))
        {
            throw new InvalidOperationException("Collector:Sources:Id 不能为空。");
        }

        return options.Protocol.Trim() switch
        {
            "Mqtt" or "MQTT" or "mqtt" => new MqttDataSource(
                options,
                _writer,
                _metrics,
                _loggerFactory.CreateLogger<MqttDataSource>()),
            "S7" or "s7" => new S7DataSource(
                options,
                _metrics,
                _loggerFactory.CreateLogger<S7DataSource>(),
                _options.DataRoot,
                _options.FileSink.PlcFileName,
                writer: null),
            _ => throw new InvalidOperationException($"未知协议 {options.Protocol}，采集源 {options.Id}。")
        };
    }
}
