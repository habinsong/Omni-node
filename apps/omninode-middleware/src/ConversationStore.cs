using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed class ConversationStore : IConversationStore
{
    private static readonly Regex UnsafeTokenRegex = new("[^a-z0-9._-]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly string _statePath;
    private readonly object _lock = new();
    private ConversationState _state = new();

    public ConversationStore(string statePath)
    {
        _statePath = Path.GetFullPath(statePath);
        Load();
    }

    public IReadOnlyList<ConversationThreadSummary> List(string scope, string mode)
    {
        var normalizedScope = NormalizeToken(scope, "chat");
        var normalizedMode = NormalizeToken(mode, "single");

        lock (_lock)
        {
            return _state.Conversations
                .Where(x => x.Scope.Equals(normalizedScope, StringComparison.OrdinalIgnoreCase)
                            && x.Mode.Equals(normalizedMode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(ToSummary)
                .ToArray();
        }
    }

    public IReadOnlyList<ConversationThreadSummary> ListAll()
    {
        lock (_lock)
        {
            return _state.Conversations
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(ToSummary)
                .ToArray();
        }
    }

    public IReadOnlyList<ConversationThreadView> ListAllViews()
    {
        lock (_lock)
        {
            return _state.Conversations
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(ToView)
                .ToArray();
        }
    }

    public ConversationThreadView Create(
        string scope,
        string mode,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    )
    {
        var normalizedScope = NormalizeToken(scope, "chat");
        var normalizedMode = NormalizeToken(mode, "single");
        var resolvedTitle = ResolveTitle(title, normalizedScope, normalizedMode);
        var resolvedProject = NormalizeLabel(project, "기본");
        var resolvedCategory = NormalizeLabel(category, "일반");
        var resolvedTags = NormalizeTags(tags);

        lock (_lock)
        {
            var thread = new ConversationThread
            {
                Id = Guid.NewGuid().ToString("N"),
                Scope = normalizedScope,
                Mode = normalizedMode,
                Title = resolvedTitle,
                Project = resolvedProject,
                Category = resolvedCategory,
                Tags = resolvedTags.ToList(),
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            };

            _state.Conversations.Add(thread);
            SaveLocked();
            return ToView(thread);
        }
    }

    public ConversationThreadView Ensure(
        string scope,
        string mode,
        string? conversationId,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    )
    {
        var normalizedScope = NormalizeToken(scope, "chat");
        var normalizedMode = NormalizeToken(mode, "single");

        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(conversationId))
            {
                var existing = _state.Conversations.FirstOrDefault(x => x.Id.Equals(conversationId.Trim(), StringComparison.Ordinal));
                if (existing != null)
                {
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        existing.Title = title.Trim();
                    }

                    if (project != null)
                    {
                        existing.Project = NormalizeLabel(project, "기본");
                    }

                    if (category != null)
                    {
                        existing.Category = NormalizeLabel(category, "일반");
                    }

                    if (tags != null)
                    {
                        existing.Tags = NormalizeTags(tags).ToList();
                    }

                    existing.Scope = normalizedScope;
                    existing.Mode = normalizedMode;
                    existing.UpdatedUtc = DateTimeOffset.UtcNow;
                    SaveLocked();
                    return ToView(existing);
                }
            }

            var created = new ConversationThread
            {
                Id = Guid.NewGuid().ToString("N"),
                Scope = normalizedScope,
                Mode = normalizedMode,
                Title = ResolveTitle(title, normalizedScope, normalizedMode),
                Project = NormalizeLabel(project, "기본"),
                Category = NormalizeLabel(category, "일반"),
                Tags = NormalizeTags(tags).ToList(),
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            };

            _state.Conversations.Add(created);
            SaveLocked();
            return ToView(created);
        }
    }

    public ConversationThreadView? Get(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        lock (_lock)
        {
            var thread = _state.Conversations.FirstOrDefault(x => x.Id.Equals(conversationId.Trim(), StringComparison.Ordinal));
            return thread == null ? null : ToView(thread);
        }
    }

    public bool Delete(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        lock (_lock)
        {
            var removed = _state.Conversations.RemoveAll(x => x.Id.Equals(conversationId.Trim(), StringComparison.Ordinal));
            if (removed <= 0)
            {
                return false;
            }

            SaveLocked();
            return true;
        }
    }

    public ConversationImportResult ImportConversations(IReadOnlyList<ConversationThreadView> conversations, bool overwrite)
    {
        if (conversations == null || conversations.Count == 0)
        {
            return new ConversationImportResult(0, 0, 0);
        }

        var imported = 0;
        var skipped = 0;
        var overwritten = 0;

        lock (_lock)
        {
            foreach (var view in conversations)
            {
                if (view == null || string.IsNullOrWhiteSpace(view.Id))
                {
                    skipped += 1;
                    continue;
                }

                var normalizedId = view.Id.Trim();
                var existingIndex = _state.Conversations.FindIndex(item => item.Id.Equals(normalizedId, StringComparison.Ordinal));
                if (existingIndex >= 0 && !overwrite)
                {
                    skipped += 1;
                    continue;
                }

                var importedThread = new ConversationThread
                {
                    Id = normalizedId,
                    Scope = NormalizeToken(view.Scope, "chat"),
                    Mode = NormalizeToken(view.Mode, "single"),
                    Title = string.IsNullOrWhiteSpace(view.Title) ? ResolveTitle(null, view.Scope, view.Mode) : view.Title.Trim(),
                    Project = NormalizeLabel(view.Project, "기본"),
                    Category = NormalizeLabel(view.Category, "일반"),
                    Tags = NormalizeTags(view.Tags).ToList(),
                    CreatedUtc = view.CreatedUtc == default ? DateTimeOffset.UtcNow : view.CreatedUtc,
                    UpdatedUtc = view.UpdatedUtc == default ? DateTimeOffset.UtcNow : view.UpdatedUtc,
                    LinkedMemoryNotes = (view.LinkedMemoryNotes ?? Array.Empty<string>())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    LatestCodingResult = view.LatestCodingResult,
                    Messages = (view.Messages ?? Array.Empty<ConversationMessageView>())
                        .Select(message => new ConversationMessage
                        {
                            Role = NormalizeRole(message.Role),
                            Text = (message.Text ?? string.Empty).Trim(),
                            Meta = (message.Meta ?? string.Empty).Trim(),
                            CreatedUtc = message.CreatedUtc == default ? DateTimeOffset.UtcNow : message.CreatedUtc,
                            TokenUsage = message.TokenUsage == null ? null : TokenUsageEstimator.Normalize(message.TokenUsage)
                        })
                        .ToList()
                };

                if (existingIndex >= 0)
                {
                    _state.Conversations[existingIndex] = importedThread;
                    overwritten += 1;
                }
                else
                {
                    _state.Conversations.Add(importedThread);
                    imported += 1;
                }
            }

            if (imported > 0 || overwritten > 0)
            {
                SaveLocked();
            }
        }

        return new ConversationImportResult(imported, skipped, overwritten);
    }

    public int DeleteByScope(string scope, string? mode = null)
    {
        var normalizedScope = NormalizeToken(scope, "chat");
        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? null : NormalizeToken(mode, "single");

        lock (_lock)
        {
            var removed = _state.Conversations.RemoveAll(item =>
                item.Scope.Equals(normalizedScope, StringComparison.OrdinalIgnoreCase)
                && (normalizedMode == null
                    || item.Mode.Equals(normalizedMode, StringComparison.OrdinalIgnoreCase))
            );
            if (removed > 0)
            {
                SaveLocked();
            }

            return removed;
        }
    }

    public ConversationThreadView AppendMessage(string conversationId, string role, string text, string meta, TokenUsage? tokenUsage = null)
    {
        var normalizedRole = NormalizeRole(role);
        var safeText = (text ?? string.Empty).Trim();
        var safeMeta = (meta ?? string.Empty).Trim();
        var safeUsage = tokenUsage == null ? null : TokenUsageEstimator.Normalize(tokenUsage);

        lock (_lock)
        {
            var thread = RequireThreadLocked(conversationId);
            thread.Messages.Add(new ConversationMessage
            {
                Role = normalizedRole,
                Text = safeText,
                Meta = safeMeta,
                CreatedUtc = DateTimeOffset.UtcNow,
                TokenUsage = safeUsage
            });
            thread.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveLocked();
            return ToView(thread);
        }
    }

    public ConversationThreadView SetLatestCodingResult(string conversationId, ConversationCodingResultSnapshot? result)
    {
        lock (_lock)
        {
            var thread = RequireThreadLocked(conversationId);
            thread.LatestCodingResult = result;
            thread.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveLocked();
            return ToView(thread);
        }
    }

    // 활성 스킬 이름을 thread에 영구 저장. null/empty 전달 시 해제.
    public void SetActiveSkillName(string conversationId, string? skillName)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return;
        lock (_lock)
        {
            if (!_state.Conversations.Any(t => string.Equals(t.Id, conversationId, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            var thread = RequireThreadLocked(conversationId);
            var normalized = string.IsNullOrWhiteSpace(skillName) ? null : skillName.Trim();
            if (string.Equals(thread.ActiveSkillName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            thread.ActiveSkillName = normalized;
            thread.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveLocked();
        }
    }

    // 영구 저장된 활성 스킬을 모두 읽어 (conversationId, skillName) 쌍을 반환. 시작 시 메모리 dictionary 복원에 사용.
    public IReadOnlyList<(string ConversationId, string SkillName)> ListActiveSkillBindings()
    {
        lock (_lock)
        {
            return _state.Conversations
                .Where(t => !string.IsNullOrWhiteSpace(t.ActiveSkillName))
                .Select(t => (t.Id, t.ActiveSkillName!))
                .ToArray();
        }
    }

    public ConversationThreadView SetLinkedMemoryNotes(string conversationId, IReadOnlyList<string> names)
    {
        lock (_lock)
        {
            var thread = RequireThreadLocked(conversationId);
            thread.LinkedMemoryNotes = names
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            thread.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveLocked();
            return ToView(thread);
        }
    }

    public ConversationThreadView AddLinkedMemoryNote(string conversationId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Get(conversationId) ?? throw new InvalidOperationException("conversation not found");
        }

        lock (_lock)
        {
            var thread = RequireThreadLocked(conversationId);
            if (!thread.LinkedMemoryNotes.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                thread.LinkedMemoryNotes.Add(name.Trim());
                thread.UpdatedUtc = DateTimeOffset.UtcNow;
                SaveLocked();
            }

            return ToView(thread);
        }
    }

    public int RemoveLinkedMemoryNotes(IReadOnlyList<string> names)
    {
        if (names == null || names.Count == 0)
        {
            return 0;
        }

        var targets = names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 0)
        {
            return 0;
        }

        lock (_lock)
        {
            var updatedCount = 0;
            foreach (var thread in _state.Conversations)
            {
                if (thread.LinkedMemoryNotes.Count == 0)
                {
                    continue;
                }

                var filtered = thread.LinkedMemoryNotes
                    .Where(x => !targets.Contains(x.Trim()))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (filtered.Count == thread.LinkedMemoryNotes.Count)
                {
                    continue;
                }

                thread.LinkedMemoryNotes = filtered;
                thread.UpdatedUtc = DateTimeOffset.UtcNow;
                updatedCount += 1;
            }

            if (updatedCount > 0)
            {
                SaveLocked();
            }

            return updatedCount;
        }
    }

    public int RenameLinkedMemoryNote(string oldName, string newName)
    {
        var safeOld = (oldName ?? string.Empty).Trim();
        var safeNew = (newName ?? string.Empty).Trim();
        if (safeOld.Length == 0 || safeNew.Length == 0)
        {
            return 0;
        }

        lock (_lock)
        {
            var updatedCount = 0;
            foreach (var thread in _state.Conversations)
            {
                if (thread.LinkedMemoryNotes.Count == 0)
                {
                    continue;
                }

                var changed = false;
                var renamed = new List<string>(thread.LinkedMemoryNotes.Count);
                foreach (var note in thread.LinkedMemoryNotes)
                {
                    var trimmed = (note ?? string.Empty).Trim();
                    if (trimmed.Equals(safeOld, StringComparison.OrdinalIgnoreCase))
                    {
                        renamed.Add(safeNew);
                        changed = true;
                    }
                    else if (trimmed.Length > 0)
                    {
                        renamed.Add(trimmed);
                    }
                }

                if (!changed)
                {
                    continue;
                }

                thread.LinkedMemoryNotes = renamed
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                thread.UpdatedUtc = DateTimeOffset.UtcNow;
                updatedCount += 1;
            }

            if (updatedCount > 0)
            {
                SaveLocked();
            }

            return updatedCount;
        }
    }

    public ConversationThreadView UpdateTitle(string conversationId, string title)
    {
        var safeTitle = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(safeTitle))
        {
            return Get(conversationId) ?? throw new InvalidOperationException("conversation not found");
        }

        lock (_lock)
        {
            var thread = RequireThreadLocked(conversationId);
            thread.Title = safeTitle;
            thread.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveLocked();
            return ToView(thread);
        }
    }

    public ConversationThreadView UpdateMetadata(
        string conversationId,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    )
    {
        return UpdateMetadata(conversationId, null, project, category, tags);
    }

    public ConversationThreadView UpdateMetadata(
        string conversationId,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    )
    {
        lock (_lock)
        {
            var thread = RequireThreadLocked(conversationId);
            if (title != null)
            {
                thread.Title = NormalizeTitleLabel(title, thread.Title);
            }

            if (project != null)
            {
                thread.Project = NormalizeLabel(project, "기본");
            }

            if (category != null)
            {
                thread.Category = NormalizeLabel(category, "일반");
            }

            if (tags != null)
            {
                thread.Tags = NormalizeTags(tags).ToList();
            }

            thread.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveLocked();
            return ToView(thread);
        }
    }

    public int GetTotalCharacters(string conversationId)
    {
        lock (_lock)
        {
            var thread = RequireThreadLocked(conversationId);
            return thread.Messages.Sum(x => x.Text?.Length ?? 0);
        }
    }

    public ConversationThreadView CompactWithSummary(string conversationId, int keepRecentMessages, string summaryNotice)
    {
        lock (_lock)
        {
            var thread = RequireThreadLocked(conversationId);
            if (thread.Messages.Count <= keepRecentMessages)
            {
                return ToView(thread);
            }

            var keep = Math.Max(2, keepRecentMessages);
            var latest = thread.Messages
                .OrderByDescending(x => x.CreatedUtc)
                .Take(keep)
                .OrderBy(x => x.CreatedUtc)
                .ToList();

            thread.Messages = new List<ConversationMessage>
            {
                new()
                {
                    Role = "system",
                    Meta = "auto-compress",
                    Text = summaryNotice,
                    CreatedUtc = DateTimeOffset.UtcNow
                }
            };
            thread.Messages.AddRange(latest);
            thread.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveLocked();
            return ToView(thread);
        }
    }

    public string BuildHistoryText(string conversationId, int maxMessages)
    {
        lock (_lock)
        {
            var thread = RequireThreadLocked(conversationId);
            if (thread.Messages.Count == 0)
            {
                return string.Empty;
            }

            var slice = thread.Messages
                .OrderByDescending(x => x.CreatedUtc)
                .Take(Math.Max(1, maxMessages))
                .OrderBy(x => x.CreatedUtc)
                .ToArray();

            var lines = new List<string>(slice.Length * 2);
            foreach (var item in slice)
            {
                var role = NormalizeRole(item.Role);
                lines.Add($"[{role}] {item.Text}");
            }

            return string.Join('\n', lines);
        }
    }

    public string BuildCompressionSourceText(string conversationId, int keepRecentMessages)
    {
        lock (_lock)
        {
            var thread = RequireThreadLocked(conversationId);
            if (thread.Messages.Count <= keepRecentMessages)
            {
                return string.Empty;
            }

            var cut = Math.Max(0, thread.Messages.Count - Math.Max(2, keepRecentMessages));
            var lines = thread.Messages
                .Take(cut)
                .Select(x => $"[{NormalizeRole(x.Role)}] {x.Text}")
                .ToArray();
            return string.Join('\n', lines);
        }
    }

    private static ConversationThreadSummary ToSummary(ConversationThread thread)
    {
        var last = thread.Messages.LastOrDefault();
        var preview = last == null
            ? string.Empty
            : BuildPreview(last.Text);

        return new ConversationThreadSummary(
            thread.Id,
            thread.Scope,
            thread.Mode,
            thread.Title,
            thread.Project,
            thread.Category,
            thread.Tags.ToArray(),
            thread.CreatedUtc,
            thread.UpdatedUtc,
            thread.Messages.Count,
            thread.LinkedMemoryNotes.ToArray(),
            preview
        );
    }

    private static ConversationThreadView ToView(ConversationThread thread)
    {
        return new ConversationThreadView(
            thread.Id,
            thread.Scope,
            thread.Mode,
            thread.Title,
            thread.Project,
            thread.Category,
            thread.Tags.ToArray(),
            thread.CreatedUtc,
            thread.UpdatedUtc,
            thread.Messages.Select(x =>
            {
                var usage = x.TokenUsage;
                if (usage == null && string.Equals(x.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    usage = TokenUsageEstimator.Estimate(string.Empty, x.Text, TokenUsageEstimator.SourceLegacyEstimated);
                }

                return new ConversationMessageView(x.Role, x.Text, x.Meta, x.CreatedUtc, usage);
            }).ToArray(),
            thread.LinkedMemoryNotes.ToArray(),
            thread.LatestCodingResult,
            TokenUsageEstimator.Combine(thread.Messages
                .Where(x => string.Equals(x.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.TokenUsage ?? TokenUsageEstimator.Estimate(string.Empty, x.Text, TokenUsageEstimator.SourceLegacyEstimated)))
        );
    }

    private static string BuildPreview(string? text)
    {
        var value = (text ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (value.Length <= 96)
        {
            return value;
        }

        return value[..96] + "...";
    }

    private ConversationThread RequireThreadLocked(string conversationId)
    {
        var thread = _state.Conversations.FirstOrDefault(x => x.Id.Equals(conversationId, StringComparison.Ordinal));
        if (thread == null)
        {
            throw new InvalidOperationException("conversation not found");
        }

        return thread;
    }

    private static string NormalizeToken(string raw, string fallback)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return UnsafeTokenRegex.Replace(value, "_");
    }

    private static string NormalizeRole(string role)
    {
        var value = (role ?? string.Empty).Trim().ToLowerInvariant();
        return value is "user" or "assistant" or "system" ? value : "assistant";
    }

    private static string ResolveTitle(string? title, string scope, string mode)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        return $"{scope}-{mode}-{DateTimeOffset.UtcNow:MMdd-HHmm}";
    }

    private static string NormalizeLabel(string? raw, string fallback)
    {
        var value = (raw ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Length <= 48 ? value : value[..48].TrimEnd();
    }

    private static string NormalizeTitleLabel(string? raw, string fallback)
    {
        var value = (raw ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Length <= 96 ? value : value[..96].TrimEnd();
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return Array.Empty<string>();
        }

        return tags
            .Select(item => (item ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Length <= 24 ? item : item[..24].TrimEnd())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return;
            }

            var json = File.ReadAllText(_statePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var parsed = JsonSerializer.Deserialize(json, OmniJsonContext.Default.ConversationState);
            if (parsed != null)
            {
                _state = parsed;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[conversation] load failed: {ex.Message}");
        }
    }

    private void SaveLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(_statePath);
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_state, OmniJsonContext.Default.ConversationState);
            AtomicFileStore.WriteAllText(_statePath, json, ownerOnly: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[conversation] save failed: {ex.Message}");
        }
    }
}

public sealed class ConversationState
{
    public List<ConversationThread> Conversations { get; set; } = new();
}

public sealed class ConversationThread
{
    public string Id { get; set; } = string.Empty;
    public string Scope { get; set; } = "chat";
    public string Mode { get; set; } = "single";
    public string Title { get; set; } = string.Empty;
    public string Project { get; set; } = "기본";
    public string Category { get; set; } = "일반";
    public List<string> Tags { get; set; } = new();
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<ConversationMessage> Messages { get; set; } = new();
    public List<string> LinkedMemoryNotes { get; set; } = new();
    public ConversationCodingResultSnapshot? LatestCodingResult { get; set; }
    // 사용자가 마지막으로 활성화한 스킬 이름 (재시작 후에도 유지). 비활성/해제 시 null.
    public string? ActiveSkillName { get; set; }
}

public sealed class ConversationMessage
{
    public string Role { get; set; } = "assistant";
    public string Text { get; set; } = string.Empty;
    public string Meta { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public TokenUsage? TokenUsage { get; set; }
}

public sealed record ConversationThreadSummary(
    string Id,
    string Scope,
    string Mode,
    string Title,
    string Project,
    string Category,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    int MessageCount,
    IReadOnlyList<string> LinkedMemoryNotes,
    string Preview
);

public sealed record ConversationMessageView(
    string Role,
    string Text,
    string Meta,
    DateTimeOffset CreatedUtc,
    TokenUsage? TokenUsage = null
);

public sealed record ConversationThreadView(
    string Id,
    string Scope,
    string Mode,
    string Title,
    string Project,
    string Category,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<ConversationMessageView> Messages,
    IReadOnlyList<string> LinkedMemoryNotes,
    ConversationCodingResultSnapshot? LatestCodingResult,
    TokenUsage? TokenUsageTotal = null
);
