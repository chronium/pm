using PM.Files;
using PM.Project;

namespace PM.Application;

public interface IProjectConfigPersistence
{
    string ReadText();
    void WriteTextAtomic(string yaml);
    bool Reload();
}

public sealed class ProjectConfigPersistence(ProjectRoot projectRoot) : IProjectConfigPersistence
{
    public string ReadText() => FileSystem.ReadAllText(projectRoot.ConfigPath);

    public void WriteTextAtomic(string yaml) => FileSystem.WriteAllTextAtomic(projectRoot.ConfigPath, yaml);

    public bool Reload() => projectRoot.TryReloadConfig();
}
