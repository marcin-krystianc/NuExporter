using System;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Build.Locator;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace NuExporter;

public class Program
{
    static async Task Main(string[] args)
    {
        MSBuildLocator.RegisterDefaults();

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(theme: ConsoleTheme.None)
            .CreateLogger();

        var parserResult = await Parser.Default.ParseArguments<Options>(args).WithParsedAsync(async options =>
        {
            var nuExporter = new NuExporter();
            await nuExporter.DoWorkAsync(options);
        });
        parserResult.WithNotParsed(_ => DisplayHelp());
    }

    static void DisplayHelp()
    {
        var helpText = "TODO(marcink) \n";

        Console.Write(helpText);
    }
}
