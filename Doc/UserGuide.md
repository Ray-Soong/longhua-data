# 龙华立库数据采集 — 使用说明

你拿到的是已经编译好的程序文件夹（例如 `net6.0`）。**请整个文件夹一起拷贝**，不要只拷里面的 `.exe`。

本程序只从立库 MQTT 收数据并写成文件，**不会**向设备下发任务。

---

## 1. 文件夹里有什么

```
（本程序文件夹）
  Longhua.Collector.exe     ← 双击启动
  config\
    appsettings.json        ← 改 Broker 地址（必做）
    catalog.json            ← 一般不用改
    appsettings.Local.json.example
  使用说明.md               ← 本说明
  data\                     ← 运行后自动出现，采集结果在这里
```

其它 `.dll`、`.json`、`.pdb` 都是程序运行需要的，不要删。

---

## 2. 用户电脑需要什么

- Windows 10 / 11
- 已安装 **.NET 6 运行时**（Desktop Runtime 或 Runtime 均可）  
  没有的话打开：https://dotnet.microsoft.com/zh-cn/download/dotnet/6.0  
  下载 **.NET Desktop Runtime 6.0**（x64）并安装。  
  **不必**安装 Visual Studio，也**不必**安装 SDK。
- 电脑能访问立库 MQTT 服务器（默认端口 **1883**）

若双击 exe 窗口闪一下就关：多半是没装运行时。按住 `Shift` 右键本文件夹空白处 →「在此处打开 PowerShell 窗口」，输入：

```
.\Longhua.Collector.exe
```

看红色报错再处理。

---

## 3. 第一次使用（必做）

### 第 1 步：放到固定位置

把整个文件夹拷到用户电脑，例如：

```
D:\龙华采集\
```

以后不要来回挪路径。采集数据会写在这个文件夹里的 `data\`。

### 第 2 步：填写 MQTT 地址

用记事本打开：

```
本文件夹\config\appsettings.json
```

找到下面这一段，改成现场实际值：

```json
"Mqtt": {
  "Host": "127.0.0.1",
  "Port": 1883,
  "UseTls": false,
  "ClientId": "longhua-collector",
  "Username": "",
  "Password": "",
```

| 要改的项 | 说明 |
|---|---|
| `Host` | 立库 MQTT 服务器 IP，**不要用 127.0.0.1**（那是本机测试） |
| `Port` | 一般是 `1883` |
| `UseTls` | 现场要求加密再改 `true`，否则保持 `false` |
| `ClientId` | 每台电脑必须不同，例如 `longhua-collector-1`、`longhua-collector-2` |
| `Username` / `Password` | 服务器有账号就填；没有则保持 `""` |

改完后**保存**，编码保持 UTF-8。不要把文件改成「一个大 JSON 数组」，也不要删逗号、引号。

有密码又不想写在主配置里时：把 `config\appsettings.Local.json.example` 复制一份，改名为 `appsettings.Local.json`，只填 Host / 账号 / 密码。这个文件优先于 `appsettings.json`。

### 第 3 步：启动

双击 **`Longhua.Collector.exe`**。

会弹出黑色命令行窗口。看到类似下面的字就表示起来了：

```
配置目录 ...\config，数据目录 ...\data
启动 1 个采集源
MQTT 已连接 Source=mqtt-sls600 Broker=（你填的IP）:1883
Application started.
```

**不要关这个窗口。** 关掉就等于停止采集。可以把它最小化。

停止采集：在窗口里按 `Ctrl+C`，或直接关闭窗口。

---

## 4. 数据在哪里

启动成功后，本文件夹下会出现 `data\`。**同一天所有 MQTT 记录写在一个文件里**（一行一条 JSON），方便以后整文件导入数据库：

```
data\
  2026-09-16\
    collect.jsonl             ← 当天全部采集记录
```

用记事本可以打开，不要用 Word 保存。每一行结构固定为 **连接 ID + 数据类型 + 名字 + 时间戳 + 数据内容**：

```json
{"linkId":"3f2a9c1b8e0d47a1b4c55e6f708192a3","dataType":"Rgv","name":"Rgv1","timestamp":"2017-04-15T11:40:03.12Z","data":{"warehouseNo":"1","carNo":1,"status":"1"}}
```

- `linkId`：本次程序连上 MQTT 后生成的连接标识，**重启或重连后会变**
- `dataType`：数据类型（`TaskAssign`、`TaskState`、`Rgv`、`FloorState` 等）
- `name`：这一路的名字（`TaskAssign`、`Rgv1`、`Rgv2`、`Floor1State` 等）
- `timestamp`：报文里的时间，回放按它排序
- `data`：MQTT 原文 JSON

小车、任务、库状态靠 `dataType` + `name` 区分，不拆文件。`linkId` 用来判断是不是同一次连接采到的数据。

默认保留 **30 天**，更早的日期文件夹会被自动删掉。要改保留天数，在 `config\appsettings.json` 里改 `KeepDays`（例如 `90`）。改成 `0` 表示不自动删。

以后若改成写数据库，仍是同一套记录结构，只换存储。

---

## 5. 怎样判断在正常采集

1. 黑窗口还在，且出现过 `MQTT 已连接`。
2. `data\当天日期\collect.jsonl` 文件大小在增加。
3. 大约每 30 秒会打一行 `采集统计`，其中 `Connected=True`，`Received` 数字在变大。

---

## 6. 常见问题

**安装 .NET 后要不要重启电脑？**  
一般**不用重启**。关掉所有已经打开的命令行窗口，再重新双击 exe。若刚装完立刻运行仍报错，可以重启一次排除缓存，但不是必须。

**提示「不支持的程序」/「无法在你的电脑上运行」**  
这通常不是「没重启」，而是运行时装错或点错了文件。按顺序检查：

1. 启动的必须是 **`Longhua.Collector.exe`**，不要双击 `.dll`。
2. 必须安装 **.NET 6 Desktop Runtime（x64）**，和 Windows 64 位匹配。  
   不要只装 x86（32 位），也不要只装 .NET 8 / 9 却没有 6.0。  
   下载页：https://dotnet.microsoft.com/zh-cn/download/dotnet/6.0  
   选 **Run desktop apps** → **Download x64**。
3. 装完后**新开** PowerShell，执行：

```
dotnet --list-runtimes
```

列表里要有 `Microsoft.NETCore.App 6.0.x` 和 `Microsoft.WindowsDesktop.App 6.0.x`。没有就说明装错包或没装成功。

4. 在程序文件夹里运行（不要只拷走 exe）：

```
cd （程序文件夹完整路径）
.\Longhua.Collector.exe
```

把窗口里的完整英文/中文报错留下来。

**提示「不是此操作系统平台的有效应用程序」**  
`.exe` 不是 Windows 程序（常见原因：文件夹是在 Linux 上编译后拷过来的）。运行时已经装好时，**不要用 exe**，在同一目录执行：

```
dotnet Longhua.Collector.dll
```

这和双击 exe 是同一个程序。发给别人的包应在 Windows 上用下面命令重新发布后再拷贝：

```
dotnet publish src\Longhua.Collector\Longhua.Collector.csproj -c Release -r win-x64 --self-contained false -o .\publish-win
```

拷给用户的是 `publish-win` 整个文件夹，不要拷 Linux 的 `bin\Debug\net6.0`。

**窗口一闪就没了**  
没装 .NET 6 运行时，见第 2 节。或从 PowerShell 运行 exe 看报错。

**一直 `MQTT 连接失败` / Connection refused**  
`Host`、`Port` 填错；电脑和立库不在同一网络；防火墙拦了 1883。先用现场网络确认能 ping 通 Broker IP。

**已连接，但 `data` 是空的或 raw 不增长**  
Broker 暂时没有立体库报文。确认订阅没被改掉（默认 `+/+/HUAZH/#`）。连上时若现场有保留状态，一般会先收到一批。

**两台电脑互相掉线**  
`ClientId` 重复了，改成每台不一样。

**提示找不到 config/appsettings.json**  
没有拷完整文件夹，或把 exe 单独拿出来运行了。必须在「整个程序文件夹」里启动。

**dead-letter 里很多记录**  
现场报文缺 `timestamp`、不是 JSON、或 Topic 与 `catalog.json` 对不上。把该文件发给维护人员。

**改了配置没生效**  
先停掉程序再改 JSON，保存后再启动。

---

## 7. 不要做的事

- 不要只拷贝 `Longhua.Collector.exe` 一个文件  
- 不要删除同目录下的 dll  
- 不要关闭正在采集的黑窗口  
- 不要两台机器用同一个 `ClientId`  
- 不要用本程序给立库下发任务  

---

## 8. 日常备忘

| 目的 | 做法 |
|---|---|
| 开始采集 | 双击 `Longhua.Collector.exe`，窗口不要关 |
| 停止采集 | 窗口里 `Ctrl+C`，或关窗口 |
| 改服务器地址 | 改 `config\appsettings.json` 里的 `Host`，重启程序 |
| 看数据 | 打开本文件夹下 `data\当天日期\` |
| 换电脑跑 | 拷贝**整个文件夹**，改一个新的 `ClientId` |
