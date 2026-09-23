using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BiliVrcLink;

internal sealed record VideoPage(int Number, string Title, long Cid);
internal sealed record VideoInfo(string Bvid, string Title, string? CoverUrl, IReadOnlyList<VideoPage> Pages, int RequestedPage);
internal sealed record VideoLink(string Url, int Quality, string Format, DateTimeOffset? ExpiresAt, long? SizeBytes);
internal enum DownloadKind { ProgressiveMp4, DashMp4, Mp3 }
internal sealed record DownloadChoice(DownloadKind Kind, int Quality, string Label, string? VideoUrl, string? AudioUrl, long? SizeBytes)
{
    public override string ToString() => Label;
}

internal sealed class BilibiliResolver
{
    private static readonly Regex BvidPattern = new("^BV[0-9A-Za-z]{10}$", RegexOptions.Compiled);
    private static readonly Regex VideoPathPattern = new("^/video/(BV[0-9A-Za-z]{10})(?:/|$)", RegexOptions.Compiled);
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        }) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36");
        client.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
        return client;
    }

    internal static (string Bvid, int Page) ParseInput(string input)
    {
        input = input.Trim();
        if (BvidPattern.IsMatch(input)) return (input, 1);

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.Host is not ("www.bilibili.com" or "bilibili.com" or "m.bilibili.com"))
        {
            throw new InvalidOperationException("请输入完整的 Bilibili 视频网址，或单独的 BV 号。");
        }

        var match = VideoPathPattern.Match(uri.AbsolutePath);
        if (!match.Success) throw new InvalidOperationException("网址中没有找到有效的 BV 号。");

        var page = 1;
        foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            if (pair[0] == "p" && pair.Length == 2
                && int.TryParse(Uri.UnescapeDataString(pair[1]), out var requestedPage)
                && requestedPage > 0)
            {
                page = requestedPage;
                break;
            }
        }
        return (match.Groups[1].Value, page);
    }

    internal async Task<VideoInfo> GetVideoInfoAsync(string input)
    {
        var (bvid, page) = ParseInput(input);
        using var json = await GetJsonAsync($"https://api.bilibili.com/x/web-interface/view?bvid={Uri.EscapeDataString(bvid)}");
        var data = RequireData(json.RootElement);
        var title = data.GetProperty("title").GetString() ?? bvid;
        var coverUrl = data.TryGetProperty("pic", out var pic) ? pic.GetString() : null;
        var pages = new List<VideoPage>();
        foreach (var item in data.GetProperty("pages").EnumerateArray())
        {
            pages.Add(new VideoPage(
                item.GetProperty("page").GetInt32(),
                item.GetProperty("part").GetString() ?? "",
                item.GetProperty("cid").GetInt64()));
        }
        if (pages.Count == 0) throw new InvalidOperationException("该视频没有可解析的分 P。");
        if (!pages.Any(item => item.Number == page))
            throw new InvalidOperationException($"这个视频只有 {pages.Count} 个分 P，链接指定的第 {page} P 不存在。");
        return new VideoInfo(bvid, title, coverUrl, pages, page);
    }

    internal async Task<byte[]?> GetCoverAsync(string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(coverUrl)) return null;
        if (!Uri.TryCreate(coverUrl, UriKind.Absolute, out var original)
            || !original.Host.EndsWith(".hdslb.com", StringComparison.OrdinalIgnoreCase))
            return null;

        var uri = new UriBuilder(original) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        const int maxBytes = 8 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidOperationException("封面文件过大。");
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var result = new MemoryStream();
        var buffer = new byte[32 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (result.Length + count > maxBytes) throw new InvalidOperationException("封面文件过大。");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }

    internal async Task<VideoLink> GetVideoLinkAsync(string bvid, VideoPage page)
    {
        // fnval=1 asks for a progressive MP4. DASH has separate video and audio URLs.
        var query = $"bvid={Uri.EscapeDataString(bvid)}&cid={page.Cid}&qn=64&fnver=0&fnval=1&fourk=0&platform=html5";
        using var json = await GetJsonAsync("https://api.bilibili.com/x/player/playurl?" + query);
        var data = RequireData(json.RootElement);

        if (!data.TryGetProperty("durl", out var durl) || durl.ValueKind != JsonValueKind.Array || durl.GetArrayLength() != 1)
            throw new InvalidOperationException("Bilibili 没有返回单文件视频地址；这个视频暂不支持直接粘贴进 VRChat。");

        var url = durl[0].GetProperty("url").GetString();
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Bilibili 返回的地址不是 HTTPS MP4；没有生成可直接使用的链接。");

        var quality = data.TryGetProperty("quality", out var q) ? q.GetInt32() : 0;
        var format = data.TryGetProperty("format", out var f) ? f.GetString() ?? "" : "";
        DateTimeOffset? expiry = null;
        foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            if (pair[0] == "deadline" && pair.Length == 2 && long.TryParse(pair[1], out var seconds))
            {
                try { expiry = DateTimeOffset.FromUnixTimeSeconds(seconds); }
                catch (ArgumentOutOfRangeException) { /* The link can still be copied. */ }
                break;
            }
        }
        long? sizeBytes = durl[0].TryGetProperty("size", out var size) && size.TryGetInt64(out var number) ? number : null;
        return new VideoLink(url, quality, format, expiry, sizeBytes);
    }

    internal async Task<IReadOnlyList<DownloadChoice>> GetDownloadChoicesAsync(string bvid, VideoPage page, VideoLink progressive)
    {
        var choices = new List<DownloadChoice>
        {
            new(DownloadKind.ProgressiveMp4, progressive.Quality,
                $"MP4 · {QualityName(progressive.Quality)} · 单文件", progressive.Url, null, progressive.SizeBytes)
        };
        var query = $"bvid={Uri.EscapeDataString(bvid)}&cid={page.Cid}&qn=127&fnver=0&fnval=16&fourk=1";
        using var json = await GetJsonAsync("https://api.bilibili.com/x/player/playurl?" + query);
        var data = RequireData(json.RootElement);
        if (!data.TryGetProperty("dash", out var dash) || dash.ValueKind != JsonValueKind.Object)
            return choices;
        if (!dash.TryGetProperty("audio", out var audioArray) || audioArray.ValueKind != JsonValueKind.Array)
            return choices;
        var audio = audioArray.EnumerateArray()
            .OrderByDescending(item => item.TryGetProperty("bandwidth", out var bandwidth) ? bandwidth.GetInt64() : 0)
            .Select(StreamUrl)
            .FirstOrDefault(url => url is not null);
        if (audio is null) return choices;

        choices.Add(new DownloadChoice(DownloadKind.Mp3, 0, "MP3 · 仅音频 · 192 kbps", null, audio, null));
        if (!dash.TryGetProperty("video", out var videoArray) || videoArray.ValueKind != JsonValueKind.Array)
            return choices;
        var streams = videoArray.EnumerateArray()
            .Where(item => item.TryGetProperty("codecs", out var codecs)
                && (codecs.GetString()?.StartsWith("avc1", StringComparison.OrdinalIgnoreCase) ?? false))
            .GroupBy(item => item.GetProperty("id").GetInt32())
            .OrderByDescending(group => group.Key);
        foreach (var group in streams)
        {
            var item = group.OrderByDescending(stream => stream.TryGetProperty("bandwidth", out var bw) ? bw.GetInt64() : 0).First();
            var url = StreamUrl(item);
            if (url is null || group.Key == progressive.Quality) continue;
            choices.Add(new DownloadChoice(DownloadKind.DashMp4, group.Key,
                $"MP4 · {QualityName(group.Key)} · H.264 + AAC", url, audio, null));
        }
        return choices.OrderBy(choice => choice.Kind == DownloadKind.Mp3 ? 1 : 0).ToList();
    }

    internal static string QualityName(int quality) => quality switch
    {
        6 => "240P", 16 => "360P", 32 => "480P", 64 => "720P", 74 => "720P60",
        80 => "1080P", 112 => "1080P+", 116 => "1080P60", 120 => "4K", 125 => "HDR",
        126 => "杜比视界", 127 => "8K", _ => $"画质 {quality}"
    };

    private static string? StreamUrl(JsonElement stream)
    {
        var value = stream.TryGetProperty("baseUrl", out var camel) ? camel.GetString()
            : stream.TryGetProperty("base_url", out var snake) ? snake.GetString() : null;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? value : null;
    }

    private static async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        return await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
    }

    private static JsonElement RequireData(JsonElement root)
    {
        var code = root.GetProperty("code").GetInt32();
        if (code != 0)
        {
            var message = root.TryGetProperty("message", out var value) ? value.GetString() : null;
            throw new InvalidOperationException($"Bilibili 返回错误 {code}：{message ?? "未知错误"}");
        }
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Bilibili 未返回视频数据。");
        return data;
    }
}
