using System.Diagnostics;
using System.Text;

namespace LiteDB.ReproRunner.Cli.Execution;

internal sealed class ReproBuildCoordinator
{
    public async Task<IReadOnlyList<ReproBuildResult>> BuildAsync(
        IEnumerable<RunVariantPlan> variants,
        CancellationToken cancellationToken)
    {
        if (variants is null)
        {
            throw new ArgumentNullException(nameof(variants));
        }

        var results = new List<ReproBuildResult>();

        foreach (var plan in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (plan.Repro.ProjectPath is null)
            {
                results.Add(ReproBuildResult.CreateMissingProject(plan));
                continue;
            }

            var projectPath = plan.Repro.ProjectPath;
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var assemblyName = Path.GetFileNameWithoutExtension(projectPath);

            var arguments = new List<string>
            {
                "build",
                projectPath,
                "-c",
                "Release",
                "--nologo",
                $"-p:UseProjectReference={(plan.UseProjectReference ? "true" : "false")}",
                $"-p:OutputPath={plan.BuildOutputDirectory}"
            };

            var (exitCode, output) = await RunProcessAsync(projectDirectory, arguments, cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                results.Add(ReproBuildResult.CreateFailure(plan, exitCode, output));
                continue;
            }

            var assemblyPath = Path.Combine(plan.BuildOutputDirectory, assemblyName + ".dll");

            if (!File.Exists(assemblyPath))
            {
                output.Add($"Assembly '{assemblyPath}' was not produced by build.");
                results.Add(ReproBuildResult.CreateFailure(plan, exitCode: -1, output));
                continue;
            }

            results.Add(ReproBuildResult.CreateSuccess(plan, assemblyPath, output));
        }

        return results;
    }

    private static async Task<(int ExitCode, List<string> Output)> RunProcessAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet build process.");

        var lines = new List<string>();

        var outputTask = CaptureAsync(process.StandardOutput, lines, cancellationToken);
        var errorTask = CaptureAsync(process.StandardError, lines, cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);

        return (process.ExitCode, lines);
    }

    private static async Task CaptureAsync(StreamReader reader, List<string> destination, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                destination.Add(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }
}

internal sealed class ReproBuildResult
{
    private ReproBuildResult(
        RunVariantPlan plan,
        bool succeeded,
        int exitCode,
        string? assemblyPath,
        IReadOnlyList<string> output)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Succeeded = succeeded;
        ExitCode = exitCode;
        AssemblyPath = assemblyPath;
        Output = output;
    }

    public RunVariantPlan Plan { get; }

    public bool Succeeded { get; }

    public int ExitCode { get; }

    public string? AssemblyPath { get; }

    public IReadOnlyList<string> Output { get; }

    public static ReproBuildResult CreateSuccess(RunVariantPlan plan, string assemblyPath, IReadOnlyList<string> output)
    {
        return new ReproBuildResult(plan, succeeded: true, exitCode: 0, assemblyPath: assemblyPath, output: output);
    }

    public static ReproBuildResult CreateFailure(RunVariantPlan plan, int exitCode, IReadOnlyList<string> output)
    {
        return new ReproBuildResult(plan, succeeded: false, exitCode: exitCode, assemblyPath: null, output: output);
    }

    public static ReproBuildResult CreateMissingProject(RunVariantPlan plan)
    {
        var output = new List<string> { "No project file was discovered for this repro." };
        return new ReproBuildResult(plan, succeeded: false, exitCode: -2, assemblyPath: null, output);
    }
}
