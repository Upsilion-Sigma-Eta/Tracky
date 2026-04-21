namespace Tracky.Infrastructure.Persistence;

public sealed class TrackyWorkspacePathProvider(string rootDirectory)
{
    private readonly string _rootDirectory = rootDirectory;

    public TrackyWorkspacePathProvider()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tracky"))
    {
    }

    public string GetDatabasePath()
    {
        var workspaceDirectory = Path.Combine(_rootDirectory, "workspaces", "default");
        Directory.CreateDirectory(workspaceDirectory);
        return Path.Combine(workspaceDirectory, "tracky.db");
    }
}
