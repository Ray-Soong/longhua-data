using System.Threading.Channels;
using Longhua.Collector;
using Longhua.Collector.Abstractions;
using Longhua.Collector.Configuration;
using Longhua.Collector.Persistence;
using Longhua.Collector.Pipeline;
using Longhua.Collector.S7;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var (mode, remainingArgs) = CliMode.Parse(args);
string? configDir = null;

if (string.Equals(mode, "phase", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = await RunPhaseAsync(remainingArgs);
    return;
}

var hostBuilder = Host.CreateDefaultBuilder(remainingArgs)
    .UseWindowsService(options => options.ServiceName = "LonghuaCollector")
    .ConfigureAppConfiguration((ctx, config) =>
    {
        if (WindowsServiceHelpers.IsWindowsService())
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        }

        configDir = ConfigPaths.ResolveConfigDir(ctx.HostingEnvironment.ContentRootPath);
        config.Sources.Clear();
        config
            .AddJsonFile(Path.Combine(configDir, "appsettings.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine(configDir, "catalog.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine(configDir, $"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json"), optional: true, reloadOnChange: true)
            .AddJsonFile(Path.Combine(configDir, "appsettings.Local.json"), optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(remainingArgs);
    })
    .ConfigureServices((ctx, services) =>
    {
        var dir = configDir ?? ConfigPaths.ResolveConfigDir(ctx.HostingEnvironment.ContentRootPath);
        services.Configure<CollectorOptions>(ctx.Configuration.GetSection(CollectorOptions.SectionName));
        services.Configure<CatalogOptions>(ctx.Configuration.GetSection(CatalogOptions.SectionName));
        services.PostConfigure<CollectorOptions>(options =>
        {
            options.Mode = string.IsNullOrWhiteSpace(options.Mode) ? "collection" : options.Mode;
            options.DataRoot = ConfigPaths.ResolveDataRoot(options.DataRoot, dir);
            Directory.CreateDirectory(options.DataRoot);
            if (string.IsNullOrWhiteSpace(options.FileSink.FileName))
            {
                options.FileSink.FileName = "mqtt";
            }

            if (string.IsNullOrWhiteSpace(options.FileSink.PlcFileName))
            {
                options.FileSink.PlcFileName = "plc";
            }
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;
            return TimeZoneHelper.Resolve(options.TimeZone);
        });
        services.AddSingleton(sp =>
        {
            var catalog = sp.GetRequiredService<IOptions<CatalogOptions>>().Value;
            return DeviceCatalog.FromOptions(catalog);
        });
        services.AddSingleton<FrameNormalizer>();
        services.AddSingleton<CollectorMetrics>();
        services.AddSingleton(sp =>
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
        services.AddSingleton(sp => sp.GetRequiredService<Channel<RawFrame>>().Writer);
        services.AddSingleton<ITelemetrySink, FileTelemetrySink>();
        services.AddSingleton<HealthWatch>();
        services.AddSingleton<DataSourceFactory>();
        services.AddHostedService<CollectorWorker>();
    });

if (WindowsServiceHelpers.IsWindowsService())
{
    hostBuilder.UseContentRoot(AppContext.BaseDirectory);
}

var host = hostBuilder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
var collector = host.Services.GetRequiredService<IOptions<CollectorOptions>>().Value;
logger.LogInformation(
    "模式 {Mode}，配置目录 {ConfigDir}，数据目录 {DataRoot}，MQTT文件={Mqtt} PLC文件={Plc}",
    mode,
    configDir ?? "(未解析)",
    collector.DataRoot,
    collector.FileSink.FileName,
    collector.FileSink.PlcFileName);
await host.RunAsync();

static async Task<int> RunPhaseAsync(string[] args)
{
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((ctx, config) =>
        {
            var dir = ConfigPaths.ResolveConfigDir(ctx.HostingEnvironment.ContentRootPath);
            config.Sources.Clear();
            config
                .AddJsonFile(Path.Combine(dir, "appsettings.json"), optional: false, reloadOnChange: false)
                .AddJsonFile(Path.Combine(dir, "catalog.json"), optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .AddCommandLine(args);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["__ConfigDir"] = dir
            });
        })
        .ConfigureServices((ctx, services) =>
        {
            services.Configure<CollectorOptions>(ctx.Configuration.GetSection(CollectorOptions.SectionName));
            services.AddLogging();
            services.AddSingleton<PhaseRunner>();
        })
        .Build();

    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Phase");
    var options = host.Services.GetRequiredService<IOptions<CollectorOptions>>().Value;
    var configDir = ConfigPaths.ResolveConfigDir(Directory.GetCurrentDirectory());
    var cli = CliMode.ParsePhase(args, options, configDir);
    logger.LogInformation(
        "phase Input={Input} BlockLength={Block} PointTable={Table} Output={Output}",
        cli.InputPath, cli.BlockLength, cli.PointTablePath, cli.OutputPath);
    var runner = host.Services.GetRequiredService<PhaseRunner>();
    return await runner.RunAsync(cli, CancellationToken.None);
}

internal static class CliMode
{
    public static (string Mode, string[] Remaining) Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return ("collection", args);
        }

        var first = args[0].Trim();
        if (first.Equals("collection", StringComparison.OrdinalIgnoreCase)
            || first.Equals("phase", StringComparison.OrdinalIgnoreCase)
            || first.Equals("collect", StringComparison.OrdinalIgnoreCase))
        {
            var mode = first.Equals("collect", StringComparison.OrdinalIgnoreCase) ? "collection" : first.ToLowerInvariant();
            return (mode, args.Skip(1).ToArray());
        }

        return ("collection", args);
    }

    public static PhaseCliOptions ParsePhase(string[] args, CollectorOptions options, string configDir)
    {
        var cli = new PhaseCliOptions
        {
            InputPath = options.Phase.Input,
            OutputPath = options.Phase.Output,
            BlockLength = options.Phase.BlockLength,
            PointTablePath = ResolvePointTable(options.Phase.PointTable, configDir)
        };

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (TryGet(args, ref i, a, "--input", "-i", out var input))
            {
                cli.InputPath = input;
            }
            else if (TryGet(args, ref i, a, "--output", "-o", out var output))
            {
                cli.OutputPath = output;
            }
            else if (TryGet(args, ref i, a, "--point-table", "-p", out var table))
            {
                cli.PointTablePath = ResolvePointTable(table, configDir);
            }
            else if (TryGet(args, ref i, a, "--block-length", "-b", out var block)
                     && int.TryParse(block, out var n))
            {
                cli.BlockLength = n;
            }
            else if (a.StartsWith("Collector:Phase:", StringComparison.OrdinalIgnoreCase)
                     || a.StartsWith("--Collector:Phase:", StringComparison.OrdinalIgnoreCase))
            {
                // 也接受配置风格覆盖
            }
        }

        return cli;
    }

    private static string ResolvePointTable(string path, string configDir)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "plc-conveyor.json";
        }

        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        var inConfig = Path.Combine(configDir, path);
        if (File.Exists(inConfig))
        {
            return inConfig;
        }

        var cwd = Path.GetFullPath(path);
        return File.Exists(cwd) ? cwd : inConfig;
    }

    private static bool TryGet(string[] args, ref int i, string current, string longName, string shortName, out string value)
    {
        value = "";
        if (current.Equals(longName, StringComparison.OrdinalIgnoreCase)
            || current.Equals(shortName, StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 >= args.Length)
            {
                return false;
            }

            value = args[++i];
            return true;
        }

        var prefix = longName + "=";
        if (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = current[prefix.Length..];
            return true;
        }

        return false;
    }
}

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
