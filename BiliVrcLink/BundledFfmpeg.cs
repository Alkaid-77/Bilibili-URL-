using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BiliVrcLink;

internal static class BundledFfmpeg
{
    private const string CompressedResource = "BiliVrcLink.ffmpeg.br";
    private const string ExpectedSha256 = "3256173f3f8bffd7df12227c68adf68025edb1832273a9530688a7bb1ed8edec";
    private const long ExpectedLength = 105423872;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static async Task<string> GetOrExtractAsync(string? cacheRoot = null, CancellationToken cancellationToken = default)
    {
        var baseDirectory = cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BiliVRC", "Tools");
        var directory = Path.Combine(baseDirectory, "ffmpeg-9.0.2-" + ExpectedSha256[..12]);
        var path = Path.Combine(directory, "ffmpeg.exe");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (await IsExpectedFileAsync(path, cancellationToken)) return path;
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, "ffmpeg-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(CompressedResource)
                    ?? throw new InvalidOperationException("程序未包含 FFmpeg 压缩资源，请重新获取完整程序。");
                await using var decoder = new BrotliStream(resource, CompressionMode.Decompress);
                await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[128 * 1024];
                    long bytes = 0;
                    int count;
                    while ((count = await decoder.ReadAsync(buffer, cancellationToken)) != 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        hash.AppendData(buffer, 0, count);
                        bytes += count;
                        if (bytes > ExpectedLength) throw new InvalidDataException("内置 FFmpeg 长度异常。");
                    }
                    await output.FlushAsync(cancellationToken);
                    if (bytes != ExpectedLength ||
                        !Convert.ToHexString(hash.GetHashAndReset()).Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("内置 FFmpeg 校验失败。");
                }
                try { File.Move(temporaryPath, path, true); }
                catch (IOException)
                {
                    // A second running copy of the program completed the same extraction.
                    if (!await IsExpectedFileAsync(path, cancellationToken)) throw;
                }
                return path;
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        finally { Gate.Release(); }
    }

    internal static string GetLicenseText()
    {
        return "FFmpeg 第三方组件说明\r\n\r\n" + ReadText("BiliVrcLink.FFmpeg-NOTICE.md")
            + "\r\n\r\n构建包说明\r\n\r\n" + ReadText("BiliVrcLink.FFmpeg-README.txt")
            + "\r\n\r\nGPL v3 许可全文\r\n\r\n" + ReadText("BiliVrcLink.FFmpeg-LICENSE.txt");
    }

    private static string ReadText(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("程序未包含许可资源：" + resourceName);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static async Task<bool> IsExpectedFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != ExpectedLength) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase);
    }
}
