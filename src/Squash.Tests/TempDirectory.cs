public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SquashTests", Guid.NewGuid().ToString("N"));

    public TempDirectory() =>
        Directory.CreateDirectory(Path);

    public string Combine(params string[] parts) =>
        System.IO.Path.Combine([Path, .. parts]);

    public string Write(string relative, string content)
    {
        var path = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
