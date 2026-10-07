namespace Squash;

static class Files
{
    /// <summary>
    /// Moves a file over whatever is at the destination. Retried, because a virus scanner or an
    /// indexer often holds a freshly written assembly open for a moment.
    /// </summary>
    public static void Move(string source, string destination)
    {
        for (var attempt = 1;; attempt++)
        {
            try
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(source, destination);
                return;
            }
            catch (Exception exception) when (attempt < 5 &&
                                              exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }

    /// <summary>
    /// Leaves a file that already has this content alone, timestamp included: it may be an input
    /// the build compares against.
    /// </summary>
    public static bool WriteIfDifferent(string path, string content)
    {
        if (File.Exists(path) &&
            File.ReadAllText(path) == content)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return true;
    }

    public static void RecreateDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }

        Directory.CreateDirectory(directory);
    }
}
