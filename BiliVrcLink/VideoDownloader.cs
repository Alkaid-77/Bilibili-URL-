using System.Diagnostics;
using System.Text;

namespace BiliVrcLink;

internal sealed record DownloadProgress(string Stage, long BytesReceived, long? TotalBytes, bool Processing = false);

internal sealed class VideoDownloader
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36");
        client.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
        return client;
    }

    internal async Task<string> DownloadAsync(
        DownloadChoice choice, string directory, string title, string bvid, int page,
        string? ffmpegPath, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("选择的下载目录不存在。");
        if (choice.Kind != DownloadKind.ProgressiveMp4 && (ffmpegPath is null || !File.Exists(ffmpegPath)))
            throw new FileNotFoundException("缺少 tools\\ffmpeg.exe，无法合并 MP4 或转码 MP3。", ffmpegPath);
        var extension = choice.Kind == DownloadKind.Mp3 ? ".mp3" : ".mp4";
        var work = Path.Combine(directory, ".BiliVrcLink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var output = Path.Combine(work, "output" + extension);
            if (choice.Kind == DownloadKind.ProgressiveMp4)
            {
                await DownloadStreamAsync(choice.VideoUrl!, output, "下载 MP4", choice.SizeBytes, true, progress, cancellationToken);
            }
            else if (choice.Kind == DownloadKind.Mp3)
            {
                var audio = Path.Combine(work, "audio.m4s");
                await DownloadStreamAsync(choice.AudioUrl!, audio, "下载音轨", null, false, progress, cancellationToken);
                progress.Report(new DownloadProgress("转码 MP3", 0, null, true));
                await RunFfmpegAsync(ffmpegPath!, ["-i", audio, "-vn", "-c:a", "libmp3lame", "-b:a", "192k", output], cancellationToken);
            }
            else
            {
                var video = Path.Combine(work, "video.m4s");
                var audio = Path.Combine(work, "audio.m4s");
                await DownloadStreamAsync(choice.VideoUrl!, video, "下载视频流", null, false, progress, cancellationToken);
                await DownloadStreamAsync(choice.AudioUrl!, audio, "下载音轨", null, false, progress, cancellationToken);
                progress.Report(new DownloadProgress("合并 MP4", 0, null, true));
                await RunFfmpegAsync(ffmpegPath!, ["-i", video, "-i", audio, "-map", "0:v:0", "-map", "1:a:0", "-c", "copy", "-movflags", "+faststart", output], cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidDataException("输出文件为空。");
            var finalPath = ChooseNewPath(directory, title, bvid, page, extension);
            File.Move(output, finalPath);
            return finalPath;
        }
        finally
        {
            if (Directory.Exists(work)) Directory.Delete(work, true);
        }
    }

    private static async Task DownloadStreamAsync(string url, string path, string stage, long? suggestedSize,
        bool requireMp4, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var expected = response.Content.Headers.ContentLength;
        var total = expected ?? suggestedSize;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = new byte[12];
        await source.ReadExactlyAsync(header, cancellationToken);
        if (requireMp4 && Encoding.ASCII.GetString(header, 4, 4) != "ftyp")
            throw new InvalidDataException("服务器返回的内容不是 MP4 文件。");
        await target.WriteAsync(header, cancellationToken);
        var received = (long)header.Length;
        var timer = Stopwatch.StartNew();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            received += count;
            if (timer.ElapsedMilliseconds >= 150)
            {
                progress.Report(new DownloadProgress(stage, received, total));
                timer.Restart();
            }
        }
        await target.FlushAsync(cancellationToken);
        if (expected is > 0 && received != expected)
            throw new InvalidDataException($"下载字节数不完整：收到 {received}，预期 {expected}。");
        progress.Report(new DownloadProgress(stage, received, total));
    }

    private static async Task RunFfmpegAsync(string ffmpegPath, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-nostdin");
        start.ArgumentList.Add("-hide_banner");
        start.ArgumentList.Add("-loglevel");
        start.ArgumentList.Add("error");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 FFmpeg。");
        using var cancel = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None);
        var error = await stderr;
        await stdout;
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("FFmpeg 处理失败：" + (error.Length > 400 ? error[^400..] : error).Trim());
    }

    private static string ChooseNewPath(string directory, string title, string bvid, int page, string extension)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeTitle = new string(title.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim().TrimEnd('.');
        if (safeTitle.Length > 70) safeTitle = safeTitle[..70].TrimEnd(' ', '.');
        if (safeTitle.Length == 0) safeTitle = "Bilibili 视频";
        var name = $"{safeTitle} - {bvid} - P{page}";
        for (var suffix = 0; suffix < 10000; suffix++)
        {
            var numberedName = suffix == 0 ? name : $"{name} ({suffix + 1})";
            var path = Path.Combine(directory, numberedName + extension);
            if (!File.Exists(path)) return path;
        }
        throw new IOException("同名文件过多，无法选择新的文件名。");
    }
}
