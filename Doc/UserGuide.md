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
| `Length` | **494**（工位 1001–1038） |
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

### 5.1 先进入程序目录

`phase` 必须在**含有 exe 与 `config\` 的目录**里执行（例如 `...\bin\Debug\net6.0` 或 `publish-win`）。  
相对路径 `config\plc-conveyor.json` 是相对这个目录解析的。

```powershell
cd D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\net6.0
```

### 5.2 命令示例（PowerShell 单行）

```powershell
.\Longhua.Collector.exe phase --input=D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\test2.txt --block-length=494 --point-table=config\plc-conveyor.json --output=D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\test2.phased.jsonl
```

> 若 dump 是旧文件且每帧仍是 **514** 字节（如早期 `test2.txt`），把 `--block-length` 改成 `514`。新采集的 `plc.txt` 为 **494**。

| 参数 | 含义 |
|---|---|
| `--input` / `-i` | dump 文件路径（可用绝对路径，文件可放在程序目录外） |
| `--block-length` / `-b` | 每块原始字节数（新采集 **494**；旧 `test2.txt` 用 **514**） |
| `--point-table` / `-p` | 点表；写 `config\plc-conveyor.json` 时表示本目录下的 `config\` |
| `--output` / `-o` | 输出 JSONL；省略则在 input 旁生成 `*.phased.jsonl` |

也可用配置 `Collector:Phase:*`，再执行 `phase`。

### 5.3 怎样算成功

日志类似：

```
phase Input=... BlockLength=494 PointTable=...\config\plc-conveyor.json Output=...
phase 完成 Input=... Frames=... Rows=... Stations=38 BlockLength=494 Output=...
```

| 字段 | 含义 |
|---|---|
| `Frames` | dump 里读到的 PLC 帧数 |
| `Stations` | 点表里 **enabled=true** 的工位数（完整点表应为 **38**） |
| `Rows` | 写出行数，应满足 **Rows ≈ Frames × Stations**（例如 `Frames×38`） |

工位范围由 **`config\plc-conveyor.json` 决定**，不是写死在程序里：

- 完整模型点表：工位 **1001–1038**，`Stations` 应为 **38**，`Rows = Frames × 38`
- 若日志里是 `Stations=23`，多半是旧点表（只有 **1016–1038**）。请确认本目录 `config\plc-conveyor.json` 已更新，或重新编译/拷贝配置后再跑

输出每一行一个工位，字段含 `stationId`、`dataType`、`offset`、`rawHex`、`data` 等。

### 5.4 注意

- 改完源码或 `Doc\UserGuide.md` 后要**重新编译**，输出目录里的 `使用说明.md` / `config\` 才会更新；不要只看旧的 `bin\Debug\net6.0\使用说明.md`。
- `input` / `output` 建议用绝对路径，避免当前目录搞错。

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
`--block-length` 要与 dump 每帧字节数一致：新采集 **494**；旧 `test2` 为 **514**。

**phase 已完成但 Stations 不是 38**  
看日志 `Stations=`：等于点表里 enabled 工位数。若是 `23`，通常是旧 `plc-conveyor.json`（仅 1016–1038）。把仓库里最新点表拷进本目录 `config\`，或重新 `dotnet build` 后再跑。用 `Rows == Frames × Stations` 自检。

**phase 提示找不到点表 / 配置**  
先 `cd` 到含 `Longhua.Collector.exe` 与 `config\` 的目录再执行；或给 `--point-table` 写绝对路径。

**改了配置没生效**  
先停程序再改，保存后重启。phase 用的是**程序目录下**那份 `config\plc-conveyor.json`，不是源码树里未拷贝的文件。

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
| 解析旧 PLC 文件 | 先 `cd` 到程序目录；新 dump 用 `--block-length=494`，旧 514 帧 dump 用 `514` |
| 核对 phase 结果 | 看日志 `Frames` / `Stations` / `Rows`，且 `Rows ≈ Frames × Stations` |
| 换电脑 | 拷整个文件夹，换新 `ClientId` |
