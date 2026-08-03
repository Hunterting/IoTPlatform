using System.Text;
using IoTPlatform.Tools.AnShengFieldTest;

// Ensure UTF-8 output for Chinese payloads/logs on all platforms.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.InputEncoding = Encoding.UTF8;

var options = FieldTestOptions.Parse(args);

if (options.ShowHelp)
{
    Console.WriteLine(FieldTestOptions.HelpText);
    return 0;
}

using var cts = new CancellationTokenSource();

// Ctrl+C -> graceful cancellation (capture what we have, then write report).
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\n[signal] 收到 Ctrl+C，正在收尾...");
    try { cts.Cancel(); } catch { /* ignore */ }
};

int exitCode = 0;
FieldTestReportData? data = null;
try
{
    var runner = new FieldTestRunner(options);
    data = await runner.RunAsync(cts.Token);
    exitCode = data.ExitCode;
}
catch (OperationCanceledException)
{
    exitCode = 130; // SIGINT-style
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[fatal] {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    exitCode = 1;
}
finally
{
    if (data is not null)
    {
        try
        {
            var stamp = DateTime.Now;
            var reportPath = ReportWriter.Write(data, options.OutputDirectory, stamp);
            Console.WriteLine();
            Console.WriteLine($"[report] 报告: {reportPath}");
            if (data.CapturePath is not null)
                Console.WriteLine($"[capture] 抓包: {data.CapturePath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[warn] 写报告失败: {ex.Message}");
        }
    }
}

Console.WriteLine($"[exit] code={exitCode}");
return exitCode;
