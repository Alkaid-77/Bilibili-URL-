# FFmpeg 第三方组件说明

- 组件：FFmpeg 9.0.2 release essentials，Windows x64 静态构建
- 构建方：[Gyan Doshi](https://www.gyan.dev/ffmpeg/builds/)；该页面由 [FFmpeg 下载页](https://ffmpeg.org/download.html)链接
- 原始压缩包：`ffmpeg-release-essentials.7z`
- 原始压缩包 SHA-256：`4705843ccaaf54257c16ad90f3e952ece33c17df964ecf7bfdbb0f49c7171077`，已与发布方提供值核对
- 提取后的 `ffmpeg.exe` SHA-256：`3256173f3f8bffd7df12227c68adf68025edb1832273a9530688a7bb1ed8edec`
- 许可：构建包标示为 GPL v3，完整文本见 `FFmpeg-LICENSE.txt`
- 构建包说明：`FFmpeg-README.txt`，其中提供确切的 [FFmpeg 源代码提交](https://github.com/FFmpeg/FFmpeg/commit/946fcce07b)

本项目通过独立进程调用 `ffmpeg.exe` 完成 MP3 转码和 MP4 音视频合并。重新分发或修改本组件时，请遵守其许可及相关依赖的要求。
