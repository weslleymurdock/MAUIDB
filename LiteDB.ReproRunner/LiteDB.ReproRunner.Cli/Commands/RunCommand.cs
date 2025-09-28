using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Linq;
using LiteDB.ReproRunner.Cli.Execution;
using LiteDB.ReproRunner.Cli.Infrastructure;
using LiteDB.ReproRunner.Cli.Manifests;
using LiteDB.ReproRunner.Shared.Messaging;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace LiteDB.ReproRunner.Cli.Commands;

internal sealed class RunCommand : AsyncCommand<RunCommandSettings>
{
    private const int MaxLogLines = 5;
    private const decimal DefaultFps = 30.0m;
    private const decimal MinimumFps = 1.0m;
    private const decimal MaximumFps = 60.0m;
    private const decimal FpsStep = 0.5m;

    private readonly IAnsiConsole _console;
    private readonly ReproRootLocator _rootLocator;
    private readonly RunDirectoryPlanner _planner;
    private readonly ReproBuildCoordinator _buildCoordinator;
    private readonly ReproExecutor _executor;
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunCommand"/> class.
    /// </summary>
    /// <param name="console">The console used to render output.</param>
    /// <param name="rootLocator">Resolves the repro root directory.</param>
    /// <param name="planner">Creates deterministic run directories.</param>
    /// <param name="buildCoordinator">Builds repro variants before execution.</param>
    /// <param name="executor">Executes repro variants.</param>
    /// <param name="cancellationToken">Signals cancellation requests.</param>
    public RunCommand(
        IAnsiConsole console,
        ReproRootLocator rootLocator,
        RunDirectoryPlanner planner,
        ReproBuildCoordinator buildCoordinator,
        ReproExecutor executor,
        CancellationToken cancellationToken)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _rootLocator = rootLocator ?? throw new ArgumentNullException(nameof(rootLocator));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _buildCoordinator = buildCoordinator ?? throw new ArgumentNullException(nameof(buildCoordinator));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Executes the run command.
    /// </summary>
    /// <param name="context">The Spectre command context.</param>
    /// <param name="settings">The run settings provided by the user.</param>
    /// <returns>The process exit code.</returns>
    public override async Task<int> ExecuteAsync(CommandContext context, RunCommandSettings settings)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var repository = new ManifestRepository(_rootLocator.ResolveRoot(settings.Root));
        var manifests = repository.Discover();
        var selected = settings.All
            ? manifests.ToList()
            : manifests
                .Where(x => string.Equals(x.Manifest?.Id ?? x.RawId, settings.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (selected.Count == 0)
        {
            if (settings.All)
            {
                _console.MarkupLine("[yellow]No repros discovered.[/]");
                return 0;
            }

            _console.MarkupLine($"[red]Repro '{Markup.Escape(settings.Id!)}' was not found.[/]");
            return 1;
        }

        var table = new Table().Border(TableBorder.Rounded).AddColumns("Repro", "Repro Version", "Reproduced", "Fixed");
        var fpsDial = new FpsDial(DefaultFps, MinimumFps, MaximumFps, FpsStep);
        var logLines = new List<string>();
        var layout = new Layout("root")
            .SplitRows(
                new Layout("controls").Size(4),
                new Layout("logs").Size(8),
                new Layout("results"));

        layout["results"].Update(table);
        layout["controls"].Update(CreateFpsView(fpsDial));
        layout["logs"].Update(CreateLogView(logLines));
        var overallExitCode = 0;
        var plannedVariants = new List<RunVariantPlan>();
        var buildFailures = new List<BuildFailure>();
        var uiUpdates = Channel.CreateUnbounded<UiUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        try
        {
            await _console.Live(layout).StartAsync(async ctx =>
            {
                var uiTask = ProcessUiUpdatesAsync(uiUpdates.Reader, table, layout, logLines, fpsDial, ctx, _cancellationToken);
                var writer = uiUpdates.Writer;
                var previousObserver = _executor.LogObserver;
                var previousSuppression = _executor.SuppressConsoleLogOutput;
                _executor.SuppressConsoleLogOutput = true;
                _executor.LogObserver = entry =>
                {
                    var formatted = FormatLogLine(entry);
                    writer.TryWrite(new LogLineUpdate(formatted));
                };

                void QueueRow(string reproId, string version, string reproduced, string fixedStatus)
                {
                    writer.TryWrite(new TableRowUpdate(reproId, version, reproduced, fixedStatus));
                }

                CancellationTokenSource? inputCancellation = null;
                Task? inputTask = null;

                try
                {
                    if (!Console.IsInputRedirected)
                    {
                        inputCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
                        inputTask = MonitorFpsInputAsync(fpsDial, writer, inputCancellation.Token);
                    }

                    var candidates = new List<RunCandidate>();

                    foreach (var repro in selected)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();

                        if (!repro.IsValid)
                        {
                            if (!settings.SkipValidation)
                            {
                                QueueRow(Markup.Escape(repro.RawId ?? "(unknown)"), "[red]Invalid[/]", "[red]❌[/]", "[red]❌[/]");
                                overallExitCode = overallExitCode == 0 ? 2 : overallExitCode;
                                continue;
                            }

                            if (repro.Manifest is null)
                            {
                                QueueRow(Markup.Escape(repro.RawId ?? "(unknown)"), "[red]Invalid[/]", "[red]❌[/]", "[red]❌[/]");
                                overallExitCode = overallExitCode == 0 ? 2 : overallExitCode;
                                continue;
                            }
                        }

                        if (repro.Manifest is null)
                        {
                            QueueRow(Markup.Escape(repro.RawId ?? "(unknown)"), "[red]Missing[/]", "[red]❌[/]", "[red]❌[/]");
                            overallExitCode = overallExitCode == 0 ? 2 : overallExitCode;
                            continue;
                        }

                        var manifest = repro.Manifest;
                        var instances = settings.Instances ?? manifest.DefaultInstances;

                        if (manifest.RequiresParallel && instances < 2)
                        {
                            QueueRow(Markup.Escape(manifest.Id), "[red]Config Error[/]", "[red]❌[/]", "[red]❌[/]");
                            overallExitCode = 1;
                            continue;
                        }

                        if (repro.ProjectPath is null)
                        {
                            QueueRow(Markup.Escape(manifest.Id), "[red]Project Missing[/]", "[red]❌[/]", "[red]❌[/]");
                            overallExitCode = overallExitCode == 0 ? 2 : overallExitCode;
                            continue;
                        }

                        var timeoutSeconds = settings.Timeout ?? manifest.TimeoutSeconds;
                        var packageVersion = TryResolvePackageVersion(repro.ProjectPath);
                        var packageDisplay = packageVersion ?? "NuGet";
                        var packageVariantId = BuildVariantIdentifier(packageVersion);

                        var packagePlan = _planner.CreateVariantPlan(
                            repro,
                            manifest.Id,
                            packageVariantId,
                            packageDisplay,
                            useProjectReference: false);

                        var latestPlan = _planner.CreateVariantPlan(
                            repro,
                            manifest.Id,
                            "ver_latest",
                            "Latest",
                            useProjectReference: true);

                        plannedVariants.Add(packagePlan);
                        plannedVariants.Add(latestPlan);

                        candidates.Add(new RunCandidate(
                            manifest,
                            instances,
                            timeoutSeconds,
                            packageDisplay,
                            packagePlan,
                            latestPlan));
                    }

                    if (candidates.Count == 0)
                    {
                        return;
                    }

                    var buildResults = await _buildCoordinator.BuildAsync(plannedVariants, _cancellationToken).ConfigureAwait(false);
                    var buildLookup = buildResults.ToDictionary(result => result.Plan);

                    foreach (var candidate in candidates)
                    {
                        var packageBuild = buildLookup[candidate.PackagePlan];
                        var latestBuild = buildLookup[candidate.LatestPlan];

                        ReproExecutionResult? packageResult = null;
                        ReproExecutionResult? latestResult = null;

                        if (packageBuild.Succeeded)
                        {
                            packageResult = await _executor.ExecuteAsync(packageBuild, candidate.Instances, candidate.TimeoutSeconds, _cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            overallExitCode = overallExitCode == 0 ? 1 : overallExitCode;
                            buildFailures.Add(new BuildFailure(candidate.Manifest.Id, candidate.PackageDisplay, packageBuild.Output));
                        }

                        if (latestBuild.Succeeded)
                        {
                            latestResult = await _executor.ExecuteAsync(latestBuild, candidate.Instances, candidate.TimeoutSeconds, _cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            overallExitCode = overallExitCode == 0 ? 1 : overallExitCode;
                            buildFailures.Add(new BuildFailure(candidate.Manifest.Id, "Latest", latestBuild.Output));
                        }

                        var versionCell = packageBuild.Succeeded
                            ? Markup.Escape(candidate.PackageDisplay)
                            : "[red]Build Failed[/]";

                        var reproducedStatus = packageResult is null
                            ? "[yellow]-[/]"
                            : (packageResult.Value.Reproduced ? "[green]✅[/]" : "[red]❌[/]");

                        var fixedStatus = latestResult is null
                            ? "[yellow]-[/]"
                            : (latestResult.Value.Reproduced ? "[red]❌[/]" : "[green]✅[/]");

                        if ((packageResult?.Reproduced ?? false) || (latestResult?.Reproduced ?? false))
                        {
                            overallExitCode = overallExitCode == 0 ? 1 : overallExitCode;
                        }

                        QueueRow(Markup.Escape(candidate.Manifest.Id), versionCell, reproducedStatus, fixedStatus);
                    }
                }
                finally
                {
                    if (inputCancellation is not null)
                    {
                        inputCancellation.Cancel();
                    }

                    if (inputTask is not null)
                    {
                        try
                        {
                            await inputTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }

                    inputCancellation?.Dispose();
                    _executor.LogObserver = previousObserver;
                    _executor.SuppressConsoleLogOutput = previousSuppression;
                    writer.TryComplete();

                    try
                    {
                        await uiTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }).ConfigureAwait(false);
            if (buildFailures.Count > 0)
            {
                _console.WriteLine();
                foreach (var failure in buildFailures)
                {
                    _console.MarkupLine($"[red]Build failed for {Markup.Escape(failure.ManifestId)} ({Markup.Escape(failure.Variant)}).[/]");

                    if (failure.Output.Count == 0)
                    {
                        _console.MarkupLine("[yellow](No build output captured.)[/]");
                    }
                    else
                    {
                        foreach (var line in failure.Output)
                        {
                            _console.WriteLine(line);
                        }
                    }

                    _console.WriteLine();
                }
            }
        }
        finally
        {
            foreach (var plan in plannedVariants)
            {
                plan.Dispose();
            }
        }

        return overallExitCode;
    }

    private static string BuildVariantIdentifier(string? packageVersion)
    {
        if (string.IsNullOrWhiteSpace(packageVersion))
        {
            return "ver_package";
        }

        var normalized = packageVersion.Replace('.', '_').Replace('-', '_');
        return $"ver_{normalized}";
    }

    private static string? TryResolvePackageVersion(string? projectPath)
    {
        if (projectPath is null || !File.Exists(projectPath))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(projectPath);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;

            var versionElement = document
                .Descendants(ns + "LiteDBPackageVersion")
                .FirstOrDefault();

            if (versionElement is not null && !string.IsNullOrWhiteSpace(versionElement.Value))
            {
                return versionElement.Value.Trim();
            }

            var packageReference = document
                .Descendants(ns + "PackageReference")
                .FirstOrDefault(e => string.Equals(e.Attribute("Include")?.Value, "LiteDB", StringComparison.OrdinalIgnoreCase));

            var version = packageReference?.Attribute("Version")?.Value;
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version!.Trim();
            }
        }
        catch
        {
        }

        return null;
    }

    private sealed record RunCandidate(
        ReproManifest Manifest,
        int Instances,
        int TimeoutSeconds,
        string PackageDisplay,
        RunVariantPlan PackagePlan,
        RunVariantPlan LatestPlan);

    private sealed record BuildFailure(string ManifestId, string Variant, IReadOnlyList<string> Output);

    private static IRenderable CreateLogView(IReadOnlyList<string> lines)
    {
        var logTable = new Table().Border(TableBorder.Rounded);
        logTable.AddColumn(new TableColumn("[bold]Recent Logs[/]").LeftAligned());

        if (lines.Count == 0)
        {
            logTable.AddRow("[dim]No log entries.[/]");
        }
        else
        {
            foreach (var line in lines)
            {
                logTable.AddRow(line);
            }
        }

        return logTable;
    }

    private static string FormatLogLine(ReproExecutionLogEntry entry)
    {
        var levelMarkup = entry.Level switch
        {
            ReproHostLogLevel.Error or ReproHostLogLevel.Critical => "[red]ERR[/]",
            ReproHostLogLevel.Warning => "[yellow]WRN[/]",
            ReproHostLogLevel.Debug => "[grey]DBG[/]",
            ReproHostLogLevel.Trace => "[grey]TRC[/]",
            _ => "[grey]INF[/]"
        };

        return $"{levelMarkup} [dim]#{entry.InstanceIndex}[/] {Markup.Escape(entry.Message)}";
    }

    private static async Task ProcessUiUpdatesAsync(
        ChannelReader<UiUpdate> reader,
        Table table,
        Layout layout,
        List<string> logLines,
        FpsDial fpsDial,
        LiveDisplayContext context,
        CancellationToken cancellationToken)
    {
        var pendingRefresh = false;

        try
        {
            while (true)
            {
                while (reader.TryRead(out var update))
                {
                    switch (update)
                    {
                        case LogLineUpdate logUpdate:
                            logLines.Add(logUpdate.Line);
                            while (logLines.Count > MaxLogLines)
                            {
                                logLines.RemoveAt(0);
                            }

                            layout["logs"].Update(CreateLogView(logLines));
                            pendingRefresh = true;
                            break;
                        case TableRowUpdate rowUpdate:
                            table.AddRow(rowUpdate.ReproId, rowUpdate.Version, rowUpdate.Reproduced, rowUpdate.Fixed);
                            pendingRefresh = true;
                            break;
                        case FpsDialUpdate dialUpdate:
                            layout["controls"].Update(CreateFpsView(fpsDial));
                            pendingRefresh = true;
                            break;
                    }
                }

                if (!pendingRefresh)
                {
                    if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }

                    continue;
                }

                var timestamp = DateTimeOffset.UtcNow;

                if (fpsDial.TryConsumeFrame(timestamp))
                {
                    context.Refresh();
                    pendingRefresh = false;
                    continue;
                }

                var delay = fpsDial.GetDelayUntilNextFrame(timestamp);

                if (delay <= TimeSpan.Zero)
                {
                    if (fpsDial.TryConsumeFrame(DateTimeOffset.UtcNow))
                    {
                        context.Refresh();
                        pendingRefresh = false;
                    }

                    continue;
                }

                var waitTask = reader.WaitToReadAsync(cancellationToken).AsTask();
                var delayTask = Task.Delay(delay, cancellationToken);
                var completed = await Task.WhenAny(waitTask, delayTask).ConfigureAwait(false);

                if (completed == delayTask)
                {
                    await delayTask.ConfigureAwait(false);

                    if (fpsDial.TryConsumeFrame(DateTimeOffset.UtcNow))
                    {
                        context.Refresh();
                        pendingRefresh = false;
                    }
                }
                else
                {
                    if (!await waitTask.ConfigureAwait(false))
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        if (pendingRefresh)
        {
            context.Refresh();
            fpsDial.MarkRefreshed(DateTimeOffset.UtcNow);
        }
    }

    private abstract record UiUpdate;

    private sealed record LogLineUpdate(string Line) : UiUpdate;

    private sealed record TableRowUpdate(string ReproId, string Version, string Reproduced, string Fixed) : UiUpdate;

    private sealed record FpsDialUpdate(decimal Value) : UiUpdate;

    private sealed class FpsDial
    {
        private readonly decimal _min;
        private readonly decimal _max;
        private readonly decimal _step;
        private decimal _value;
        private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
        private readonly object _syncRoot = new();

        public FpsDial(decimal value, decimal min, decimal max, decimal step)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"FPS value must be between {min} and {max}.");
            }

            if (step <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(step), "FPS step must be positive.");
            }

            _value = value;
            _min = min;
            _max = max;
            _step = step;
        }

        public decimal Value
        {
            get
            {
                lock (_syncRoot)
                {
                    return _value;
                }
            }
        }

        public decimal Minimum => _min;

        public decimal Maximum => _max;

        public bool TryIncrease(out decimal newValue)
        {
            lock (_syncRoot)
            {
                if (_value >= _max)
                {
                    newValue = _value;
                    return false;
                }

                _value = Math.Min(_value + _step, _max);
                _lastRefresh = DateTimeOffset.MinValue;
                newValue = _value;
                return true;
            }
        }

        public bool TryDecrease(out decimal newValue)
        {
            lock (_syncRoot)
            {
                if (_value <= _min)
                {
                    newValue = _value;
                    return false;
                }

                _value = Math.Max(_value - _step, _min);
                _lastRefresh = DateTimeOffset.MinValue;
                newValue = _value;
                return true;
            }
        }

        public bool TryConsumeFrame(DateTimeOffset timestamp)
        {
            lock (_syncRoot)
            {
                var fps = (double)_value;

                if (fps <= 0d)
                {
                    _lastRefresh = timestamp;
                    return true;
                }

                var frameDuration = TimeSpan.FromSeconds(1d / fps);

                if (_lastRefresh == DateTimeOffset.MinValue)
                {
                    _lastRefresh = timestamp;
                    return true;
                }

                if (timestamp - _lastRefresh >= frameDuration)
                {
                    _lastRefresh = timestamp;
                    return true;
                }

                return false;
            }
        }

        public void MarkRefreshed(DateTimeOffset timestamp)
        {
            lock (_syncRoot)
            {
                _lastRefresh = timestamp;
            }
        }

        public TimeSpan GetDelayUntilNextFrame(DateTimeOffset timestamp)
        {
            lock (_syncRoot)
            {
                var fps = (double)_value;

                if (fps <= 0d || _lastRefresh == DateTimeOffset.MinValue)
                {
                    return TimeSpan.Zero;
                }

                var frameDuration = TimeSpan.FromSeconds(1d / fps);
                var elapsed = timestamp - _lastRefresh;

                if (elapsed >= frameDuration)
                {
                    return TimeSpan.Zero;
                }

                return frameDuration - elapsed;
            }
        }
    }

    private static async Task MonitorFpsInputAsync(FpsDial dial, ChannelWriter<UiUpdate> writer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        var handled = false;
                        decimal value = dial.Value;

                        switch (key.Key)
                        {
                            case ConsoleKey.Add:
                            case ConsoleKey.OemPlus:
                            case ConsoleKey.UpArrow:
                            case ConsoleKey.RightArrow:
                                handled = dial.TryIncrease(out value);
                                break;
                            case ConsoleKey.Subtract:
                            case ConsoleKey.OemMinus:
                            case ConsoleKey.DownArrow:
                            case ConsoleKey.LeftArrow:
                                handled = dial.TryDecrease(out value);
                                break;
                        }

                        if (handled)
                        {
                            writer.TryWrite(new FpsDialUpdate(value));
                        }
                    }
                    else
                    {
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static IRenderable CreateFpsView(FpsDial dial)
    {
        var range = dial.Maximum - dial.Minimum;
        var normalized = range == 0m
            ? 1d
            : (double)((dial.Value - dial.Minimum) / range);
        normalized = Math.Clamp(normalized, 0d, 1d);
        const int segments = 20;
        var filledSegments = Math.Clamp((int)Math.Round(normalized * segments), 0, segments);
        var barBuilder = new StringBuilder();
        barBuilder.Append('[');
        barBuilder.Append(new string('=', Math.Max(0, filledSegments)));
        barBuilder.Append(new string(' ', Math.Max(0, segments - filledSegments)));
        barBuilder.Append(']');

        var barMarkup = Markup.Escape(barBuilder.ToString());

        var markup = new StringBuilder()
            .AppendLine("[bold]FPS Dial[/]")
            .AppendLine($"[cyan]{dial.Value:F1}[/] fps")
            .AppendLine($"[green]{barMarkup}[/]")
            .Append("[grey]Use +/- or arrow keys to adjust[/]");

        return new Panel(new Markup(markup.ToString()))
        {
            Border = BoxBorder.Rounded,
            Expand = true
        };
    }
}
