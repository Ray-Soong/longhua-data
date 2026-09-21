# 龙华数据采集架构

C# 采集进程。被动订阅华章 SLS600 MQTT（协议 v4.3），并用 Sharp7 轮询库外输送线 PLC（`ConveyorInfor` DB3）。协议差异停在适配器。

本程序是**只读采集器**，不是 WCS：不向 Broker 发布 `TASK-ASSIGN` 等控制报文。

支持两种运行模式：

| 模式 | 作用 |
|---|---|
| **`collection`**（默认） | 实时采 MQTT + PLC，**分两个文件**落盘 |
| **`phase`** | 离线解析 PLC dump（如 `test2.txt`），按点表切工位解码 |

依据：`Doc/Requirements.md`、`Doc/上药龙华总院-WCS-SLS-STD-MQTT协议v4.3.pdf`、点表 `config/plc-conveyor.json`。业务侧数据说明见 Unity 工程 `LongHua/Doc/Requirements/Data.md`。

---

## 1. 设计原则

MQTT 与 S7 在现场不是同一种东西：

| | 立库 MQTT | 库外 S7 |
|---|---|---|
| 触发 | Broker 推送 | 按间隔读 DB |
| 身份 | Topic 五段 | IP + DB + 偏移 + 长度 |
| 载荷 | JSON | 字节块 + 点表 |
| 落盘（collection） | `mqtt.jsonl` | `plc.txt`（`[[longhua]]` 块） |
| 快照 | Retain 最后一条状态 | 最近一次轮询结果 |

正确切法：**两套 Source，分文件落盘；解码靠点表，不写死偏移。**

约束：

- MQTT 回调线程禁止写盘；S7 轮询线程只写独立 PLC dump（或投递队列），规范化写盘走消费者。
- 不要把 S7 字节伪装成 Topic 再进 `MqttSource`。
- Payload 保持设备原结构：不把立库 JSON 硬折成 S7 DB，也不把 S7 字节硬折成立库 Topic。
- Unity 模型当前只关心工位 **1001–1038（38 点）**，读取长度 **494** 字节。

---

## 2. 运行模式与分层

```
CLI: collection | phase
        │
        ├─ collection ──────────────────────────────────────────┐
        │     SLS600 MQTT Broker          输送线 PLC DB3         │
        │            │                         │                 │
        │      MqttDataSource            S7DataSource (Sharp7)   │
        │            │                         │                 │
        │            ▼                         ▼                 │
        │     Channel → 规范化 → mqtt.jsonl   plc.txt (dump)     │
        │                                                        │
        └─ phase ────────────────────────────────────────────────┤
              PlcDumpParser + StationDecoder + plc-conveyor.json │
              → *.phased.jsonl（每工位一行）                      │
```

| 层 | 职责 |
|---|---|
| Host / CLI | 解析 `collection`/`phase`；collection 拉起 Worker；phase 一次性跑完退出 |
| Source | `MqttDataSource`、`S7DataSource`（Sharp7 串行读） |
| 点表 | `plc-conveyor.json`：`types` + `stations[]`（id/type/offset/length） |
| Pipeline | Topic Catalog、规范化、死信（MQTT） |
| Persistence | MQTT → JSONL；PLC collection → dump 文本；phase → 解码 JSONL |

---

## 3. MQTT 采集

### 3.1 Topic 规则（协议 v4.3）

```
modelName / majorVersion / HUAZH / serialNumber / suffix
```

| 项 | 选择 |
|---|---|
| 订阅 | 配置里按实例 Topic 列表；亦可宽订 `+/+/HUAZH/#` |
| 订阅 QoS | 1 |
| 会话 | 稳定 `ClientId`，`Clean Session = false` |
| Retain | 规范化时打 `IsRetained` |
| 连接 | 长连接，断线指数退避；回调只投递 Channel |

### 3.2 设备型号与接口

见协议 PDF 与 `catalog.json`。型号包括 `SLS600`、`SLS600-FLOOR`、`SLS600-CONVEYOR-INTERFACE`、`SLS600-GOOD-LIFT`、`SLS600-TEMPORARY`、`SLS600-RGV`、`SLS600-RGV-LIFT` 等。

必采：立库上报的状态 / 扫码 / 回调 / 心跳 / 任务跟踪；WCS 下发旁听以保证任务链完整。

现场 serial 可能与协议示例不同（如 `W1-F1`、`W1-C-T2FL1`），以 `appsettings.json` 订阅为准。

---

## 4. S7 / 库外输送线

### 4.1 设备与关注范围

| 项 | 值 |
|---|---|
| 块 | `ConveyorInfor` **DB3** |
| PLC | 如 `172.168.0.9`（见配置） |
| 模型工位 | **1001–1038**，共 38 个 |
| 读取长度 | **494** 字节（`plc.length` / `plc.readLength` / `Polls[].Length`） |

工位类型：

| 类型 | 含义 | 长度 |
|---|---|---|
| `ModuleState` | 移栽工位 | 16 bytes |
| `RollerState` | 直线滚筒工位 | 10 bytes |

字段偏移见点表 `types`（与 TIA 图一致）。

### 4.2 collection：实时采 PLC

- `S7DataSource` 用 Sharp7 连接，按 `Polls[]` 读 DB。
- 每轮写成 `[[longhua]]` / 时间 / id / `DB3.0` / 空格分隔字节（与 `test2.txt` 同类）。
- 文件：`data/{yyyy-MM-dd}/{PlcFileName}.txt`（默认 `plc.txt`）。
- 连不上 PLC 时重试打日志，**不阻断 MQTT**。

### 4.3 phase：离线解析

- 读 dump 文件，按 `--block-length`（默认点表 `readLength`/配置 `Phase:BlockLength`）校验每帧长度。
- 只解点表中 `enabled` 工位；工位越界则跳过并告警。
- 输出 JSONL：每行含 `frameTimestamp`、`stationId`、`dataType`、`offset`、`rawHex`、`data`（字段字典）。

---

## 5. 统一记录（MQTT）

MQTT 规范化后仍用 `TelemetryRecord` 信封落 `mqtt.jsonl`：

| 字段 | 作用 |
|---|---|
| `linkId` | 本次 MQTT 连接会话 |
| `dataType` / `name` | 回放分类与实例 |
| `timestamp` | 报文时间 |
| `data` | 原文 JSON |

PLC **collection** 阶段先保留原始 dump；**phase** 产出按工位解码后的 JSON，供 Unity/回放消费。二者时间轴可对齐，不强制同一文件。

---

## 6. 文件落盘约定

### collection

```
data/{yyyy-MM-dd}/
  mqtt.jsonl          ← FileSink.FileName（默认 mqtt）
  plc.txt             ← FileSink.PlcFileName（默认 plc）
  raw/...             ← WriteRaw=true 时
  dead-letter/...     ← WriteDeadLetter=true 时
```

MQTT 行示例：

```json
{"linkId":"...","dataType":"Rgv","name":"Rgv1","timestamp":"...","data":{...}}
```

PLC 块示例：

```
[[longhua]]
2026/9/18 16:36:47
639253462074758532
DB3.0
19 0 0 0 ...
```

### phase

```
某路径/xxx.phased.jsonl
```

---

## 7. 配置要点

| 文件 | 作用 |
|---|---|
| `config/appsettings.json` | Mode、Broker、S7 IP、Poll 长度、双文件名、Phase 默认参数 |
| `config/catalog.json` | MQTT 型号 / EventKind |
| `config/plc-conveyor.json` | PLC 点表（types + stations 1001–1038） |
| `appsettings.Local.json` | 现场覆盖（勿提交） |

S7 `Polls[].Length` / 点表 `length`/`readLength`：**494**。历史 dump（如早期 `test2.txt`）若整帧仍是 514，phase 时加 `--block-length=514`，点表仍只解 1001–1038。

---

## 8. 技术选型

| 项 | 选择 |
|---|---|
| 运行时 | .NET 6 Worker（`global.json` 钉 6.0.203，可 rollForward） |
| MQTT | MQTTnet |
| S7 | **Sharp7** |
| 配置 | appsettings + catalog + plc-conveyor |
| 可视化 | 与 Unity（LongHua）分离；Unity 读落盘/解码结果回放 |

---

## 9. 分期状态（更新）

| 阶段 | 内容 | 状态 |
|---|---|---|
| MQTT 采集 + JSONL | 已交付 | 完成 |
| S7 实时 dump（collection） | Sharp7 + 独立 `plc.txt` | **已实现** |
| S7 点表解码（phase） | dump → 工位 JSONL | **已实现** |
| 统一信封合并 / 变化检测 / 数据库 | 可选后续 | 未做 |

---

## 10. 接入前核对

1. Broker 地址、端口、TLS、账号、唯一 `ClientId`。
2. 现场 Topic serial 是否与 `Subscriptions` 一致。
3. PLC IP / Rack / Slot / DB3；点表与 TIA 是否一致（尤其 1001–1015）。
4. collection / phase 默认块长均为 **494**；解析旧 514 字节 dump 时再显式传 `--block-length=514`。
5. 数据目录与 `KeepDays`。
