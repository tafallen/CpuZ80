namespace CpuZ80.TestSupport;

/// <summary>
/// Finds a ROM image somewhere in the working copy.
/// </summary>
/// <remarks>
/// ROM images are copyrighted and gitignored, so tests that need one skip when
/// it is absent. That makes a locator that quietly fails to find a present file
/// dangerous: the test still passes, and it looks like it ran. So this searches
/// the whole tree rather than assuming a layout — the images have already been
/// reorganised once, which silently disarmed both boot tests.
///
/// Use <see cref="Found"/> to assert a test really executed when it should have.
/// </remarks>
public static class RomLocator
{
    private static readonly Dictionary<string, string?> Cache = [];
    private static readonly object Gate = new();

    /// <summary>Locates <paramref name="fileName"/>, or null if it is not in the working copy.</summary>
    public static string? Find(string fileName)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(fileName, out string? cached)) return cached;

            string? found = Search(fileName);
            Cache[fileName] = found;
            return found;
        }
    }

    private static string? Search(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            // The repo root is the useful place to search from, and it is the
            // first ancestor holding a .git directory or the solution file.
            if (dir.EnumerateDirectories(".git").Any() || dir.EnumerateFiles("*.sln").Any())
            {
                return SearchTree(dir, fileName);
            }
            dir = dir.Parent;
        }

        // No repo root above us — fall back to the ancestor chain itself.
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? SearchTree(DirectoryInfo root, string fileName)
    {
        string direct = Path.Combine(root.FullName, fileName);
        if (File.Exists(direct)) return direct;

        foreach (var sub in root.EnumerateDirectories())
        {
            // Build output holds copies of nothing useful and a lot of files.
            if (sub.Name is "bin" or "obj" or ".git") continue;

            string? hit = SearchTree(sub, fileName);
            if (hit is not null) return hit;
        }
        return null;
    }
}
