using System.Text;
using LiteDB.ReproRunner.Cli.Manifests;

namespace LiteDB.ReproRunner.Cli.Execution;

internal sealed class RunDirectoryPlanner
{
    private readonly string _runsRoot;

    public RunDirectoryPlanner()
    {
        _runsRoot = Path.Combine(AppContext.BaseDirectory, "runs");
    }

    public RunVariantPlan CreateVariantPlan(
        DiscoveredRepro repro,
        string manifestIdentifier,
        string variantIdentifier,
        string displayName,
        bool useProjectReference)
    {
        if (repro is null)
        {
            throw new ArgumentNullException(nameof(repro));
        }

        if (string.IsNullOrWhiteSpace(manifestIdentifier))
        {
            manifestIdentifier = repro.Manifest?.Id ?? repro.RawId ?? "repro";
        }

        if (string.IsNullOrWhiteSpace(variantIdentifier))
        {
            variantIdentifier = useProjectReference ? "ver_latest" : "ver_package";
        }

        var manifestSegment = Sanitize(manifestIdentifier);
        var variantSegment = Sanitize(variantIdentifier);

        var variantRoot = Path.Combine(_runsRoot, manifestSegment, variantSegment);
        PurgeDirectory(variantRoot);
        Directory.CreateDirectory(variantRoot);

        var buildOutput = Path.Combine(variantRoot, "build");
        Directory.CreateDirectory(buildOutput);

        var executionRoot = Path.Combine(variantRoot, "run");
        Directory.CreateDirectory(executionRoot);

        return new RunVariantPlan(
            repro,
            displayName,
            useProjectReference,
            manifestIdentifier,
            variantIdentifier,
            variantRoot,
            buildOutput,
            executionRoot,
            static path => PurgeDirectory(path));
    }

    private static string Sanitize(string value)
    {
        var buffer = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0)
            {
                buffer.Append('_');
            }
            else
            {
                buffer.Append(ch);
            }
        }

        return buffer.ToString();
    }

    private static void PurgeDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

internal sealed class RunVariantPlan : IDisposable
{
    private readonly Action<string> _cleanup;

    public RunVariantPlan(
        DiscoveredRepro repro,
        string displayName,
        bool useProjectReference,
        string manifestIdentifier,
        string variantIdentifier,
        string rootDirectory,
        string buildOutputDirectory,
        string executionRootDirectory,
        Action<string> cleanup)
    {
        Repro = repro ?? throw new ArgumentNullException(nameof(repro));
        DisplayName = displayName;
        UseProjectReference = useProjectReference;
        ManifestIdentifier = manifestIdentifier;
        VariantIdentifier = variantIdentifier;
        RootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        BuildOutputDirectory = buildOutputDirectory ?? throw new ArgumentNullException(nameof(buildOutputDirectory));
        ExecutionRootDirectory = executionRootDirectory ?? throw new ArgumentNullException(nameof(executionRootDirectory));
        _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
    }

    public DiscoveredRepro Repro { get; }

    public string DisplayName { get; }

    public bool UseProjectReference { get; }

    public string ManifestIdentifier { get; }

    public string VariantIdentifier { get; }

    public string RootDirectory { get; }

    public string BuildOutputDirectory { get; }

    public string ExecutionRootDirectory { get; }

    public void Dispose()
    {
        try
        {
            _cleanup(RootDirectory);
        }
        catch
        {
        }
    }
}
