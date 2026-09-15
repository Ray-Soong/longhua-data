using System.Text;
using System.Threading.Channels;
using Longhua.Collector.Abstractions;
using Longhua.Collector.Configuration;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Exceptions;
using MQTTnet.Protocol;

namespace Longhua.Collector.Mqtt;

public sealed class MqttDataSource : IDataSource
{
    private readonly SourceOptions _source;
    private readonly MqttSourceOptions _mqtt;
    private readonly ChannelWriter<RawFrame> _writer;
    private readonly CollectorMetrics _metrics;
    private readonly ILogger<MqttDataSource> _logger;
    private readonly MqttFactory _factory = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public MqttDataSource(
        SourceOptions source,
        ChannelWriter<RawFrame> writer,
        CollectorMetrics metrics,
        ILogger<MqttDataSource> logger)
    {
        _source = source;
        _mqtt = source.Mqtt ?? throw new InvalidOperationException($"采集源 {source.Id} 缺少 Collector:Sources:Mqtt 配置。");
        _writer = writer;
        _metrics = metrics;
        _logger = logger;
    }

    public string Id => _source.Id;

    public ProtocolKind Protocol => ProtocolKind.Mqtt;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var delay = Math.Max(1, _mqtt.ReconnectMinSeconds);
        var maxDelay = Math.Max(delay, _mqtt.ReconnectMaxSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = _factory.CreateMqttClient();
                client.ApplicationMessageReceivedAsync += OnMessageAsync;
                var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                client.DisconnectedAsync += _ =>
                {
                    disconnected.TrySetResult();
                    return Task.CompletedTask;
                };

                var options = BuildOptions();
                await client.ConnectAsync(options, cancellationToken);
                await SubscribeAsync(client, cancellationToken);
                _metrics.MarkMqttConnected(Id);
                _logger.LogInformation(
                    "MQTT 已连接 Source={SourceId} Broker={Host}:{Port} ClientId={ClientId}",
                    Id, _mqtt.Host, _mqtt.Port, _mqtt.ClientId);

                delay = Math.Max(1, _mqtt.ReconnectMinSeconds);
                using var linked = cancellationToken.Register(() => disconnected.TrySetCanceled(cancellationToken));
                try
                {
                    await disconnected.Task;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await client.DisconnectAsync(cancellationToken: CancellationToken.None);
                    }
                    catch
                    {
                        // ignored
                    }

                    break;
                }

                _logger.LogWarning("MQTT 连接断开 Source={SourceId}，准备重连", Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (MqttCommunicationException ex)
            {
                _logger.LogError(
                    "MQTT 连接失败 Source={SourceId} Broker={Host}:{Port}：{Message}",
                    Id, _mqtt.Host, _mqtt.Port, ex.InnerException?.Message ?? ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT 连接失败 Source={SourceId} Broker={Host}:{Port}", Id, _mqtt.Host, _mqtt.Port);
            }

            _metrics.MarkMqttDisconnected(Id);
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _logger.LogInformation("MQTT {Seconds}s 后重连 Source={SourceId}", delay, Id);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            delay = Math.Min(delay * 2, maxDelay);
        }
    }

    private MqttClientOptions BuildOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_mqtt.Host, _mqtt.Port)
            .WithClientId(_mqtt.ClientId)
            .WithCleanSession(_mqtt.CleanSession)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(Math.Max(5, _mqtt.KeepAliveSeconds)));

        if (!string.IsNullOrWhiteSpace(_mqtt.Username))
        {
            builder.WithCredentials(_mqtt.Username, _mqtt.Password ?? "");
        }

        if (_mqtt.UseTls)
        {
            builder.WithTlsOptions(new MqttClientTlsOptions { UseTls = true });
        }

        return builder.Build();
    }

    private async Task SubscribeAsync(IMqttClient client, CancellationToken cancellationToken)
    {
        var subscriptions = _mqtt.Subscriptions.Count == 0
            ? new List<MqttSubscriptionOptions> { new MqttSubscriptionOptions() }
            : _mqtt.Subscriptions;

        var subscribe = new MqttClientSubscribeOptionsBuilder();
        foreach (var item in subscriptions)
        {
            var qos = (MqttQualityOfServiceLevel)Math.Clamp(item.QoS, 0, 2);
            subscribe.WithTopicFilter(item.Topic, qos);
            _logger.LogInformation("MQTT 订阅 Source={SourceId} Topic={Topic} QoS={QoS}", Id, item.Topic, qos);
        }

        var result = await client.SubscribeAsync(subscribe.Build(), cancellationToken);
        foreach (var item in result.Items)
        {
            _logger.LogInformation("MQTT 订阅结果 Topic={Topic} Result={Result}", item.TopicFilter.Topic, item.ResultCode);
        }
    }

    private Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var message = args.ApplicationMessage;
        var bytes = GetPayload(message);
        string? text = null;
        if (bytes.Length > 0)
        {
            text = Encoding.UTF8.GetString(bytes);
        }

        var frame = new RawFrame
        {
            SourceId = Id,
            Protocol = ProtocolKind.Mqtt,
            Identity = message.Topic ?? "",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            PayloadBytes = bytes,
            PayloadText = text,
            MqttQoS = (int)message.QualityOfServiceLevel,
            IsRetained = message.Retain,
            ClientId = _mqtt.ClientId
        };

        _metrics.IncrementReceived(Id);
        if (!_writer.TryWrite(frame))
        {
            _metrics.IncrementDropped(Id);
            _logger.LogWarning("原始队列已满，丢弃报文 Source={SourceId} Topic={Topic}", Id, message.Topic);
        }

        return Task.CompletedTask;
    }

    private static byte[] GetPayload(MqttApplicationMessage message)
    {
        var segment = message.PayloadSegment;
        if (segment.Count > 0)
        {
            return segment.ToArray();
        }

#pragma warning disable CS0618
        return message.Payload ?? Array.Empty<byte>();
#pragma warning restore CS0618
    }
}
