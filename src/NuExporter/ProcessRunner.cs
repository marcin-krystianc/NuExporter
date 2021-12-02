using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace NuExporter;

public class ProcessRunner
{
    private readonly string _workingDirectory;

    public ProcessRunner(string workingDirectory = null)
    {
        _workingDirectory = workingDirectory;
    }

    public async Task RunAsync(string fileName, params string[] args)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.WorkingDirectory = _workingDirectory;
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        process.StartInfo.CreateNoWindow = true; //not diplay a windows
        process.Start();
        var outputTask = Task.Run(() => process.StandardOutput.ReadToEnd());
        var errorTask = Task.Run(() => process.StandardError.ReadToEnd());
        await process.WaitForExitAsync();
        var outputs = await Task.WhenAll(outputTask, errorTask);

        if (process.ExitCode != 0)
        {
            throw new Exception(
                $"Program '{fileName} {string.Join(" ", args)}' exited with exit code:{process.ExitCode}." +
                Environment.NewLine +
                outputs[0] + Environment.NewLine +
                outputs[1] + Environment.NewLine +
                "");
        }
    }
}
