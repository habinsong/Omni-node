using System.Text;
namespace OmniNode.Middleware;

public sealed class FileRoutingPolicyStore
{
    private readonly IStatePathResolver _pathResolver;

    public FileRoutingPolicyStore(IStatePathResolver pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public RoutingPolicy LoadOverrides()
    {
        var path = _pathResolver.GetRoutingPolicyPath();
        if (!File.Exists(path))
        {
            return new RoutingPolicy();
        }

        try
        {
            var json = AtomicFileStore.ReadAllTextWithBackup(
                path,
                RoutingPolicyJson.IsValidPolicyJson,
                Encoding.UTF8,
                "routing-policy-store"
            );
            if (string.IsNullOrWhiteSpace(json))
            {
                return new RoutingPolicy();
            }

            return RoutingPolicyJson.DeserializePolicy(json);
        }
        catch
        {
            return new RoutingPolicy();
        }
    }

    public void SaveOverrides(RoutingPolicy policy)
    {
        var path = _pathResolver.GetRoutingPolicyPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _pathResolver.StateRootDir);
        AtomicFileStore.WriteAllText(
            path,
            RoutingPolicyJson.SerializePolicy(policy, indented: true),
            ownerOnly: true
        );
    }

    public void DeleteOverrides()
    {
        var path = _pathResolver.GetRoutingPolicyPath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
