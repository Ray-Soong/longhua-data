using System.Threading.Channels;
using Longhua.Collector;
using Longhua.Collector.Abstractions;
using Longhua.Collector.Configuration;
using Longhua.Collector.Persistence;
using Longhua.Collector.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

var settings = new HostApplicationBuilderSettings { Args = args };
if (WindowsServiceHelpers.IsWindowsService())
{
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
    settings.ContentRootPath = AppContext.BaseDirectory;
}

var builder = Host.CreateApplicationBuilder(settings);
if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService(options => options.ServiceName = "LonghuaCollector");
}

var configDir = ConfigPaths.ResolveConfigDir(builder.Environment.ContentRootPath);
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(Path.Combine(configDir, "appsettings.json"), optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine(configDir, "catalog.json"), optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine(configDir, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(configDir, "appsettings.Local.json"), optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.Configure<CollectorOptions>(builder.Configuration.GetSection(CollectorOptions.SectionName));
builder.Services.Configure<CatalogOptions>(builder.Configuration.GetSection(CatalogOptions.SectionName));
builder.Services.PostConfigure<CollectorOptions>(options =>
{
    options.DataRoot = ConfigPaths.ResolveDataRoot(options.DataRoot, configDir);
    Directory.CreateDirectory(options.DataRoot);
});

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;
    return TimeZoneHelper.Resolve(options.TimeZone);
});

builder.Services.AddSingleton(sp =>
{
    var catalog = sp.GetRequiredService<IOptions<CatalogOptions>>().Value;
    return DeviceCatalog.FromOptions(catalog);
});

builder.Services.AddSingleton<FrameNormalizer>();
builder.Services.AddSingleton<CollectorMetrics>();
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;
    var capacity = Math.Max(100, options.RawChannelCapacity);
    return Channel.CreateBounded<RawFrame>(new BoundedChannelOptions(capacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<Channel<RawFrame>>().Writer);
builder.Services.AddSingleton<ITelemetrySink, FileTelemetrySink>();
builder.Services.AddSingleton<HealthWatch>();
builder.Services.AddSingleton<DataSourceFactory>();
builder.Services.AddHostedService<CollectorWorker>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var collector = host.Services.GetRequiredService<IOptions<CollectorOptions>>().Value;
logger.LogInformation("配置目录 {ConfigDir}，数据目录 {DataRoot}", configDir, collector.DataRoot);
await host.RunAsync();

internal static class ConfigPaths
{
    public static string ResolveConfigDir(string contentRoot)
    {
        var envDir = Environment.GetEnvironmentVariable("LONGHUA_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(envDir) && File.Exists(Path.Combine(envDir, "appsettings.json")))
        {
            return Path.GetFullPath(envDir);
        }

        var starts = new[]
        {
            contentRoot,
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var start in starts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var probe = new DirectoryInfo(Path.GetFullPath(start));
            while (probe is not null)
            {
                var dir = Path.Combine(probe.FullName, "config");
                if (File.Exists(Path.Combine(dir, "appsettings.json"))
                    && File.Exists(Path.Combine(dir, "catalog.json")))
                {
                    return dir;
                }

                probe = probe.Parent;
            }
        }

        throw new FileNotFoundException(
            "找不到 config/appsettings.json。请将配置放在程序目录/config，或设置环境变量 LONGHUA_CONFIG_DIR。");
    }

    public static string ResolveDataRoot(string dataRoot, string configDir)
    {
        if (Path.IsPathRooted(dataRoot))
        {
            return Path.GetFullPath(dataRoot);
        }

        var codeRoot = Directory.GetParent(configDir)?.FullName ?? configDir;
        return Path.GetFullPath(Path.Combine(codeRoot, dataRoot));
    }
}

internal static class TimeZoneHelper
{
    public static TimeZoneInfo Resolve(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            if (id.Equals("Asia/Shanghai", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            }

            if (id.Equals("China Standard Time", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            }

            throw;
        }
    }
}
