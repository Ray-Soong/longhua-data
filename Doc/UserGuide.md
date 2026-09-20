# 龙华立库数据采集 — 使用说明

你拿到的是已经编译好的程序文件夹（例如 `net6.0` 或 `publish-win`）。**请整个文件夹一起拷贝**，不要只拷里面的 `.exe`。

本程序**只读**采集：旁听立库 MQTT，并可选轮询库外 PLC。**不会**向设备下发任务。

支持两种用法：

| 模式 | 做什么 |
|---|---|
| **collection**（默认） | 同时采 MQTT + PLC，写成**两个文件** |
| **phase** | 不连现场，解析已有的 PLC dump（如 `test2.txt`） |

---

## 1. 文件夹里有什么

```
（本程序文件夹）
  Longhua.Collector.exe     ← 启动采集（或用 dotnet 跑 dll）
  config\
    appsettings.json        ← Broker / PLC IP / 文件名（必看）
    catalog.json            ← MQTT 型号映射（一般不用改）
    plc-conveyor.json       ← PLC 工位点表（1001–1038）
    appsettings.Local.json.example
  使用说明.md               ← 本说明
  data\                     ← 运行后自动出现
```

其它 `.dll`、`.json`、`.pdb` 都是运行需要的，不要删。

---

## 2. 用户电脑需要什么

- Windows 10 / 11（或已装 .NET 6 的其它环境）
- **.NET 6 运行时**（Desktop Runtime 或 Runtime）  
  https://dotnet.microsoft.com/zh-cn/download/dotnet/6.0  
  选 **.NET Desktop Runtime 6.0**（x64）
- 能访问立库 MQTT（默认 **1883**）
- 若启用 PLC 采集：能访问 PLC（默认 **102**，如 `172.168.0.9`）

双击 exe 窗口一闪就关：多半没装运行时。在本文件夹打开 PowerShell：

```
.\Longhua.Collector.exe
```

或：

```
dotnet Longhua.Collector.dll
```

---

## 3. 第一次使用 — collection（采集）

### 第 1 步：放到固定位置

例如 `D:\龙华采集\`。数据写在本文件夹下的 `data\`。

### 第 2 步：改配置

打开 `config\appsettings.json`。

**MQTT（必改 Host）：**

```json
"Mqtt": {
  "Host": "现场BrokerIP",
  "Port": 1883,
  "ClientId": "longhua-collector-1",
  ...
}
```

| 项 | 说明 |
|---|---|
| `Host` | 立库 MQTT IP，不要用 `127.0.0.1`（除非本机测试） |
| `ClientId` | **每台电脑不同** |
| `Username` / `Password` | 有则填，无则 `""` |

**PLC（按现场改）：**

```json
"S7": {
  "Ip": "172.168.0.9",
  "Port": 102,
  "Rack": 0,
  "Slot": 1,
  "Polls": [
    {
      "Db": 3,
      "Start": 0,
      "Length": 494,
      "IntervalMs": 200,
      "PointTable": "plc-conveyor.json"
    }
  ]
}
```

| 项 | 说明 |
|---|---|
| `Enabled` | `true` 采 PLC；暂时只采 MQTT 可改 `false` |
| `Length` | 模型关注 **494**（工位 1001–1038）；若要对齐旧 dump 可读 **514** |
| `PlcFileName` / `FileName` | 默认 PLC 文件名 `plc`，MQTT 文件名 `mqtt` |

密码不想写进主配置：复制 `appsettings.Local.json.example` → `appsettings.Local.json`，只填覆盖项。

### 第 3 步：启动

双击 exe，或：

```
.\Longhua.Collector.exe collection
```

（省略参数时默认也是 collection。）

看到类似输出即正常：

```
模式 collection，配置目录 ...\config，数据目录 ...\data，MQTT文件=mqtt PLC文件=plc
MQTT 已连接 ...
S7 采集已启动 ...
```

**不要关窗口。** 停止：`Ctrl+C` 或关窗口。

PLC 连不上时会打警告并重试，**MQTT 仍可继续采**。

---

## 4. 数据在哪里（collection）

```
data\
  2026-09-16\
    mqtt.jsonl      ← 当天 MQTT（一行一条 JSON）
    plc.txt         ← 当天 PLC dump（[[longhua]] 块）
```

### mqtt.jsonl

```json
{"linkId":"...","dataType":"Rgv","name":"Rgv1","timestamp":"...","data":{...}}
```

- `linkId`：本次 MQTT 连接 ID，重连会变  
- `dataType` / `name`：类型与实例  
- `timestamp`：报文时间  
- `data`：原文 JSON  

### plc.txt

与现场 `test2.txt` 同类：

```
[[longhua]]
2026/9/18 16:36:47
（帧id）
DB3.0
19 0 0 0 ...（空格分隔字节）
```

默认保留 **30 天**（`KeepDays`）。

---

## 5. phase — 解析 PLC dump

不连 Broker / PLC，只把已有 dump 按点表解开。

```
.\Longhua.Collector.exe phase ^
  --input=D:\data\test2.txt ^
  --block-length=514 ^
  --point-table=config\plc-conveyor.json ^
  --output=D:\data\test2.phased.jsonl
```

| 参数 | 含义 |
|---|---|
| `--input` / `-i` | dump 文件路径 |
| `--block-length` / `-b` | 每块原始字节数（`test2.txt` 用 **514**） |
| `--point-table` / `-p` | 点表，默认 `config/plc-conveyor.json` |
| `--output` / `-o` | 输出 JSONL；省略则在 input 旁生成 `*.phased.jsonl` |

点表只解 **enabled** 工位（默认 1001–1038）。输出每一行一个工位字段对象，含 `stationId`、`dataType`、`rawHex`、`data` 等。

也可用配置 `Collector:Phase:*`，再执行 `phase`。

---

## 6. 怎样判断在正常采集

1. 窗口还在，出现过 `MQTT 已连接`。  
2. `data\当天\mqtt.jsonl` 在变大。  
3. 若启用了 S7：`plc.txt` 在变大，或日志里 S7 Received 在增加。  
4. 约每 30 秒有 `采集统计`，`Connected=True`，`Received` 增大。

---

## 7. 常见问题

**没装 .NET / 窗口一闪就没**  
见第 2 节；用 PowerShell 跑看报错。

**「不是有效应用程序」**  
多半是 Linux 编译的 exe。应在 Windows 上：

```
dotnet publish src\Longhua.Collector\Longhua.Collector.csproj -c Release -r win-x64 --self-contained false -o .\publish-win
```

或直接：`dotnet Longhua.Collector.dll`。

**MQTT 连接失败**  
Host/Port、网络、防火墙 1883；先 ping Broker。

**mqtt.jsonl 不增长**  
Topic 与现场不一致；或只有 Received 增加、Events 不增加 → 解析失败（缺 timestamp 等）。

**plc.txt 不增长**  
S7 `Enabled` 是否为 true；IP/Rack/Slot/DB 是否对；防火墙 102；日志里是否有「S7 轮询失败」。

**两台电脑互踢**  
`ClientId` 重复。

**phase 报块长度不符**  
`--block-length` 要与 dump 每帧字节数一致（`test2` 为 514）。

**改了配置没生效**  
先停程序再改，保存后重启。

**找不到 config**  
必须在完整程序文件夹里启动，或设置环境变量 `LONGHUA_CONFIG_DIR`。

---

## 8. 不要做的事

- 不要只拷贝一个 exe  
- 不要删同目录 dll / config  
- 不要关正在采集的窗口  
- 不要两台机器同一 `ClientId`  
- 不要用本程序给立库下发任务  

---

## 9. 日常备忘

| 目的 | 做法 |
|---|---|
| 开始采集 | `Longhua.Collector.exe` 或 `… collection` |
| 停止采集 | `Ctrl+C` |
| 改 MQTT/PLC 地址 | 改 `config\appsettings.json`，重启 |
| 看数据 | `data\当天日期\mqtt.jsonl` 与 `plc.txt` |
| 解析旧 PLC 文件 | `… phase --input=... --block-length=514` |
| 换电脑 | 拷整个文件夹，换新 `ClientId` |
