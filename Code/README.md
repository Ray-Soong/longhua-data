完整操作说明（给只拿到编译文件夹的用户）：[`Doc/UserGuide.md`](../Doc/UserGuide.md)。重新编译后，该说明会复制为 `bin\Debug\net6.0\使用说明.md`，可随整个输出目录一起拷走。

# 龙华数据采集程序

C# Worker。一期订阅华章 SLS600 MQTT，把原包和规范化记录写成 JSONL。二期 S7 只在配置里预留，代码尚未采集。

本程序只读旁听 Broker，不发布任务报文。

## 目录

```
Code/
  config/                         现场要改的配置
    appsettings.json              Broker、落盘、采集源开关
    catalog.json                  设备型号与 Topic suffix 映射
    appsettings.Local.json.example  现场覆盖模板（复制后改名）
  src/Longhua.Collector/          采集进程
  src/Longhua.Collector.Tests/    Topic 解析与规范化单测
  data/                           运行后生成（按日分目录）
```

## 配置

先改 `config/appsettings.json` 里的 Broker。现场密码建议复制模板：

```bash
cp config/appsettings.Local.json.example config/appsettings.Local.json
```

`appsettings.Local.json` 会覆盖同名键，不要提交到 git。

也可用环境变量覆盖（双下划线）：

```bash
Collector__Sources__0__Mqtt__Host=10.0.0.10
Collector__Sources__0__Mqtt__Password=secret
LONGHUA_CONFIG_DIR=/opt/longhua/config
```

### appsettings.json 主要项

| 键 | 含义 |
|---|---|
| `Collector:DataRoot` | 数据根目录。相对路径相对于 `config/` 的上一级（`Code/` 或发布目录） |
| `Collector:TimeZone` | 按该时区切 `yyyy-MM-dd` 目录，默认 `Asia/Shanghai` |
| `Collector:RawChannelCapacity` | 原始队列容量。满则丢包并打日志 |
| `Collector:HealthStaleSeconds` | `HEALTH` 超时后写一条 `Quality=Stale` |
| `Collector:Deduplicate` | 是否按 Identity+时间戳+载荷去重（QoS1 重复时用） |
| `Collector:FileSink:FlushIntervalMs` | 落盘 flush 间隔 |
| `Collector:FileSink:MaxFileBytes` | 单文件滚动阈值，0 表示不按体积切 |
| `Collector:FileSink:KeepDays` | 超过天数的日期目录会删除 |
| `Collector:Sources[]` | 采集源。`Protocol` 为 `Mqtt` 或 `S7`，`Enabled` 控制是否启动 |

MQTT 源：

| 键 | 含义 |
|---|---|
| `Mqtt:Host` / `Port` / `UseTls` | Broker 地址 |
| `Mqtt:ClientId` | 稳定客户端 ID，配合 `CleanSession: false` 接 QoS1 补发 |
| `Mqtt:Username` / `Password` | 可空 |
| `Mqtt:Subscriptions` | 默认 `+/+/HUAZH/#`，QoS 1 |

S7 源：`Enabled` 保持 `false`。打开只会打警告，不会读 PLC。点表字段已留在配置里。

### catalog.json

把协议里的 `modelName` 映射成 `DeviceType`，把 suffix 映射成 `EventKind`。任务类 suffix 的 `File` 仅作内部分类，落盘不再按设备拆文件。

改现场设备编码或补 Topic 时，优先改这个文件，不必改代码。

## 运行

需要 **.NET 6 SDK**。`global.json` 已对准本机常见版本 `6.0.425`。

在 `Code` 目录执行：

```bat
dotnet --version
dotnet run --project src\Longhua.Collector\Longhua.Collector.csproj
```

`dotnet --version` 应显示 `6.0.425`（或同 band 的更高补丁）。用命令行即可编译运行；Visual Studio 2019 仍可能无法打开项目，请用 VS 2022 或继续用 `dotnet`。

发给 Windows 现场请用（会生成真正的 Windows exe）：

```bat
dotnet publish src\Longhua.Collector\Longhua.Collector.csproj -c Release -r win-x64 --self-contained false -o .\publish-win
```

拷贝整个 `publish-win` 文件夹。不要把 Linux 编译的 `bin\Debug\net6.0` 直接给 Windows 用户；那边的 `Longhua.Collector.exe` 无法运行。

临时补救（用户电脑已装 .NET 6）：在程序目录执行 `dotnet Longhua.Collector.dll`。

发布目录会带上 `config/`。可用 `LONGHUA_CONFIG_DIR` 指向另一份配置。

Windows 服务：发布后用 `sc.exe create` 指向 `Longhua.Collector.exe`，工作目录为发布目录。

## 落盘

```
data/{yyyy-MM-dd}/
  collect.jsonl
```

每一行是回放信封：`linkId`（本次 MQTT 连接 ID，重启后会变）+ `dataType` + `name` + `timestamp` + `data`。

```json
{"linkId":"3f2a9c1b8e0d47a1b4c55e6f708192a3","dataType":"Rgv","name":"Rgv1","timestamp":"2017-04-15T11:40:03.12Z","data":{"warehouseNo":"1","carNo":1,"status":"1"}}
```

`raw/`、`dead-letter/` 默认关闭，可在 `FileSink` 打开。

## 测试

```bash
cd Code
dotnet test
```
