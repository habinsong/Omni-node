namespace OmniNode.Middleware;

public sealed class RoutingPolicyResolver
{
    private static readonly string[] SupportedProviders =
    {
        "gemini",
        "groq",
        "nvidia",
        "cerebras",
        "copilot",
        "codex"
    };

    private readonly FileRoutingPolicyStore _store;
    private readonly object _gate = new();
    private readonly RoutingPolicy _defaultPolicy;
    private RoutingPolicy _overridePolicy;
    private RoutingDecision? _lastDecision;

    public RoutingPolicyResolver(FileRoutingPolicyStore store)
    {
        _store = store;
        _defaultPolicy = BuildDefaultPolicy();
        _overridePolicy = NormalizePolicy(_store.LoadOverrides());
    }

    public RoutingPolicyActionResult GetSnapshotResult(string message = "라우팅 정책을 불러왔습니다.")
    {
        return new RoutingPolicyActionResult(true, message, BuildSnapshot());
    }

    public RoutingPolicyActionResult SaveOverrides(RoutingPolicy? incoming)
    {
        var normalized = NormalizePolicy(incoming ?? new RoutingPolicy());
        lock (_gate)
        {
            _overridePolicy = normalized.Clone();
            _store.SaveOverrides(_overridePolicy);
            return new RoutingPolicyActionResult(true, "라우팅 override를 저장했습니다.", BuildSnapshotCore());
        }
    }

    public RoutingPolicyActionResult ResetOverrides()
    {
        lock (_gate)
        {
            _overridePolicy = new RoutingPolicy();
            _store.DeleteOverrides();
            return new RoutingPolicyActionResult(true, "라우팅 override를 초기화했습니다.", BuildSnapshotCore());
        }
    }

    public RoutingDecision? GetLastDecision()
    {
        lock (_gate)
        {
            return _lastDecision;
        }
    }

    public IReadOnlyList<string> ResolveProviderChain(TaskCategory category)
    {
        lock (_gate)
        {
            return ResolveProviderChainCore(category);
        }
    }

    public RoutingDecision ResolveDecision(
        TaskCategory category,
        string? requestedProvider,
        IReadOnlyCollection<ProviderAvailability> availabilitySnapshot,
        IReadOnlyDictionary<string, string?>? selectionByProvider = null,
        bool allowRequestedOverride = true,
        string? reason = null
    )
    {
        selectionByProvider ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            var chain = ResolveProviderChainCore(category);
            var availableProviders = availabilitySnapshot
                .Where(item => IsSelectable(item, selectionByProvider))
                .Select(item => item.Provider)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var normalizedRequested = NormalizeProviderKey(requestedProvider, allowAuto: true);
            var resolvedProvider = "none";
            var resolvedReason = string.IsNullOrWhiteSpace(reason) ? "category_chain" : reason.Trim();

            if (allowRequestedOverride
                && normalizedRequested != "auto"
                && normalizedRequested != "none"
                && availableProviders.Contains(normalizedRequested, StringComparer.OrdinalIgnoreCase))
            {
                resolvedProvider = normalizedRequested;
                resolvedReason = string.IsNullOrWhiteSpace(reason)
                    ? "requested_override"
                    : $"{reason.Trim()}:requested_override";
            }
            else
            {
                resolvedProvider = ResolveFromChain(chain, availabilitySnapshot, selectionByProvider, category);
                if (normalizedRequested != "auto" && normalizedRequested != "none" && resolvedProvider == "none")
                {
                    resolvedReason = string.IsNullOrWhiteSpace(reason)
                        ? "requested_unavailable"
                        : $"{reason.Trim()}:requested_unavailable";
                }
            }

            var decision = new RoutingDecision(
                $"route_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
                category,
                TaskCategoryMetadata.ToPolicyKey(category),
                TaskCategoryMetadata.ToDisplayLabel(category),
                DateTimeOffset.UtcNow,
                normalizedRequested,
                resolvedProvider,
                chain,
                availableProviders,
                resolvedReason
            );
            _lastDecision = decision;
            return decision;
        }
    }

    private RoutingPolicySnapshot BuildSnapshot()
    {
        lock (_gate)
        {
            return BuildSnapshotCore();
        }
    }

    private RoutingPolicySnapshot BuildSnapshotCore()
    {
        var defaults = ToDictionary(_defaultPolicy, includeEmpty: false);
        var overrides = ToDictionary(_overridePolicy, includeEmpty: false);
        var effective = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var category in TaskCategoryMetadata.All)
        {
            effective[TaskCategoryMetadata.ToPolicyKey(category)] = ResolveProviderChainCore(category);
        }

        return new RoutingPolicySnapshot(defaults, overrides, effective, _lastDecision);
    }

    private IReadOnlyList<string> ResolveProviderChainCore(TaskCategory category)
    {
        var overrideChain = NormalizeChain(_overridePolicy.GetChain(category));
        if (overrideChain.Count > 0)
        {
            return overrideChain;
        }

        var defaultChain = NormalizeChain(_defaultPolicy.GetChain(category));
        return defaultChain.Count > 0 ? defaultChain : SupportedProviders;
    }

    private static string ResolveFromChain(
        IReadOnlyList<string> chain,
        IReadOnlyCollection<ProviderAvailability> availabilitySnapshot,
        IReadOnlyDictionary<string, string?> selectionByProvider,
        TaskCategory category
    )
    {
        var preferredByCapability = chain
            .Where(provider =>
                availabilitySnapshot.Any(item =>
                    item.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)
                    && IsSelectable(item, selectionByProvider)
                    && MatchesPreferredCapability(item, category)))
            .ToArray();
        if (preferredByCapability.Length > 0)
        {
            return preferredByCapability[0];
        }

        foreach (var provider in chain)
        {
            var availability = availabilitySnapshot.FirstOrDefault(item =>
                item.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));
            if (availability == null || !IsSelectable(availability, selectionByProvider))
            {
                continue;
            }

            return provider;
        }

        return "none";
    }

    private static bool IsSelectable(
        ProviderAvailability availability,
        IReadOnlyDictionary<string, string?> selectionByProvider
    )
    {
        if (!availability.Available)
        {
            return false;
        }

        if (selectionByProvider.TryGetValue(availability.Provider, out var selection)
            && IsDisabledModelSelection(selection))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesPreferredCapability(ProviderAvailability availability, TaskCategory category)
    {
        return category switch
        {
            TaskCategory.VisualUi => availability.VisualCapable,
            TaskCategory.SearchTimeSensitive => availability.SearchCapable,
            TaskCategory.BackgroundMonitor => availability.BackgroundSafe,
            TaskCategory.DeepCode => availability.CodeCapable,
            TaskCategory.SafeRefactor => availability.CodeCapable,
            TaskCategory.QuickFix => availability.CodeCapable,
            TaskCategory.Documentation => availability.CodeCapable,
            TaskCategory.RoutineBuilder => availability.CodeCapable,
            _ => false
        };
    }

    private static RoutingPolicy BuildDefaultPolicy()
    {
        return new RoutingPolicy
        {
            GeneralChat = new[] { "gemini", "groq", "nvidia", "cerebras", "copilot", "codex" },
            Planner = new[] { "gemini", "groq", "nvidia", "cerebras", "codex", "copilot" },
            Reviewer = new[] { "codex", "gemini", "groq", "nvidia", "cerebras", "copilot" },
            SearchTimeSensitive = new[] { "gemini", "groq", "nvidia", "cerebras", "codex", "copilot" },
            SearchFallback = new[] { "groq", "nvidia", "gemini", "cerebras", "codex", "copilot" },
            DeepCode = new[] { "codex", "copilot", "gemini", "groq", "nvidia", "cerebras" },
            SafeRefactor = new[] { "codex", "copilot", "gemini", "groq", "nvidia", "cerebras" },
            QuickFix = new[] { "groq", "nvidia", "cerebras", "gemini", "copilot", "codex" },
            VisualUi = new[] { "gemini", "codex", "groq", "nvidia", "cerebras", "copilot" },
            RoutineBuilder = new[] { "groq", "nvidia", "gemini", "cerebras", "codex", "copilot" },
            BackgroundMonitor = new[] { "groq", "nvidia", "cerebras", "gemini", "codex", "copilot" },
            Documentation = new[] { "gemini", "codex", "groq", "nvidia", "copilot", "cerebras" }
        };
    }

    private static RoutingPolicy NormalizePolicy(RoutingPolicy policy)
    {
        var normalized = new RoutingPolicy();
        foreach (var category in TaskCategoryMetadata.All)
        {
            normalized.SetChain(category, NormalizeChain(policy.GetChain(category)));
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ToDictionary(RoutingPolicy policy, bool includeEmpty)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var category in TaskCategoryMetadata.All)
        {
            var chain = NormalizeChain(policy.GetChain(category));
            if (!includeEmpty && chain.Count == 0)
            {
                continue;
            }

            result[TaskCategoryMetadata.ToPolicyKey(category)] = chain;
        }

        return result;
    }

    private static string NormalizeProviderKey(string? provider, bool allowAuto)
    {
        var normalized = (provider ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized == "nvidia-nim" || normalized == "nvidia_nim" || normalized == "nim")
        {
            normalized = "nvidia";
        }

        if (normalized.Length == 0)
        {
            return allowAuto ? "auto" : "none";
        }

        if (normalized == "auto" && allowAuto)
        {
            return normalized;
        }

        return SupportedProviders.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : (allowAuto ? "auto" : "none");
    }

    private static IReadOnlyList<string> NormalizeChain(IReadOnlyList<string>? chain)
    {
        if (chain == null || chain.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalized = chain
            .Select(item => NormalizeProviderKey(item, allowAuto: false))
            .Where(item => item != "none")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized;
    }

    private static bool IsDisabledModelSelection(string? selection)
    {
        var normalized = (selection ?? string.Empty).Trim();
        return normalized.Equals("none", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("disabled", StringComparison.OrdinalIgnoreCase);
    }
}
