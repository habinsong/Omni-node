using System.Text;

namespace OmniNode.Middleware;

public sealed class SkillManifestLoader
{
    private readonly IStatePathResolver _pathResolver;

    public SkillManifestLoader(IStatePathResolver pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public IReadOnlyList<SkillManifest> Load(string projectRoot)
    {
        var items = new List<SkillManifest>();
        LoadDirectory(Path.Combine(projectRoot, ".omni", "skills"), "project");
        LoadDirectory(_pathResolver.GetGlobalSkillsRoot(), "global");
        return items
            .OrderBy(item => item.Scope.Equals("project", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        void LoadDirectory(string root, string scope)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var path in Directory.GetFiles(root, "SKILL.md", SearchOption.AllDirectories))
            {
                var manifest = ParseSkillManifest(path, scope);
                if (manifest != null)
                {
                    items.Add(manifest);
                }
            }
        }
    }

    private static SkillManifest? ParseSkillManifest(string path, string scope)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch
        {
            return null;
        }

        var name = string.Empty;
        var description = string.Empty;
        foreach (var rawLine in lines.Take(40))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line == "---")
            {
                continue;
            }

            if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            {
                name = line["name:".Length..].Trim().Trim('"');
                continue;
            }

            if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
            {
                description = line["description:".Length..].Trim().Trim('"');
                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                name = name.Length == 0 ? line.TrimStart('#', ' ') : name;
                continue;
            }

            if (description.Length == 0)
            {
                description = line;
            }
        }

        if (name.Length == 0)
        {
            var directoryName = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
            name = string.IsNullOrWhiteSpace(directoryName) ? Path.GetFileNameWithoutExtension(path) : directoryName;
        }

        if (description.Length == 0)
        {
            description = "설명이 없습니다.";
        }

        return new SkillManifest(name, description, path, scope);
    }
}
