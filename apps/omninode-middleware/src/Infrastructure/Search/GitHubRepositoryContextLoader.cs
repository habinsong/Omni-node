using System.Net;

namespace OmniNode.Middleware;

internal readonly record struct SearchRepositoryContextSnapshot(
    string RepositorySlug,
    string Description,
    string ReadmeText
);

internal sealed class GitHubRepositoryContextLoader
{
    private const string UserAgent = "Omni-node/1.0";
    private readonly HttpClient _httpClient;

    public GitHubRepositoryContextLoader(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<SearchRepositoryContextSnapshot?> TryLoadAsync(
        string input,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default
    )
    {
        if (urls.Count != 1)
        {
            return null;
        }

        var primaryUrl = urls[0];
        if (!SearchUrlContextPolicy.TryParseGitHubRepositoryRoot(primaryUrl, out var owner, out var repo))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, primaryUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var description = SearchUrlContextPolicy.TryExtractHtmlMetaContent(html, "description");
            var (refName, readmePath, readmeFallbackText) = SearchUrlContextPolicy.TryExtractGitHubReadmeInfo(html);
            var readmeText = await TryFetchReadmeRawAsync(owner, repo, refName, readmePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(readmeText))
            {
                readmeText = readmeFallbackText;
            }

            readmeText = SearchUrlContextPolicy.BuildRelevantRepositoryExcerpt(input, readmeText);
            if (string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(readmeText))
            {
                return null;
            }

            return new SearchRepositoryContextSnapshot(
                $"{owner}/{repo}",
                description,
                readmeText
            );
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> TryFetchReadmeRawAsync(
        string owner,
        string repo,
        string refName,
        string readmePath,
        CancellationToken cancellationToken
    )
    {
        var normalizedOwner = (owner ?? string.Empty).Trim();
        var normalizedRepo = (repo ?? string.Empty).Trim();
        var normalizedRef = (refName ?? string.Empty).Trim();
        var normalizedPath = (readmePath ?? string.Empty).Trim();
        if (normalizedOwner.Length == 0
            || normalizedRepo.Length == 0
            || normalizedRef.Length == 0
            || normalizedPath.Length == 0)
        {
            return string.Empty;
        }

        var escapedPath = string.Join(
            "/",
            normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Uri.EscapeDataString)
        );
        var rawUrl = $"https://raw.githubusercontent.com/{Uri.EscapeDataString(normalizedOwner)}/{Uri.EscapeDataString(normalizedRepo)}/{Uri.EscapeDataString(normalizedRef)}/{escapedPath}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, rawUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            return (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
