using System.Drawing;
using System.Windows.Forms;

namespace BiliVrcLink;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(32, 48, 66);
    private static readonly Color Muted = Color.FromArgb(94, 110, 126);
    private static readonly Color Accent = Color.FromArgb(0, 143, 205);
    private readonly BilibiliResolver _resolver = new();
    private readonly VideoDownloader _downloader = new();
    private readonly TextBox _input = new() { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 11) };
    private readonly ComboBox _pages = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _choices = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly PictureBox _cover = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(244, 248, 251) };
    private readonly Label _coverEmpty = new() { Dock = DockStyle.Fill, Text = "视频封面", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, BackColor = Color.FromArgb(244, 248, 251) };
    private readonly Label _details = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Ink };
    private readonly TextBox _result = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9) };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _progressText = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted };
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous };
    private readonly Button _parse = Button("解析", Accent, Color.White);
    private readonly Button _paste = Button("粘贴并解析", Color.FromArgb(230, 243, 250), Ink);
    private readonly Button _copy = Button("复制直链", Accent, Color.White);
    private readonly Button _download = Button("下载到本地…", Accent, Color.White);
    private readonly Button _cancelDownload = Button("取消", Color.FromArgb(238, 242, 245), Ink);
    private readonly LinkLabel _licenses = new() { Text = "第三方许可", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomRight, LinkColor = Accent };

    private VideoInfo? _video;
    private VideoPage? _currentPage;
    private VideoLink? _currentLink;
    private CancellationTokenSource? _downloadCancellation;
    private bool _updatingPages;
    private bool _busy;

    internal MainForm()
    {
        Text = "BiliVRC";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 650);
        Size = new Size(1000, 720);
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Color.White;
        ForeColor = Ink;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 14), ColumnCount = 1, RowCount = 9, BackColor = Color.White };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 120f, 62f, 55f, 64f, 90f, 48f, 68f, 60f, 40f })
            layout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        Controls.Add(layout);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label { Text = "BiliVRC", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 19, FontStyle.Bold), ForeColor = Ink }, 0, 0);
        heading.Controls.Add(new Label { Text = "解析视频直链，并保存 MP4 或 MP3 到本机", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted }, 0, 1);
        var headingFooter = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        headingFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headingFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        headingFooter.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        headingFooter.Controls.Add(new Label { Text = "粘贴网址或 BV 号即可开始", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, ForeColor = Accent, Font = new Font(Font.FontFamily, 9, FontStyle.Bold) }, 0, 0);
        headingFooter.Controls.Add(_licenses, 1, 0);
        heading.Controls.Add(headingFooter, 0, 2);
        header.Controls.Add(heading, 0, 0);
        var coverFrame = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 248, 251), Padding = new Padding(2) };
        coverFrame.Controls.Add(_cover);
        coverFrame.Controls.Add(_coverEmpty);
        _coverEmpty.BringToFront();
        header.Controls.Add(coverFrame, 1, 0);
        layout.Controls.Add(header, 0, 0);

        var inputRow = Row(3, 0, 90, 140);
        inputRow.Controls.Add(_input, 0, 0);
        inputRow.Controls.Add(_parse, 1, 0);
        inputRow.Controls.Add(_paste, 2, 0);
        layout.Controls.Add(inputRow, 0, 1);
        _input.PlaceholderText = "粘贴 Bilibili 视频网址，或输入 BV 号";

        var pageRow = Row(2, 100, 0);
        pageRow.Controls.Add(Label("视频分 P", true), 0, 0);
        pageRow.Controls.Add(_pages, 1, 0);
        layout.Controls.Add(pageRow, 0, 2);

        var detailPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 250, 252), Padding = new Padding(12, 7, 12, 4), Margin = new Padding(3, 4, 3, 4) };
        detailPanel.Controls.Add(_details);
        layout.Controls.Add(detailPanel, 0, 3);
        _details.Text = "解析后显示视频标题、分 P 和实际画质。";

        var resultSection = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 5, 0, 6) };
        resultSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        resultSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        resultSection.Controls.Add(Label("VRChat 播放直链", true), 0, 0);
        resultSection.Controls.Add(_result, 0, 1);
        layout.Controls.Add(resultSection, 0, 4);

        var linkRow = Row(2, 145, 0);
        linkRow.Controls.Add(_copy, 0, 0);
        linkRow.Controls.Add(new Label { Text = "直链会过期；播放前请重新解析并复制。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted }, 1, 0);
        layout.Controls.Add(linkRow, 0, 5);

        var choiceRow = Row(2, 100, 0);
        choiceRow.Controls.Add(Label("格式/画质", true), 0, 0);
        choiceRow.Controls.Add(_choices, 1, 0);
        layout.Controls.Add(choiceRow, 0, 6);

        var downloadRow = Row(3, 170, 90, 0);
        downloadRow.Controls.Add(_download, 0, 0);
        downloadRow.Controls.Add(_cancelDownload, 1, 0);
        var progress = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        progress.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        progress.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        progress.Controls.Add(_progressBar, 0, 0);
        progress.Controls.Add(_progressText, 0, 1);
        downloadRow.Controls.Add(progress, 2, 0);
        layout.Controls.Add(downloadRow, 0, 7);
        layout.Controls.Add(_status, 0, 8);

        _input.TextChanged += (_, _) =>
        {
            if (_busy) return;
            _video = null;
            _pages.Items.Clear();
            ClearResult();
            SetPreviewCover(null);
            _status.Text = "输入已更改，请重新解析。";
        };
        _parse.Click += async (_, _) => await ParseAsync();
        _paste.Click += async (_, _) => { if (Clipboard.ContainsText()) _input.Text = Clipboard.GetText(); await ParseAsync(); };
        _input.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ParseAsync(); } };
        _pages.SelectedIndexChanged += async (_, _) => { if (!_updatingPages && !_busy && _video is not null) await ResolveSelectedPageAsync(); };
        _copy.Click += (_, _) => { if (_currentLink is not null) { Clipboard.SetText(_currentLink.Url); _status.Text = "已复制 URL，可粘贴到 VRChat 世界播放器。"; } };
        _download.Click += async (_, _) => await DownloadAsync();
        _cancelDownload.Click += (_, _) => _downloadCancellation?.Cancel();
        _licenses.LinkClicked += (_, _) => ShowLicenses();
        FormClosing += (_, _) => _downloadCancellation?.Cancel();
        FormClosed += (_, _) => _cover.Image?.Dispose();
        _copy.Enabled = _download.Enabled = _cancelDownload.Enabled = false;
        _copy.BackColor = _download.BackColor = Color.FromArgb(225, 232, 237);
        _status.Text = "本机解析；访问 Bilibili 与下载文件仍需联网。";
    }

    private static Button Button(string text, Color background, Color foreground) => new()
    {
        Text = text, Dock = DockStyle.Fill, BackColor = background, ForeColor = foreground,
        FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(3, 6, 3, 6), Cursor = Cursors.Hand
    };

    private static Label Label(string text, bool bold) => new()
    {
        Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink,
        Font = new Font("Microsoft YaHei UI", 10, bold ? FontStyle.Bold : FontStyle.Regular)
    };

    private static TableLayoutPanel Row(int columns, params int[] widths)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = 1, Margin = Padding.Empty };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < columns; i++)
            row.ColumnStyles.Add(widths[i] == 0 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.Absolute, widths[i]));
        return row;
    }

    internal void SetPreviewCover(Image? image)
    {
        var old = _cover.Image;
        _cover.Image = image;
        _coverEmpty.Visible = image is null;
        old?.Dispose();
    }

    private void ShowLicenses()
    {
        using var window = new Form
        {
            Text = "FFmpeg 第三方许可与来源", StartPosition = FormStartPosition.CenterParent,
            Size = new Size(800, 600), MinimumSize = new Size(560, 400), Font = Font
        };
        window.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9),
            Text = BundledFfmpeg.GetLicenseText()
        });
        window.ShowDialog(this);
    }

    private async Task ParseAsync()
    {
        if (_busy) return;
        SetBusy(true);
        ClearResult();
        _video = null;
        _pages.Items.Clear();
        SetPreviewCover(null);
        try
        {
            _status.Text = "正在读取视频信息…";
            _video = await _resolver.GetVideoInfoAsync(_input.Text);
            _ = LoadCoverAsync(_video);
            _updatingPages = true;
            foreach (var page in _video.Pages) _pages.Items.Add($"P{page.Number}  {page.Title}");
            _pages.SelectedIndex = _video.Pages.ToList().FindIndex(page => page.Number == _video.RequestedPage);
            _updatingPages = false;
            await ResolveSelectedPageCoreAsync();
        }
        catch (Exception ex)
        {
            _updatingPages = false;
            _status.Text = "解析失败：" + Describe(ex);
        }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    private async Task LoadCoverAsync(VideoInfo video)
    {
        try
        {
            var bytes = await _resolver.GetCoverAsync(video.CoverUrl);
            if (IsDisposed || !ReferenceEquals(_video, video) || bytes is null) return;
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);
            SetPreviewCover(new Bitmap(decoded));
        }
        catch { /* Cover is optional; playback and downloads still work. */ }
    }

    private async Task ResolveSelectedPageAsync()
    {
        if (_busy) return;
        SetBusy(true);
        ClearResult();
        try { await ResolveSelectedPageCoreAsync(); }
        catch (Exception ex) { _status.Text = "解析失败：" + Describe(ex); }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    private async Task ResolveSelectedPageCoreAsync()
    {
        if (_video is null || _pages.SelectedIndex < 0) return;
        var page = _video.Pages[_pages.SelectedIndex];
        _status.Text = $"正在获取第 {page.Number} P 的 MP4 地址…";
        var link = await _resolver.GetVideoLinkAsync(_video.Bvid, page);
        _currentPage = page;
        _currentLink = link;
        _result.Text = link.Url;
        _copy.Enabled = true;
        _copy.BackColor = Accent;
        _details.Text = $"{_video.Title}\nP{page.Number}：{page.Title}  ·  实际返回 {BilibiliResolver.QualityName(link.Quality)}（{link.Format}）";
        _choices.Items.Clear();
        _choices.Items.Add(new DownloadChoice(DownloadKind.ProgressiveMp4, link.Quality,
            $"MP4 · {BilibiliResolver.QualityName(link.Quality)} · 单文件", link.Url, null, link.SizeBytes));
        _choices.SelectedIndex = 0;
        try
        {
            _status.Text = "正在读取可下载清晰度…";
            var options = await _resolver.GetDownloadChoicesAsync(_video.Bvid, page, link);
            _choices.Items.Clear();
            foreach (var option in options) _choices.Items.Add(option);
            _choices.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _status.Text = "直链可用；其他下载格式暂不可用：" + Describe(ex);
            return;
        }
        _status.Text = link.ExpiresAt is null
            ? "已取得直链和下载选项；有效时间以 Bilibili 实际响应为准。"
            : $"已取得直链和下载选项；到期时间：{link.ExpiresAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}。";
    }

    private async Task DownloadAsync()
    {
        if (_busy || _video is null || _currentPage is null || _choices.SelectedItem is not DownloadChoice selected) return;
        using var picker = new FolderBrowserDialog { Description = "选择文件保存目录", ShowNewFolderButton = true };
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (Directory.Exists(videos)) picker.SelectedPath = videos;
        if (picker.ShowDialog(this) != DialogResult.OK) return;

        SetBusy(true);
        _downloadCancellation = new CancellationTokenSource();
        _cancelDownload.Enabled = true;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Value = 0;
        _progressText.Text = "准备下载…";
        try
        {
            _status.Text = "正在刷新临时下载地址…";
            var link = await _resolver.GetVideoLinkAsync(_video.Bvid, _currentPage);
            _currentLink = link;
            _result.Text = link.Url;
            if (selected.Kind == DownloadKind.ProgressiveMp4 && link.Quality != selected.Quality)
                throw new InvalidOperationException("实际返回清晰度已变化，请重新解析并选择。");
            var refreshed = selected.Kind == DownloadKind.ProgressiveMp4
                ? new DownloadChoice(selected.Kind, link.Quality, selected.Label, link.Url, null, link.SizeBytes)
                : (await _resolver.GetDownloadChoicesAsync(_video.Bvid, _currentPage, link))
                    .FirstOrDefault(option => option.Kind == selected.Kind && option.Quality == selected.Quality)
                    ?? throw new InvalidOperationException("该清晰度当前不可用，请重新解析并选择。");
            string? ffmpeg = null;
            if (selected.Kind != DownloadKind.ProgressiveMp4)
            {
                _status.Text = "正在准备内置 FFmpeg（首次使用可能需要片刻）…";
                ffmpeg = await BundledFfmpeg.GetOrExtractAsync(cancellationToken: _downloadCancellation.Token);
            }
            var progress = new Progress<DownloadProgress>(item =>
            {
                if (IsDisposed || _downloadCancellation is null) return;
                if (item.Processing)
                {
                    _progressBar.Style = ProgressBarStyle.Marquee;
                    _progressText.Text = item.Stage + "…";
                }
                else if (item.TotalBytes is > 0)
                {
                    _progressBar.Style = ProgressBarStyle.Continuous;
                    var percent = (int)Math.Clamp(item.BytesReceived * 100d / item.TotalBytes.Value, 0, 100);
                    _progressBar.Value = percent;
                    _progressText.Text = $"{item.Stage} {percent}% · {item.BytesReceived / 1048576d:F1} / {item.TotalBytes.Value / 1048576d:F1} MB";
                }
                else
                {
                    _progressBar.Style = ProgressBarStyle.Marquee;
                    _progressText.Text = $"{item.Stage} · {item.BytesReceived / 1048576d:F1} MB";
                }
            });
            _status.Text = "正在下载到选定目录…";
            var path = await _downloader.DownloadAsync(refreshed, picker.SelectedPath, _video.Title,
                _video.Bvid, _currentPage.Number, ffmpeg, progress, _downloadCancellation.Token);
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 100;
            _progressText.Text = "完成";
            _status.Text = "已保存：" + path;
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) { _progressText.Text = "已取消"; _status.Text = "下载已取消；未保留不完整文件。"; }
        }
        catch (Exception ex)
        {
            if (!IsDisposed) { _progressText.Text = "下载失败"; _status.Text = "下载失败：" + Describe(ex); }
        }
        finally
        {
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
            if (!IsDisposed) { _cancelDownload.Enabled = false; SetBusy(false); }
        }
    }

    private void ClearResult()
    {
        _currentLink = null;
        _currentPage = null;
        _result.Clear();
        _copy.Enabled = false;
        _download.Enabled = false;
        _copy.BackColor = _download.BackColor = Color.FromArgb(225, 232, 237);
        _choices.Items.Clear();
        _details.Text = "解析后显示视频标题、分 P 和实际画质。";
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Value = 0;
        _progressText.Text = "";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _parse.Enabled = _paste.Enabled = _input.Enabled = _pages.Enabled = _choices.Enabled = !busy;
        _download.Enabled = !busy && _choices.SelectedItem is DownloadChoice;
        _download.BackColor = _download.Enabled ? Accent : Color.FromArgb(225, 232, 237);
        UseWaitCursor = busy;
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "连接超时，请稍后重试。",
        HttpRequestException { StatusCode: not null } error => $"HTTP {(int)error.StatusCode.Value}；请稍后重试或重新解析。",
        HttpRequestException => "无法访问 Bilibili，请检查网络。",
        System.Text.Json.JsonException => "Bilibili 返回的数据格式发生变化。",
        _ => ex.Message
    };
}
