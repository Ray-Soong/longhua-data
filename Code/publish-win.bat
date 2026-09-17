@echo off
cd /d "%~dp0"
dotnet publish src\Longhua.Collector\Longhua.Collector.csproj -c Release -r win-x64 --self-contained false -o .\publish-win
echo.
echo 请把整个 publish-win 文件夹拷给用户，不要拷 bin\Debug\net6.0
pause
