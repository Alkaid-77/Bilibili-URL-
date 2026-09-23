using System.Windows.Forms;

namespace BiliVrcLink;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["--self-test-bundle", var cacheDirectory])
        {
            Directory.CreateDirectory(cacheDirectory);
            try
            {
                var path = BundledFfmpeg.GetOrExtractAsync(cacheDirectory).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(cacheDirectory, "self-test-result.txt"), path);
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(cacheDirectory, "self-test-error.txt"), error.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
