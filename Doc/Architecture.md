# 龙华数据采集架构

C# 采集进程。一期被动订阅华章 SLS600 MQTT（协议 v4.3），将数据写入文件。二期接入 S7。协议差异停在适配器，内核只认统一记录。

本程序是**只读采集器**，不是 WCS：不向 Broker 发布 `TASK-ASSIGN` 等控制报文。任务新增 / 修改 / 取消 / 删除仍由华章 WCS 发布；采集器旁听相关 Topic（含 WCS 下发与立库上报），保证任务链完整可回放。

依据：`doc/Requirements.md`、`doc/上药龙华总院-WCS-SLS-STD-MQTT协议v4.3.pdf`。

---

## 1. 设计原则

MQTT 与 S7 在现场不是同一种东西，不能做成「先写 MQTT 落盘，以后再抄一份 S7」：

| | 立库 MQTT（一期） | S7（二期） |
|---|---|---|
| 触发 | Broker 推送 | 按间隔读 DB |
| 身份 | Topic 五段 | IP + DB + 偏移 + 长度 |
| 载荷 | JSON | 字节块 + 点表 |
| 快照 | Retain 最后一条状态 | 最近一次轮询结果 |

正确切法：**两套 Source，一套记录，一套 Sink。** 文件格式、时间戳、设备主键保持同一契约，回放和对账不因协议分叉。

约束：

- MQTT 回调线程和 S7 读线程禁止写盘。写盘是独立消费者。
- 不要把 S7 字节伪装成 Topic 再进 `MqttSource`。
- 不要在 File Sink 里写 `if (mqtt) / if (s7)`。协议分叉只允许出现在 Source 和 Catalog。
- Payload 保持设备原结构：不把立库 JSON 硬折成 S7 DB，也不把 S7 字节硬折成立库 Topic。

---

## 2. 运行时分层

```
现场          SLS600 MQTT Broker          未来 S7 PLC
                 │                            │
Source       MqttSource                   S7Source（预留）
                 │                            │
                 └────────────┬───────────────┘
内核                     RawFrame 队列
                              │
                        Catalog 规范化
                              │
                      TelemetryRecord 队列
                              │
Sink              File JSONL          未来 Database
```

四层职责：

1. **Host**：.NET Worker。读配置、拉起多个 Source / Sink、处理退出。
2. **Source**：把现场数据变成 `RawFrame`。一期只有 `MqttSource`；`S7Source` 先留接口和配置位。唯一允许出现 MQTTnet / Sharp7 的层。
3. **内核**：有界 Channel、Topic/地址解析、设备目录映射、死信。与协议无关。
4. **Sink**：一期只写 JSONL。以后加库时 Source 不动。一条记录可以扇出到多个 Sink。

必须稳定的接口（设计约束，非实现）：

| 接口 | 职责 |
|---|---|
| `IDataSource` | `Start` / `Stop`。内部把现场数据变成 `RawFrame` 写入同一 Channel。Host 按配置实例化多个 Source，MQTT 与 S7 可同时开。 |
| `ITelemetrySink` | 消费 `TelemetryRecord`。一期 FileSink；二期以后加数据库 Sink 而不改 Source。 |

建议工程划分：

| 模块 | 内容 |
|---|---|
| Host | Worker、配置、日志 |
| Abstractions | `IDataSource`、`ITelemetrySink`、`RawFrame`、`TelemetryRecord` |
| Mqtt | MQTTnet、订阅、重连 |
| S7 | 二期再填；Sharp7 连接级串行读 |
| Pipeline | 规范化、Catalog、死信 |
| Persistence.File | JSONL 按日追加 |

---

## 3. 一期 MQTT 采集

### 3.1 Topic 规则（协议 v4.3）

```
modelName / majorVersion / HUAZH / serialNumber / suffix
```

suffix 可以含斜杠，例如 `TASK/STATE`。订阅不要为每台设备写死 serial。

| 项 | 选择 |
|---|---|
| 订阅 | `+/+/HUAZH/#` |
| 订阅 QoS | 1（任务、扫码尽量不丢；状态类发布方多为 QoS 0，订阅端用 1 不会把对方抬上去） |
| 会话 | 稳定 `ClientId`，`Clean Session = false`，断线后接 QoS1 补发（Broker 需支持持久会话） |
| Retain | 重连会先收到最后一条 `STATE` / `HEALTH`。规范化时打 `IsRetained`，回放不要当成一次真实跳变 |
| 遗嘱 | `HEALTH` 用来判断离线；采集器再做「超时未收到」的 `Quality=Stale` |
| 连接 | 长连接，断线指数退避。回调只投递 Channel，不做磁盘 IO |

### 3.2 设备型号

| modelName | 设备 | serial 规则示例 | 主要 suffix |
|---|---|---|---|
| `SLS600` | 整库 / 巷道 | `W1` | `TASK-*` · `TASK/STATE` · `STATE` · `HEALTH` |
| `SLS600-FLOOR` | 存储层 | `W1F2` | `STATE` |
| `SLS600-CONVEYOR-INTERFACE` | 接口线体 | `CW1T2FL1` | `SCAN` · `STATE` |
| `SLS600-GOOD-LIFT` | 货物提升机 | `GLW1T2FL1` | `STATE` |
| `SLS600-TEMPORARY` | 暂存台 | `TEMPW1T2FL1` | `STATE` |
| `SLS600-RGV` | 穿梭车 | `RGVW1SN1` | `STATE` |
| `SLS600-RGV-LIFT` | 换层提升机 | `RLW1T2F1` | `STATE` |

设备编码以协议附录为准，现场可能按项目调整，Catalog 必须可配置。

### 3.3 接口采集清单

来源：上药龙华总院 WCS-SLS-STD MQTT 协议 v4.3。3.2 回调 Topic 正文被截断，按命名对称记为 `TASK-ASSIGN-CALLBACK`，接入前用 Broker 实包核对。

| 节 | 接口 | 发布方 | QoS | Retain | 采集策略 |
|---|---|---|---|---|---|
| 3.1 | `TASK-ASSIGN` 任务新增 | WCS | 1 | 否 | 旁听 |
| 3.2 | `TASK-ASSIGN-CALLBACK` | 立库 | 1 | 否 | 必采 |
| 3.3 | `TASK-MODIFY` 任务修改 | WCS | 1 | 否 | 旁听 |
| 3.4 | `TASK-MODIFY-CALLBACK` | 立库 | 1 | 否 | 必采 |
| 3.5 | `TASK-CANCEL` 任务取消 | WCS | 1 | 否 | 旁听 |
| 3.6 | `TASK-CANCEL-CALLBACK` | 立库 | 1 | 否 | 必采 |
| 3.7 | `TASK-DELETE` 任务删除 | WCS | 1 | 否 | 旁听 |
| 3.8 | `TASK-DELETE-CALLBACK` | 立库 | 1 | 否 | 必采 |
| 3.9 | `TASK/STATE` 任务跟踪 | 立库 | 1 | 否 | 必采 |
| 3.10 | `STATE` 立库/巷道 | 立库 | 0 | 是 | 必采 |
| 3.11 | `STATE` 存储层 | 立库 | 0 | 是 | 必采 |
| 3.12 | `SCAN` 入库扫码 | 立库 | 1 | 否 | 必采 |
| 3.13 | `STATE` 接口线体 | 立库 | 0 | 是 | 必采 |
| 3.14 | `STATE` 货物提升机 | 立库 | 0 | 是 | 必采 |
| 3.15 | `STATE` 暂存台 | 立库 | 0 | 是 | 必采 |
| 3.16 | `STATE` RGV | 立库 | 0 | 是 | 必采 |
| 3.17 | `STATE` 换层提升机 | 立库 | 0 | 是 | 必采 |
| 3.18 | `HEALTH` 心跳/遗嘱 | 立库 | 1 | 是 | 必采 |
| 3.19 | `SCAN` 流利库回库口 | 立库 | 1 | 否 | 必采 |

立库上报（状态、扫码、回调、心跳、任务跟踪）必采；WCS 下发一并旁听，否则回放看不到完整任务链。

### 3.4 事件种类映射

| EventKind | 判定 | 备注 |
|---|---|---|
| Command | suffix 为 `TASK-ASSIGN` / `MODIFY` / `CANCEL` / `DELETE` | WCS → 立库 |
| Callback | suffix 以 `-CALLBACK` 结尾 | QoS1，可能重复 |
| TaskState | suffix = `TASK/STATE` | 任务跟踪主序列 |
| DeviceState | suffix = `STATE` | 高频；Retain 快照需标记 |
| Scan | suffix = `SCAN` | 入库/回库触发点 |
| Health | suffix = `HEALTH` | 配合遗嘱判断离线 |

---

## 4. 统一记录 TelemetryRecord

MQTT JSON 与未来 S7 解码结果都进入同一信封。内核只认这一种记录。

| 字段 | 来源 | 作用 |
|---|---|---|
| `RecordId` | 采集器生成 | 排障与可选去重 |
| `ReceivedAtUtc` | 采集器时钟 | 落盘与排序主时间 |
| `SourceTimestamp` | 报文 `timestamp` 或 S7 轮询时刻 | 对照现场时钟、估延迟 |
| `Protocol` | `Mqtt` \| `S7` | 回放时选择解码器 |
| `SourceId` | 配置 | 哪一条采集源 |
| `DeviceType` | Catalog | Warehouse / Floor / Rgv / …（内部台账） |
| `DeviceId` | Topic serial 或 S7 映射 | 台账主键 |
| `LinkId` | MQTT 连接成功时生成，重连/重启后更换 | 区分同一次连接会话 |
| `DataType` | 订阅配置 `Type` | 回放分类：`TaskAssign` / `TaskState` / `Rgv` / … |
| `Name` | 订阅配置 `Name` | 回放实例：`Rgv1`、`Floor1State` 等 |
| `Identity` | Topic 全文或 `s7://ip/db/off/len` | 原始寻址，不丢失 |
| `EventKind` | Catalog | Command / State / Scan / … |
| `IsRetained` | MQTT retain | 重连快照 vs 真实变化 |
| `Quality` | Good / Stale / ParseError | 心跳超时、解码失败 |
| `Payload` | JSON 对象或 S7 解码对象 | 业务字段原样保留 |

解析 Topic 五段：`model` / `version` / `manufacturer` / `serial` / `suffix`。无法解析的报文进 dead-letter，不阻断主路径。

---

## 5. 文件落盘

一期落盘格式为 **JSONL 追加**，禁止写成一个巨大 JSON 数组（崩溃只会丢最后半行）。

### 5.1 落盘信封（回放主文件）

当天全部规范化记录写入 **一个** JSONL：`data/{yyyy-MM-dd}/collect.jsonl`。每一行是回放信封：

```json
{"linkId":"3f2a9c1b8e0d47a1b4c55e6f708192a3","dataType":"Rgv","name":"Rgv1","timestamp":"2017-04-15T11:40:03.12Z","data":{"warehouseNo":"1","carNo":1,"status":"1"}}
```

| 字段 | 来源 | 回放用法 |
|---|---|---|
| `linkId` | MQTT 连接成功时生成的会话 ID | 判断是否同一次连接；重启/重连后变化 |
| `dataType` | 配置 `Subscriptions[].Type` | 按类型过滤（`Rgv`、`TaskAssign`） |
| `name` | 配置 `Subscriptions[].Name` | 按实例过滤（`Rgv1`、`Rgv2`） |
| `timestamp` | 报文 `timestamp`，缺省用接收时刻 | 按时间排序、按时码推进 |
| `data` | MQTT / 未来 S7 原文 JSON | 业务内容原样重放 |

`raw/`、`dead-letter/` 默认关闭；打开后仍按日分子目录，供协议对账。

### 5.2 目录约定

```
data/{yyyy-MM-dd}/
  collect.jsonl
  raw/mqtt-sls600.jsonl          ← WriteRaw=true 时
  dead-letter/parse-error.jsonl  ← WriteDeadLetter=true 时
```

S7 接入后仍写同一 `collect.jsonl`，`linkId` 为该次 S7 连接会话，`dataType`/`name` 用点表映射，`data` 为解码后的 JSON。

### 5.3 写入策略

| 项 | 选择 | 原因 |
|---|---|---|
| 格式 | JSONL 追加 | 崩溃只丢最后半行 |
| 切分 | 按自然日 + 可选按体积 | 现场按班次取数 |
| flush | 定时 + 批量；任务/扫码可更积极 | 状态 QoS0 可稍攒批 |
| 去重 | 可选：`Identity` + `SourceTimestamp` + payload hash | QoS1 会重复；默认先原样落盘 |
| 积压 | 有界 Channel；任务/扫码优先，状态可降采样告警 | 磁盘慢时不堵 MQTT 回调 |

---

## 6. S7 扩展

配置允许同时挂多条 Source。一期 MQTT `Enabled=true`，S7 可以先写条目但关掉。新增产线只加 Source 条目，不改管道。

| 键 | 一期 MQTT | 二期 S7 |
|---|---|---|
| `Sources[].Protocol` | `Mqtt` | `S7` |
| `Sources[].Enabled` | `true` | `false` → `true` |
| 连接 | Broker / ClientId / 用户名 | Ip / Rack / Slot |
| 采集描述 | `Subscriptions[]` | `Polls[]`：DB、偏移、长度、间隔、点表 |
| Catalog | Topic 模式 → `DeviceType` | 地址 → 同一套 `DeviceType` |

S7 适配器内部：

- 一台 PLC 一个客户端，读操作串行（Sharp7 的 `S7Client` 不能多线程共用）。
- 轮询完成后组 `RawFrame`；变化检测后再写入 `collect.jsonl`（否则周期快照会撑爆文件）。`raw/` 是否每轮都写由配置决定。
- 解码规则放点表/配置，不写死在管道里。
- 若以后同一台设备两种协议都能读，Catalog 主键仍对齐到同一 `DeviceType` + `DeviceId`。

---

## 7. 技术选型

| 项 | 选择 |
|---|---|
| 运行时 | .NET 6 Worker（`global.json` 钉 6.0.203，兼容 VS 2019 / MSBuild 16.11），可装 Windows 服务 |
| MQTT | MQTTnet |
| S7 | Sharp7 或 S7.Net Plus（二期） |
| 配置 | `appsettings` + 设备目录；热路径不改代码 |
| 可视化 | 采集与 Unity 进程分离；需要回放时再读文件或以后的库 |

一期交付边界：

| 做 | 不做 |
|---|---|
| 订阅并落盘立库 MQTT 全量相关报文（原始 + 规范化） | 发布 `TASK-ASSIGN` / `MODIFY` / `CANCEL` / `DELETE` |
| 连接、心跳、解析失败、积压的可观察性 | WCS 业务校验、储位分配、波次逻辑 |
| 配置驱动的 Source / Sink 开关 | 把采集器嵌进 Unity 可视化进程 |

---

## 8. 分期

| 阶段 | 内容 | 验收 |
|---|---|---|
| 一期 | Worker + MQTT Source + 规范化 + 按日 `collect.jsonl` | Broker 上已订 Topic 均可按 `dataType`/`name` + `timestamp` 回放，并用 `linkId` 划分连接会话 |
| 二期 | S7 Source + 点表解码 + 变化检测 | 同一 `collect.jsonl` 出现 S7 的信封，回放工具无需分叉 |
| 可选三期 | 数据库 Sink / 对接现有可视化回放 | 文件仍是权威原始层，库是查询层 |

---

## 9. 接入前核对

1. Broker 地址、端口、是否 TLS、账号、ClientId 规范。
2. 现场 `WAREHOUSE-SN` 等实际编码是否按协议附录（`W1`、`RGVW1SN1` 等）。
3. `TASK-ASSIGN-CALLBACK` 的真实 Topic 字符串。
4. 文件根目录、保留天数、是否按班次切割。
5. 二期 S7 是「立库以外的线体/堆垛机」，还是同一立库的第二条通道——Catalog 主键策略取决于这个。
