using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private Task<string?> TryHandleTelegramSkillCommandAsync(string text, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (!normalized.StartsWith("/skill", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("/skills", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(null);
        }

        var firstLine = normalized.Split('\n', 2)[0];
        var tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Task.FromResult<string?>(BuildTelegramHelpText("skill"));
        }

        if (tokens.Length == 1
            || tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase)
            || tokens[1].Equals("도움말", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(BuildTelegramHelpText("skill"));
        }

        var action = tokens[1].Trim().ToLowerInvariant();
        if (action is "list" or "ls" or "목록")
        {
            return Task.FromResult<string?>(BuildLocalSkillInventoryResponse());
        }

        if (action is "off" or "stop" or "disable" or "deactivate" or "해제" or "끄기" or "중지" or "종료" or "그만")
        {
            return Task.FromResult<string?>(DeactivateTelegramSkill());
        }

        if (action is "use" or "activate" or "load" or "불러오기" or "사용" or "활성화")
        {
            if (tokens.Length < 3)
            {
                return Task.FromResult<string?>("사용법: /skill use <name> [project|global]");
            }

            var (name, scope) = ParseTelegramSkillNameAndScope(tokens, 2);
            return Task.FromResult<string?>(ActivateTelegramSkill(name, scope, returnAck: true));
        }

        if (action is "get" or "show" or "read" or "불러와" or "보기" or "읽기")
        {
            if (tokens.Length < 3)
            {
                return Task.FromResult<string?>("사용법: /skill get <name> [project|global]");
            }

            var (name, scope) = ParseTelegramSkillNameAndScope(tokens, 2);
            return Task.FromResult<string?>(BuildTelegramSkillGetResponse(name, scope));
        }

        if (action is "create" or "save" or "new" or "생성" or "저장" or "추가")
        {
            if (tokens.Length < 3)
            {
                return Task.FromResult<string?>(BuildTelegramSkillCreateUsage());
            }

            var (name, scope) = ParseTelegramSkillNameAndScope(tokens, 2);
            var bodySpec = ExtractTelegramSkillBodySpec(normalized);
            if (bodySpec == null)
            {
                return Task.FromResult<string?>(BuildTelegramSkillCreateUsage());
            }

            var save = SkillFiles.Save(name, scope, bodySpec.Value.Description, bodySpec.Value.Body);
            if (!save.Ok)
            {
                return Task.FromResult<string?>($"스킬 저장 실패: {save.Error}");
            }

            _auditLogger.Log("telegram", "skill_save", "ok", $"name={save.Name} scope={save.Scope}");
            return Task.FromResult<string?>($"""
                   스킬을 저장했습니다.
                   - 이름: {save.Name}
                   - 범위: {save.Scope}
                   - 경로: {RelativizeSkillPathForTelegram(save.Path)}
                   """);
        }

        return Task.FromResult<string?>(BuildTelegramHelpText("skill"));
    }

    private Task<string?> TryHandleTelegramNaturalSkillCommandAsync(string text, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        // 다중 스킬 언급은 활성화/인벤토리 검사보다 먼저 거부.
        var multiSkillRejection = TryBuildMultiSkillRejectionResponse(text ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(multiSkillRejection))
        {
            _auditLogger.Log("telegram", "skill_multi_mention", "blocked", "");
            return Task.FromResult<string?>(multiSkillRejection);
        }

        var normalized = Regex.Replace((text ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0 || normalized.Length > 220)
        {
            return Task.FromResult<string?>(null);
        }

        var compact = Regex.Replace(normalized, @"[\p{P}\p{S}\s]+", string.Empty);

        // 비활성화/활성화 의도를 인벤토리 검사보다 먼저 처리해
        // "<스킬명> 스킬 사용해서 ... 해줘" 같은 명시적 호출이 인벤토리로 잘못 빠지지 않게 한다.
        if (LooksLikeSkillDeactivationRequest(normalized))
        {
            return Task.FromResult<string?>(DeactivateTelegramSkill());
        }

        var matched = FindMentionedSkill(normalized);
        var activationRequested = LooksLikeTelegramSkillActivationRequest(normalized, compact, matched != null);
        if (activationRequested)
        {
            if (LooksLikeInlineSkillTask(normalized))
            {
                if (matched == null)
                {
                    return Task.FromResult<string?>(null);
                }

                _ = ActivateTelegramSkill(matched.Name, matched.Scope, returnAck: false);
                return Task.FromResult<string?>(null);
            }

            if (matched != null)
            {
                return Task.FromResult<string?>(ActivateTelegramSkill(matched.Name, matched.Scope, returnAck: true));
            }
        }

        if (LooksLikeLocalSkillInventoryQuestion(normalized, compact))
        {
            return Task.FromResult<string?>(BuildLocalSkillInventoryResponse());
        }

        return Task.FromResult<string?>(null);
    }

    private string ActivateTelegramSkill(string name, string? scope, bool returnAck)
    {
        var skill = FindSkill(name, scope);
        if (skill == null)
        {
            return $"스킬을 찾지 못했습니다: {name}";
        }

        var session = SessionContext.Create(EnsureTelegramLinkedConversation(), "telegram");
        if (!string.IsNullOrWhiteSpace(session.SessionId))
        {
            _activeSkillByThread[session.SessionId] = skill.Name;
            PersistActiveSkillForThread(session.SessionId, skill.Name);
        }

        _auditLogger.Log("telegram", "skill_activate", "ok", $"name={skill.Name} scope={skill.Scope}");
        if (!returnAck)
        {
            return string.Empty;
        }

        return $"""
               `{skill.Name}` 스킬을 텔레그램 대화에 적용했습니다.
               다음 메시지부터 이 스킬 지침을 우선 적용합니다.

               - 범위: {skill.Scope}
               - 설명: {TrimLocalAssistantInfoText(skill.Description, 120)}
               - 해제: /skill off
               """;
    }

    private string DeactivateTelegramSkill()
    {
        var session = SessionContext.Create(EnsureTelegramLinkedConversation(), "telegram");
        if (!string.IsNullOrWhiteSpace(session.SessionId)
            && _activeSkillByThread.TryRemove(session.SessionId, out var skillName))
        {
            PersistActiveSkillForThread(session.SessionId, null);
            _auditLogger.Log("telegram", "skill_deactivate", "ok", $"name={skillName}");
            return $"`{skillName}` 스킬을 해제했습니다.";
        }

        return "현재 텔레그램 대화에 활성화된 스킬이 없습니다.";
    }

    private string BuildTelegramSkillGetResponse(string name, string? scope)
    {
        var result = SkillFiles.Get(name, scope);
        if (!result.Ok)
        {
            return $"스킬 불러오기 실패: {result.Error}";
        }

        var body = TrimToUtf8ByteCount((result.Body ?? string.Empty).Trim(), 2300);
        if (string.IsNullOrWhiteSpace(body))
        {
            body = "(본문 없음)";
        }

        return $"""
               [스킬]
               - 이름: {result.Name}
               - 범위: {result.Scope}
               - 설명: {TrimLocalAssistantInfoText(result.Description, 180)}
               - 경로: {RelativizeSkillPathForTelegram(result.Path)}

               [SKILL.md]
               {body}
               """;
    }

    private SkillManifest? FindMentionedSkill(string normalizedText)
    {
        // 공통 단어 경계 검사 helper 사용. 다중 스킬은 상위에서 거부되므로 첫 매칭만 사용.
        return DetectMentionedSkillsInPrompt(normalizedText).FirstOrDefault();
    }

    private SkillManifest? FindSkill(string name, string? scope)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        var normalizedScope = (scope ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        try
        {
            return _projectContextLoader.LoadSnapshot()
                .Skills
                .Where(skill => skill.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
                .Where(skill => string.IsNullOrWhiteSpace(normalizedScope)
                                || skill.Scope.Equals(normalizedScope, StringComparison.OrdinalIgnoreCase))
                .OrderBy(skill => skill.Scope.Equals("project", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            _auditLogger.Log("telegram", "skill_find", "failed", ex.Message);
            return null;
        }
    }

    private static bool LooksLikeTelegramSkillActivationRequest(string normalized, string compact, bool hasSkillMention)
    {
        if (!hasSkillMention
            && !ContainsAny(normalized, "스킬", "skill", "skills", "skill.md")
            && !compact.Contains("스킬", StringComparison.Ordinal)
            && !compact.Contains("skill", StringComparison.Ordinal))
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "사용해",
            "사용해줘",
            "써",
            "써줘",
            "적용해",
            "켜",
            "켜줘",
            "활성화",
            "불러",
            "불러와",
            "로드",
            "activate",
            "use",
            "load")
            || compact.Contains("사용해", StringComparison.Ordinal)
            || compact.Contains("사용해줘", StringComparison.Ordinal)
            || compact.Contains("활성화", StringComparison.Ordinal)
            || compact.Contains("불러와", StringComparison.Ordinal)
            || compact.Contains("activate", StringComparison.Ordinal)
            || compact.Contains("use", StringComparison.Ordinal)
            || compact.Contains("load", StringComparison.Ordinal);
    }

    private static bool LooksLikeInlineSkillTask(string normalized)
    {
        return Regex.IsMatch(
                   normalized,
                   @"(?i)(사용해서|써서|적용해서|활용해서|이용해서|가지고|using\s+|use\s+\S+\s+to\s+)"
               )
               || ContainsAny(
                   normalized,
                   "설명",
                   "알려",
                   "말해",
                   "정리",
                   "분석",
                   "비교",
                   "원리",
                   "방법",
                   "이유",
                   "어떻게",
                   "왜",
                   "도와",
                   "해줘",
                   "해 줘",
                   "explain",
                   "describe",
                   "tell",
                   "summarize",
                   "compare",
                   "analyze");
    }

    private static (string Name, string? Scope) ParseTelegramSkillNameAndScope(string[] tokens, int startIndex)
    {
        var name = startIndex < tokens.Length ? tokens[startIndex].Trim() : string.Empty;
        string? scope = null;
        if (startIndex + 1 < tokens.Length)
        {
            var candidate = tokens[startIndex + 1].Trim().ToLowerInvariant();
            if (candidate is "project" or "global")
            {
                scope = candidate;
            }
        }

        return (name, scope);
    }

    private static (string Description, string Body)? ExtractTelegramSkillBodySpec(string command)
    {
        var parts = (command ?? string.Empty).Replace("\r\n", "\n").Split('\n', 2);
        if (parts.Length < 2)
        {
            return null;
        }

        var payload = parts[1].Trim();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var split = Regex.Split(payload, @"(?m)^\s*---\s*$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        if (split.Length >= 2)
        {
            var description = split[0].Trim();
            var body = string.Join("\n---\n", split.Skip(1)).Trim();
            if (!string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(body))
            {
                return (description, body);
            }
        }

        var lines = payload.Split('\n');
        if (lines.Length >= 2)
        {
            var description = lines[0].Trim();
            var body = string.Join('\n', lines.Skip(1)).Trim();
            if (!string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(body))
            {
                return (description, body);
            }
        }

        return null;
    }

    private static string BuildTelegramSkillCreateUsage()
    {
        return """
               사용법:
               /skill create <name> [project|global]
               한 줄 설명
               ---
               스킬 본문

               예:
               /skill create casual-empathy project
               일상 대화에서 감정을 먼저 인정하고 짧게 공감한다.
               ---
               - 답변 시작은 사용자의 감정/상황을 한 문장으로 인정한다.
               - 해결책은 사용자가 원할 때만 짧게 제안한다.
               """;
    }

    private string RelativizeSkillPathForTelegram(string path)
    {
        var normalized = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "-";
        }

        var projectRoot = Path.GetFullPath(_config.WorkspaceRootDir);
        try
        {
            if (normalized.StartsWith(projectRoot, StringComparison.Ordinal))
            {
                return Path.GetRelativePath(projectRoot, normalized).Replace('\\', '/');
            }
        }
        catch
        {
        }

        return normalized;
    }
}
