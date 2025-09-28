using System.Xml.Linq;
using LiteDB.ReproRunner.Cli.Execution;
using LiteDB.ReproRunner.Cli.Infrastructure;
using LiteDB.ReproRunner.Cli.Manifests;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LiteDB.ReproRunner.Cli.Commands;

internal sealed class RunCommand : AsyncCommand<RunCommandSettings>
{
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
        var overallExitCode = 0;
        var plannedVariants = new List<RunVariantPlan>();
        var buildFailures = new List<BuildFailure>();

        try
        {
            await _console.Live(table).StartAsync(async ctx =>
            {
                var candidates = new List<RunCandidate>();

                foreach (var repro in selected)
                {
                    _cancellationToken.ThrowIfCancellationRequested();

                    if (!repro.IsValid)
                    {
                        if (!settings.SkipValidation)
                        {
                            table.AddRow(Markup.Escape(repro.RawId ?? "(unknown)"), "[red]Invalid[/]", "[red]❌[/]", "[red]❌[/]");
                            ctx.Refresh();
                            overallExitCode = overallExitCode == 0 ? 2 : overallExitCode;
                            continue;
                        }

                        if (repro.Manifest is null)
                        {
                            table.AddRow(Markup.Escape(repro.RawId ?? "(unknown)"), "[red]Invalid[/]", "[red]❌[/]", "[red]❌[/]");
                            ctx.Refresh();
                            overallExitCode = overallExitCode == 0 ? 2 : overallExitCode;
                            continue;
                        }
                    }

                    if (repro.Manifest is null)
                    {
                        table.AddRow(Markup.Escape(repro.RawId ?? "(unknown)"), "[red]Missing[/]", "[red]❌[/]", "[red]❌[/]");
                        ctx.Refresh();
                        overallExitCode = overallExitCode == 0 ? 2 : overallExitCode;
                        continue;
                    }

                    var manifest = repro.Manifest;
                    var instances = settings.Instances ?? manifest.DefaultInstances;

                    if (manifest.RequiresParallel && instances < 2)
                    {
                        table.AddRow(Markup.Escape(manifest.Id), "[red]Config Error[/]", "[red]❌[/]", "[red]❌[/]");
                        ctx.Refresh();
                        overallExitCode = 1;
                        continue;
                    }

                    if (repro.ProjectPath is null)
                    {
                        table.AddRow(Markup.Escape(manifest.Id), "[red]Project Missing[/]", "[red]❌[/]", "[red]❌[/]");
                        ctx.Refresh();
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

                    table.AddRow(Markup.Escape(candidate.Manifest.Id), versionCell, reproducedStatus, fixedStatus);
                    ctx.Refresh();
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
}
