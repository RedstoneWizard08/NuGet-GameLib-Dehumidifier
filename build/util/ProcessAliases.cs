using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cake.Common.Tools.Command;
using Cake.Core;
using Cake.Core.IO;

namespace Build.util;

public static class ProcessAliases
{
    private static FilePath? ResolveToolPath(ICakeContext context, CommandSettings settings)
    {
        var presetToolPath = settings.ToolPath;
        if (presetToolPath != null)
        {
            return presetToolPath.MakeAbsolute(context.Environment);
        }
        
        var resolvedToolPath = context.Tools.Resolve(settings.ToolExecutableNames);
        return resolvedToolPath;
    }

    public static async Task<Tuple<StreamReader?, StreamReader?>> ProcessAsync(
        this ICakeContext context,
        CommandSettings settings,
        ProcessArgumentBuilder? arguments = null,
        bool captureOutput = false,
        bool captureError = false
    )
    {
        var resolvedToolPath = ResolveToolPath(context, settings);
        if (resolvedToolPath == null) throw new Exception($"Couldn't resolve path for '{settings.ToolName}'");

        var startInfo = new ProcessStartInfo(
            resolvedToolPath.FullPath,
            arguments?.Select(argument => argument.Render()) ?? Array.Empty<string>()
        )
        {
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureError,
            UseShellExecute = true,
        };

        using var process = new Process();
        process.StartInfo = startInfo;

        var output = new MemoryStream();
        await using var outputWriter = new StreamWriter(output, leaveOpen: captureOutput);
        process.OutputDataReceived += async (sender, args) => { await outputWriter.WriteLineAsync(args.Data); };

        var error = new MemoryStream();
        await using var errorWriter = new StreamWriter(error, leaveOpen: captureError);
        process.ErrorDataReceived += async (sender, args) => { await errorWriter.WriteLineAsync(args.Data); };
        
        process.Start();
        if (captureOutput) process.BeginOutputReadLine();
        if (captureError) process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        await outputWriter.FlushAsync();
        await errorWriter.FlushAsync();

        if (process.ExitCode != 0)
            throw new Exception($"{settings.ToolName} returned exit code " + process.ExitCode + ".");
        
        output.Position = 0;
        error.Position = 0;

        return new Tuple<StreamReader?, StreamReader?>(
            captureOutput ? new StreamReader(output) : null,
            captureError ? new StreamReader(error) : null
        );
    }
}