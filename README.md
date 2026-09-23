# BiliVRC 4.0

Windows x64 本地工具：输入 Bilibili 视频网址或 BV 号，获取可复制到 VRChat 视频播放器的直链，并按界面显示的可用选项下载 MP4 或 MP3。解析与下载需要联网；直链可能过期。

## 下载运行

从本仓库的 **Releases** 下载 `BiliVrcLink.exe`，双击运行。该单文件已包含 .NET 运行时和 FFmpeg，无需另行下载 `tools` 目录。首次使用 MP3 或需要合并的 MP4 时，程序会把 FFmpeg 释放到 `%LOCALAPPDATA%\BiliVRC\Tools`。

## 从源码构建

需要 Windows 和 .NET 8 SDK。在此仓库根目录运行：

```powershell
dotnet publish .\BiliVrcLink\BiliVrcLink.csproj -c Release -r win-x64 --self-contained true -o .\dist
```

产物位于 `dist\BiliVrcLink.exe`。项目使用 `tools\ffmpeg.exe.br` 作为嵌入资源，不需要原始 `ffmpeg.exe`。`assets\app.ico` 是程序图标。

## 第三方组件

程序通过独立进程调用 FFmpeg。其构建来源、许可文本和说明位于 `tools\FFmpeg-NOTICE.md`、`tools\FFmpeg-LICENSE.txt`、`tools\FFmpeg-README.txt`，并嵌入发行版 EXE。请仅下载自己有权保存的内容。
