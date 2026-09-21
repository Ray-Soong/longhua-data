完整操作说明（给只拿到编译文件夹的用户）：[`Doc/UserGuide.md`](../Doc/UserGuide.md)。重新编译后，该说明会复制为输出目录里的 `使用说明.md`。

# 龙华数据采集程序

C# Worker。旁听华章 SLS600 MQTT，并用 Sharp7 轮询库外 PLC（DB3）。**只读**，不发布任务。

两种模式：

| 模式 | 说明 |
|---|---|
| `collection`（默认） | MQTT → `mqtt.jsonl`；PLC → `plc.txt` |
| `phase` | 离线解析 PLC dump → 工位 JSONL |

点表：`config/plc-conveyor.json`（工位 **1001–1038**，有效区 **494** 字节）。业务说明见 Unity 侧 `LongHua/Doc/Requirements/Data.md`。

## 目录

```
Code/
  config/
    appsettings.json              Broker、PLC、双文件名、Phase 默认参数
    catalog.json                  MQTT 型号 / EventKind
    plc-conveyor.json             PLC 工位点表
    appsettings.Local.json.example
  src/Longhua.Collector/          采集进程（含 S7 / phase）
  src/Longhua.Collector.Tests/
  data/                           运行后生成
```

## 配置

```bash
cp config/appsettings.Local.json.example config/appsettings.Local.json
```

环境变量示例：

```bash
Collector__Sources__0__Mqtt__Host=10.0.0.10
Collector__Sources__1__S7__Ip=172.168.0.9
LONGHUA_CONFIG_DIR=/opt/longhua/config
```

### appsettings.json 主要项

| 键 | 含义 |
|---|---|
| `Collector:Mode` | `collection` / `phase`（也可用 CLI 第一参数） |
| `Collector:DataRoot` | 数据根目录 |
| `Collector:FileSink:FileName` | MQTT 文件名（默认 `mqtt`） |
| `Collector:FileSink:PlcFileName` | PLC dump 文件名（默认 `plc`） |
| `Collector:Phase:*` | phase 默认 input / point-table / block / blockLength |
| `Collector:Sources[]` | `Protocol`=`Mqtt`\|`S7`，`Enabled` 开关 |

MQTT：`Host` / `Port` / `ClientId` / `Subscriptions`。

S7：

| 键 | 含义 |
|---|---|
| `S7:Ip` / `Rack` / `Slot` | PLC 连接 |
| `Polls[].Db` / `Start` / `Length` | 读 DB3；**494**（工位 1001–1038） |
| `Polls[].IntervalMs` | 轮询间隔 |
| `Polls[].PointTable` | 点表文件名（phase / 文档用；collection dump 按 Length 整块写） |

### plc-conveyor.json

- `plc.length` / `plc.readLength` = **494**  
- `types.ModuleState` / `RollerState`：字段偏移  
- `stations[]`：38 个工位 **1001–1038**

## 运行

需要 **.NET 6 SDK**（`global.json` 为 `6.0.203`，可 rollForward）。

```bat
cd Code
dotnet build src\Longhua.Collector\Longhua.Collector.csproj -c Release

rem 采集（默认）
dotnet run --project src\Longhua.Collector\Longhua.Collector.csproj -- collection

rem 解析 dump
dotnet run --project src\Longhua.Collector\Longhua.Collector.csproj -- phase ^
  --input=..\..\collected-datas\test2.txt ^
  --block-length=494 ^
  --point-table=config\plc-conveyor.json ^
  --output=.\data\test2.phased.jsonl
```

（历史 `test2.txt` 若每帧 514 字节，把 `--block-length` 改为 `514`。）
发布给 Windows 现场：

```bat
dotnet publish src\Longhua.Collector\Longhua.Collector.csproj -c Release -r win-x64 --self-contained false -o .\publish-win
```

拷贝整个 `publish-win`。不要把 Linux 的 `bin` 直接给 Windows 当 exe 用；可临时 `dotnet Longhua.Collector.dll`。

## 落盘

```
data/{yyyy-MM-dd}/
  mqtt.jsonl
  plc.txt
```

phase 输出示例字段：`frameTimestamp`、`stationId`、`dataType`、`offset`、`rawHex`、`data`。

## 测试

```bash
cd Code
dotnet test
```

## 相关文档

| 文档 | 内容 |
|---|---|
| [`Doc/Architecture.md`](../Doc/Architecture.md) | 架构、双模式、点表约定 |
| [`Doc/UserGuide.md`](../Doc/UserGuide.md) | 现场使用说明 |
| [`Doc/上药龙华总院-WCS-SLS-STD-MQTT协议v4.3.pdf`](../Doc/上药龙华总院-WCS-SLS-STD-MQTT协议v4.3.pdf) | MQTT 协议 |
