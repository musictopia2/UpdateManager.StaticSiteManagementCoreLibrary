namespace UpdateManager.StaticSiteManagementCoreLibrary;
internal static class StaticSiteHostRegistryContent
{
    internal static HashSet<string> ReadFolders(string content)
    {
        HashSet<string> folders =
            new(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(content))
        {
            return folders;
        }

        string[] lines = content.Split('\n');

        string[] headers = lines[0]
            .TrimStart('\uFEFF')
            .TrimEnd('\r')
            .Split('\t');

        int folderIndex = Array.FindIndex(
            headers,
            header => string.Equals(
                header.Trim(),
                "Folder",
                StringComparison.OrdinalIgnoreCase));

        if (folderIndex < 0)
        {
            throw new InvalidOperationException(
                "The existing apps.txt has no Folder column.");
        }

        foreach (string line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] columns = line.TrimEnd('\r').Split('\t');

            if (columns.Length != headers.Length)
            {
                throw new InvalidOperationException(
                    "The existing apps.txt contains an invalid row.");
            }

            string folder = columns[folderIndex].Trim();

            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidOperationException(
                    "The existing apps.txt contains an empty Folder value.");
            }

            folders.Add(folder);
        }

        return folders;
    }
}
