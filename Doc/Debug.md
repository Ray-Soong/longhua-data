PS D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\net6.0>
PS D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\net6.0>
PS D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\net6.0> ls


    目录: D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\net6.0


Mode                 LastWriteTime         Length Name
----                 -------------         ------ ----
d-----         2026/9/21      0:36                config
d-----         2026/9/17      2:21                data
d-----         2026/9/17      2:04                runtimes
-a----         2026/9/21      0:36          29980 Longhua.Collector.deps.json
-a----         2026/9/21      0:36         119296 Longhua.Collector.dll
-a----         2026/9/21      0:36         151552 Longhua.Collector.exe
-a----         2026/9/21      0:36          44620 Longhua.Collector.pdb
-a----         2026/9/21      0:36            147 Longhua.Collector.runtimeconfig.json
-a----        2021/10/23      0:47          25216 Microsoft.Extensions.Configuration.Abstractions.dll
-a----        2021/10/23      0:49          33920 Microsoft.Extensions.Configuration.Binder.dll
-a----        2021/10/23      0:50          23152 Microsoft.Extensions.Configuration.CommandLine.dll
-a----        2021/10/23      0:49          36464 Microsoft.Extensions.Configuration.dll
-a----         2022/1/14     19:54          18536 Microsoft.Extensions.Configuration.EnvironmentVariables.dll
-a----        2021/10/23      0:50          26224 Microsoft.Extensions.Configuration.FileExtensions.dll
-a----        2021/10/23      0:50          25728 Microsoft.Extensions.Configuration.Json.dll
-a----         2022/1/14     19:55          24672 Microsoft.Extensions.Configuration.UserSecrets.dll
-a----        2021/10/23      0:48          43632 Microsoft.Extensions.DependencyInjection.Abstractions.dll
-a----        2021/10/23      0:49          81536 Microsoft.Extensions.DependencyInjection.dll
-a----        2021/10/23      0:51          21120 Microsoft.Extensions.FileProviders.Abstractions.dll
-a----        2021/10/23      0:51          42624 Microsoft.Extensions.FileProviders.Physical.dll
-a----        2021/10/23      0:47          44160 Microsoft.Extensions.FileSystemGlobbing.dll
-a----        2021/10/23      0:50          27776 Microsoft.Extensions.Hosting.Abstractions.dll
-a----         2022/1/14     19:55          55400 Microsoft.Extensions.Hosting.dll
-a----         2023/5/19     21:54          25216 Microsoft.Extensions.Hosting.WindowsServices.dll
-a----        2021/10/23      0:51          62064 Microsoft.Extensions.Logging.Abstractions.dll
-a----        2021/10/23      0:50          26736 Microsoft.Extensions.Logging.Configuration.dll
-a----        2021/10/23      0:53          50304 Microsoft.Extensions.Logging.Console.dll
-a----        2021/10/23      0:50          18048 Microsoft.Extensions.Logging.Debug.dll
-a----        2021/10/23      0:50          44656 Microsoft.Extensions.Logging.dll
-a----        2021/10/23      0:50          24192 Microsoft.Extensions.Logging.EventLog.dll
-a----        2021/10/23      0:53          32896 Microsoft.Extensions.Logging.EventSource.dll
-a----        2021/10/23      0:50          22656 Microsoft.Extensions.Options.ConfigurationExtensions.dll
-a----        2021/10/23      0:50          59008 Microsoft.Extensions.Options.dll
-a----        2021/10/23      0:51          40048 Microsoft.Extensions.Primitives.dll
-a----          2024/9/7     10:49         348584 MQTTnet.dll
-a----         2023/5/11     21:32          44544 Sharp7.dll
-a----        2021/10/23      0:50          51328 System.Diagnostics.EventLog.dll
-a----         2023/5/19     21:52          33440 System.ServiceProcess.ServiceController.dll
-a----         2026/9/17      2:10           8928 使用说明.md


PS D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\net6.0>
PS D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\net6.0>
PS D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\net6.0> .\Longhua.Collector.exe phase --input=D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\test2.txt --block-length=514  --point-table=config\plc-conveyor.json    --output=D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\test2.phased.jsonl
info: Phase[0]
      phase Input=D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\test2.txt BlockLength=514 PointTable=D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\net6.0\config\plc-conveyor.json Output=D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\test2.phased.jsonl
info: Longhua.Collector.S7.PhaseRunner[0]
      phase 完成 Input=D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\test2.txt Frames=7445 Rows=171235 Stations=23 BlockLength=514 Output=D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\test2.phased.jsonl
PS D:\workspace\SHLH\longhua-data\Code\src\Longhua.Collector\bin\Debug\net6.0>

