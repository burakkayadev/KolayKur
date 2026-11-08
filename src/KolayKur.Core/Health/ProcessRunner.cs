using System.ComponentModel;
using System.Diagnostics;

namespace KolayKur.Core.Health;

public static class ProcessRunner
{
    private const string TimeoutError = "timeout";

    /// <summary>
    /// Süreci çalıştırır. Süre dolarsa süreç ağacını sonlandırır ve <c>Error == "timeout"</c> döner.
    /// Başlatılamayan süreç (ör. dosya yok) hata olarak döner, istisna fırlatmaz; iptal istisnası fırlatır.
    /// </summary>
    public static async Task<ProbeResult> RunAsync(
        string file, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new ProbeResult(false, "", ex.Message);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            return new ProbeResult(false, "", TimeoutError);
        }

        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode == 0)
        {
            return new ProbeResult(true, output, null);
        }

        return new ProbeResult(false, output, error.Length > 0 ? error : $"exit code {process.ExitCode}");
    }
}
