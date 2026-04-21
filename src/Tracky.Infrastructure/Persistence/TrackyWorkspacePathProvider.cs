namespace Tracky.Infrastructure.Persistence;

public sealed class TrackyWorkspacePathProvider
{
    private readonly string _rootDirectory;

    public TrackyWorkspacePathProvider()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tracky"))
    {
    }

    public TrackyWorkspacePathProvider(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
    }

    public string GetDatabasePath()
    {
        var workspaceDirectory = Path.Combine(_rootDirectory, "workspaces", "default");
        Directory.CreateDirectory(workspaceDirectory);
        return Path.Combine(workspaceDirectory, "tracky.db");
    }
}
