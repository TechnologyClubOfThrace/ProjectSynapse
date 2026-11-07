namespace Synapse.Audio;

public sealed record CompositionCatalogItem(
    string Directory,
    string ManifestPath,
    AudioManifest Manifest)
{
    public string Name => Manifest.Composition;
    public override string ToString() => Name;
}

public sealed record CompositionCatalogIssue(string Directory, string Message);

public sealed record CompositionCatalogResult(
    IReadOnlyList<CompositionCatalogItem> Compositions,
    IReadOnlyList<CompositionCatalogIssue> Issues);

public static class CompositionCatalog
{
    public static CompositionCatalogResult Discover(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Composition library not found: {root}");
        }

        var rootManifest = Path.Combine(root, "manifest.json");
        if (File.Exists(rootManifest))
        {
            return new CompositionCatalogResult([CreateItem(root, rootManifest)], []);
        }

        var items = new List<CompositionCatalogItem>();
        var issues = new List<CompositionCatalogIssue>();
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    items.Add(CreateItem(directory, manifestPath));
                }
                catch (Exception exception)
                {
                    issues.Add(new CompositionCatalogIssue(directory, exception.Message));
                }
            }
        }

        if (items.Count == 0)
        {
            var details = issues.Count == 0 ? string.Empty : $" First error: {issues[0].Message}";
            throw new InvalidDataException(
                $"No valid compositions with manifest.json were found in: {root}.{details}");
        }

        var uniqueItems = new List<CompositionCatalogItem>();
        foreach (var group in items.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            uniqueItems.Add(group.First());
            foreach (var duplicate in group.Skip(1))
            {
                issues.Add(new CompositionCatalogIssue(
                    duplicate.Directory,
                    $"Duplicate composition name: {duplicate.Name}"));
            }
        }

        return new CompositionCatalogResult(uniqueItems, issues);
    }

    private static CompositionCatalogItem CreateItem(string directory, string manifestPath)
    {
        var manifest = AudioManifestValidator.LoadManifest(manifestPath);
        return new CompositionCatalogItem(
            Path.GetFullPath(directory),
            Path.GetFullPath(manifestPath),
            manifest);
    }
}
