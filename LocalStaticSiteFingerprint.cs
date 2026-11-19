using System.Security.Cryptography;
using System.Text;

namespace UpdateManager.StaticSiteManagementCoreLibrary;
public static class LocalStaticSiteFingerprint
{
    private static readonly HashSet<string> _ignoredDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "bin",
            "obj",
            ".vs",
            ".git"
        };

    public static async Task<string> ComputeAsync(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        projectDirectory = Path.GetFullPath(projectDirectory);

        using IncrementalHash fingerprint =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (string file in EnumerateSourceFiles(projectDirectory)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            string relativePath = Path.GetRelativePath(projectDirectory, file)
                .Replace('\\', '/');

            // Include the path so renaming a file changes the fingerprint.
            fingerprint.AppendData(Encoding.UTF8.GetBytes(relativePath + "\0"));

            await using FileStream contents = File.OpenRead(file);
            fingerprint.AppendData(await SHA256.HashDataAsync(contents));
        }

        return Convert.ToHexString(fingerprint.GetHashAndReset());
    }

    private static IEnumerable<string> EnumerateSourceFiles(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            if (!file.EndsWith(".user", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (string subdirectory in Directory.EnumerateDirectories(directory))
        {
            if (_ignoredDirectories.Contains(Path.GetFileName(subdirectory)) ||
                (File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            foreach (string file in EnumerateSourceFiles(subdirectory))
            {
                yield return file;
            }
        }
    }
}