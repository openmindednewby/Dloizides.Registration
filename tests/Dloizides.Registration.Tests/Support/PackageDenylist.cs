using System.Text.RegularExpressions;

namespace Dloizides.Registration.Tests.Support;

internal static class PackageDenylist
{
    public const string ListVariable = "PACKAGE_DENYLIST";
    public const string FileVariable = "PACKAGE_DENYLIST_FILE";
    public const string RequiredVariable = "PACKAGE_DENYLIST_REQUIRED";
    public const string AncestorFile = ".package-denylist.txt";

    private const int ShortNameLength = 3;

    private static readonly char[] Separators = [',', '\n', '\r'];
    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static IReadOnlyList<string> Load(Func<string, string?> environment, string startDirectory)
    {
        var raw = environment(ListVariable) ?? ReadFile(environment(FileVariable)) ?? ReadAncestor(startDirectory);
        var names = raw?.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (names.Length == 0)
            throw new InvalidOperationException($"No package denylist: set {ListVariable} or {FileVariable}, or place {AncestorFile} above the package.");
        return names;
    }

    public static IReadOnlyList<string> LoadForThisRun() =>
        Load(Environment.GetEnvironmentVariable, AppContext.BaseDirectory);

    public static string? SkipReason(Func<string, string?> environment, string startDirectory)
    {
        if (string.Equals(environment(RequiredVariable), "1", StringComparison.Ordinal))
            return null;
        try
        {
            Load(environment, startDirectory);
            return null;
        }
        catch (InvalidOperationException)
        {
            return $"No package denylist here; set {RequiredVariable}=1 to make this fail instead.";
        }
    }

    public static IReadOnlyList<string> FilesNaming(string root, IReadOnlyList<string> names)
    {
        var patterns = Patterns(names);
        return ScannableFiles(root)
            .Where(path => Names(patterns, Path.GetRelativePath(root, path)) || Names(patterns, File.ReadAllText(path)))
            .ToList();
    }

    public static IReadOnlyList<string> ScannableFiles(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(IsShipped).ToList();

    private static bool Names(List<Regex> patterns, string text) => patterns.Exists(pattern => pattern.IsMatch(text));

    private static List<Regex> Patterns(IReadOnlyList<string> names)
    {
        var patterns = new List<Regex>();
        var longNames = names.Where(name => name.Length > ShortNameLength).Select(Regex.Escape).ToList();
        if (longNames.Count > 0)
            patterns.Add(Compile(string.Join('|', longNames), RegexOptions.IgnoreCase));
        patterns.AddRange(names.Where(name => name.Length <= ShortNameLength).Select(name => Compile(ShortNamePattern(name), RegexOptions.None)));
        return patterns;
    }

    private static string ShortNamePattern(string name)
    {
        var lower = name.ToLowerInvariant();
        var upper = name.ToUpperInvariant();
        var title = string.Concat(upper.AsSpan(0, 1), lower.AsSpan(1));
        return $"(?<![A-Za-z]){lower}(?![a-z])|(?<![A-Z]){title}(?![a-z])|(?<![A-Z]){upper}(?![a-z])";
    }

    private static Regex Compile(string pattern, RegexOptions options) =>
        new(pattern, options | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    private static bool IsShipped(string path)
    {
        var segments = path.Split(PathSeparators);
        return !Array.Exists(segments, segment => segment is "bin" or "obj" or "artifacts" or ".git" or ".vs" or "TestResults");
    }

    private static string? ReadFile(string? path) => path is null ? null : File.ReadAllText(path);

    private static string? ReadAncestor(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, AncestorFile);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        return null;
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class DenylistFactAttribute : FactAttribute
{
    public DenylistFactAttribute() =>
        Skip = PackageDenylist.SkipReason(Environment.GetEnvironmentVariable, AppContext.BaseDirectory);
}
