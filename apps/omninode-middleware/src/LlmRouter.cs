using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public enum RouterIntent
{
    OsControl,
    QuerySystem,
    DynamicCode,
    Unknown
}

public sealed record GeminiGroundedChatResponse(
    string Text,
    long FirstChunkMs,
    long FullResponseMs
);

public sealed record GeminiUrlContextChatResponse(
    string Text,
    long FirstChunkMs,
    long FullResponseMs,
    IReadOnlyList<SearchCitationReference> Citations
);

public sealed record OpenAiCompatibleStreamChunk(string Content, string FinishReason);

public sealed class LlmRouter : IDisposable
{
    private const int ChatContinuationRounds = 6;
    private const string CerebrasFallbackModel = "gpt-oss-120b";
    private string? _cerebrasResolvedModelCache = null;
    private DateTime _cerebrasResolvedModelCachedAt = DateTime.MinValue;
    private static readonly Regex GroqMaxTokensLimitRegex = new(
        @"max_tokens`?\s*must be less than or equal to `?(?<limit>\d+)`?|maximum value for `max_tokens` is(?: less than the `context_window` for this model)?\s*`?(?<limit2>\d+)`?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private readonly AppConfig _config;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly HttpClient _httpClient;
    private readonly object _groqLock = new();
    private readonly object _geminiLock = new();
    private readonly Dictionary<string, GroqUsage> _groqUsageByModel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GroqRateLimit> _groqRateByModel = new(StringComparer.OrdinalIgnoreCase);
    private GeminiUsage _geminiUsage = new();
    private readonly string _usageStatePath;
    private string _selectedGroqModel;

    public LlmRouter(AppConfig config, RuntimeSettings runtimeSettings)
    {
        _config = config;
        _runtimeSettings = runtimeSettings;
        _selectedGroqModel = _config.GroqModel;
        _usageStatePath = _config.LlmUsageStatePath;
        _httpClient = new HttpClient
        {
            Timeout = ResolveSharedHttpTimeout(_config)
        };

        LoadUsageState();
    }

    public string GetSelectedGroqModel()
    {
        lock (_groqLock)
        {
            return _selectedGroqModel;
        }
    }

    public bool TrySetSelectedGroqModel(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var trimmed = modelId.Trim();
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '/' || ch == '.')
            {
                continue;
            }

            return false;
        }

        lock (_groqLock)
        {
            _selectedGroqModel = trimmed;
            if (!_groqUsageByModel.ContainsKey(trimmed))
            {
                _groqUsageByModel[trimmed] = new GroqUsage();
            }
        }

        return true;
    }

    public IReadOnlyDictionary<string, GroqUsage> GetGroqUsageSnapshot()
    {
        lock (_groqLock)
        {
            return _groqUsageByModel.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Clone(),
                StringComparer.OrdinalIgnoreCase
            );
        }
    }

    public IReadOnlyDictionary<string, GroqRateLimit> GetGroqRateLimitSnapshot()
    {
        lock (_groqLock)
        {
            return _groqRateByModel.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Clone(),
                StringComparer.OrdinalIgnoreCase
            );
        }
    }

    public GeminiUsage GetGeminiUsageSnapshot()
    {
        lock (_geminiLock)
        {
            return _geminiUsage.Clone();
        }
    }

    public bool HasGroqApiKey()
    {
        return !string.IsNullOrWhiteSpace(_runtimeSettings.GetGroqApiKey());
    }

    public bool HasGeminiApiKey()
    {
        return !string.IsNullOrWhiteSpace(_runtimeSettings.GetGeminiApiKey());
    }

    public bool HasCerebrasApiKey()
    {
        return !string.IsNullOrWhiteSpace(_runtimeSettings.GetCerebrasApiKey());
    }

    public bool HasNvidiaApiKey()
    {
        return !string.IsNullOrWhiteSpace(_runtimeSettings.GetNvidiaApiKey());
    }

    public bool HasSttSettings()
    {
        return !string.IsNullOrWhiteSpace(_config.SttBaseUrl)
               && !string.IsNullOrWhiteSpace(_config.SttModel)
               && !string.IsNullOrWhiteSpace(_runtimeSettings.GetSttApiKey());
    }

    public async Task<string> TranscribeAudioAsync(InputAttachment attachment, CancellationToken cancellationToken)
    {
        if (!HasSttSettings())
        {
            return "음성 변환 설정 필요: OMNINODE_STT_BASE_URL, OMNINODE_STT_MODEL, OMNINODE_STT_API_KEY를 설정하세요.";
        }

        if (attachment == null || string.IsNullOrWhiteSpace(attachment.DataBase64))
        {
            return "음성 파일이 비어 있습니다.";
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(attachment.DataBase64);
        }
        catch
        {
            return "음성 파일을 읽을 수 없습니다.";
        }

        if (bytes.Length == 0)
        {
            return "음성 파일이 비어 있습니다.";
        }

        var baseUrl = _config.SttBaseUrl.Trim();
        var endpoint = baseUrl.EndsWith("/audio/transcriptions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl.TrimEnd('/')}/audio/transcriptions";
        var apiKey = _runtimeSettings.GetSttApiKey() ?? string.Empty;
        var fileName = string.IsNullOrWhiteSpace(attachment.Name) ? "telegram-audio.ogg" : attachment.Name.Trim();
        var mimeType = string.IsNullOrWhiteSpace(attachment.MimeType) ? "audio/ogg" : attachment.MimeType.Trim();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(_config.SttModel.Trim(), Encoding.UTF8), "model");
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            form.Add(fileContent, "file", fileName);
            request.Content = form;

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[stt] failed ({(int)response.StatusCode}): {responseBody}");
                return $"음성 변환 실패: {(int)response.StatusCode}";
            }

            var text = ExtractSttText(responseBody).Trim();
            return string.IsNullOrWhiteSpace(text) ? "음성 변환 결과가 비어 있습니다." : text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "음성 변환 시간이 초과되었습니다.";
        }
        catch (Exception ex)
        {
            return $"음성 변환 오류: {ex.Message}";
        }
    }

    public async Task<RouterIntent> ClassifyIntentAsync(string input, CancellationToken cancellationToken)
    {
        var fallback = ClassifyIntentFallback(input);
        var groqApiKey = _runtimeSettings.GetGroqApiKey();
        if (string.IsNullOrWhiteSpace(groqApiKey))
        {
            return fallback;
        }

        var model = GetSelectedGroqModel();
        var endpoint = $"{_config.GroqBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "Classify intent into exactly one token: OS_CONTROL, QUERY_SYSTEM, DYNAMIC_CODE, UNKNOWN.";
        var userPrompt = $"Input: {input}";

        var body = "{"
            + $"\"model\":\"{EscapeJson(model)}\","
            + "\"temperature\":0,"
            + "\"max_tokens\":12,"
            + "\"messages\":["
            + $"{{\"role\":\"system\",\"content\":\"{EscapeJson(systemPrompt)}\"}},"
            + $"{{\"role\":\"user\",\"content\":\"{EscapeJson(userPrompt)}\"}}"
            + "]"
            + "}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", groqApiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            CaptureGroqRateLimitHeaders(model, response.Headers);

            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[groq] classify failed ({(int)response.StatusCode}): {responseBody}");
                return fallback;
            }

            CaptureGroqUsage(model, responseBody);
            var content = ExtractGroqContent(responseBody);
            var mapped = MapIntent(content);
            return mapped == RouterIntent.Unknown ? fallback : mapped;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[groq] classify error: {ex.Message}");
            return fallback;
        }
    }

    public async Task<string> BuildExecutionPlanAsync(string userInput, string systemContext, CancellationToken cancellationToken)
    {
        var fallback = BuildFallbackPlan(userInput, systemContext);
        var geminiApiKey = _runtimeSettings.GetGeminiApiKey();
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return fallback;
        }

        var endpoint = $"{_config.GeminiBaseUrl.TrimEnd('/')}/models/{_config.GeminiModel}:generateContent";

        var prompt = "Generate a concise step-by-step execution plan for a local automation agent."
            + " Output only executable pseudo-steps without markdown fences.\n\n"
            + $"UserInput:\n{userInput}\n\n"
            + $"SystemContext:\n{systemContext}\n";

        var body = "{"
            + "\"contents\":[{"
            + "\"role\":\"user\","
            + "\"parts\":["
            + $"{{\"text\":\"{EscapeJson(prompt)}\"}}"
            + "]"
            + "}],"
            + "\"generationConfig\":{\"temperature\":0.2,\"maxOutputTokens\":512}"
            + "}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-goog-api-key", geminiApiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[gemini] plan failed ({(int)response.StatusCode}): {responseBody}");
                return fallback;
            }

            CaptureGeminiUsage(responseBody);
            var plan = ExtractGeminiText(responseBody);
            if (string.IsNullOrWhiteSpace(plan))
            {
                var blockReason = ExtractGeminiBlockReason(responseBody);
                if (!string.IsNullOrWhiteSpace(blockReason))
                {
                    Console.Error.WriteLine($"[gemini] blocked: {blockReason}");
                }
                return fallback;
            }

            return plan.Trim();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gemini] plan error: {ex.Message}");
            return fallback;
        }
    }

    public string ResolvePlanningRoute(string role, IReadOnlyList<string>? providerChain = null)
    {
        var normalizedRole = (role ?? string.Empty).Trim().ToLowerInvariant();
        var routeRole = normalizedRole == "reviewer" ? "reviewer" : "planner";

        foreach (var provider in NormalizePlanningProviderChain(providerChain))
        {
            if (provider == "gemini" && HasGeminiApiKey())
            {
                return $"{routeRole}:gemini:{_config.GeminiModel}";
            }

            if (provider == "groq" && HasGroqApiKey())
            {
                return $"{routeRole}:groq:{GetSelectedGroqModel()}";
            }

            if (provider == "nvidia" && HasNvidiaApiKey())
            {
                return $"{routeRole}:nvidia:{_config.NvidiaModel}";
            }

            if (provider == "cerebras" && HasCerebrasApiKey())
            {
                return $"{routeRole}:cerebras:{_config.CerebrasModel}";
            }
        }

        return $"{routeRole}:fallback";
    }

    public async Task<string> BuildWorkPlanDraftAsync(
        string objective,
        IReadOnlyList<string> constraints,
        string systemContext,
        string mode,
        IReadOnlyList<string>? providerChain,
        CancellationToken cancellationToken
    )
    {
        var prompt = BuildPlanningPrompt(objective, constraints, systemContext, mode);
        foreach (var provider in NormalizePlanningProviderChain(providerChain))
        {
            if (provider == "gemini" && HasGeminiApiKey())
            {
                return await GenerateGeminiChatAsync(prompt, _config.GeminiModel, 1024, cancellationToken);
            }

            if (provider == "groq" && HasGroqApiKey())
            {
                return await GenerateGroqChatAsync(prompt, GetSelectedGroqModel(), 1024, cancellationToken);
            }

            if (provider == "nvidia" && HasNvidiaApiKey())
            {
                return await GenerateNvidiaChatAsync(prompt, _config.NvidiaModel, 1024, cancellationToken);
            }

            if (provider == "cerebras" && HasCerebrasApiKey())
            {
                return await GenerateCerebrasChatAsync(prompt, _config.CerebrasModel, 1024, cancellationToken);
            }
        }

        return BuildFallbackPlan(objective, systemContext);
    }

    public async Task<string> ReviewWorkPlanAsync(
        WorkPlan plan,
        string systemContext,
        IReadOnlyList<string>? providerChain,
        CancellationToken cancellationToken
    )
    {
        var prompt = BuildPlanReviewPrompt(plan, systemContext);
        foreach (var provider in NormalizePlanningProviderChain(providerChain))
        {
            if (provider == "gemini" && HasGeminiApiKey())
            {
                return await GenerateGeminiChatAsync(prompt, _config.GeminiModel, 768, cancellationToken);
            }

            if (provider == "groq" && HasGroqApiKey())
            {
                return await GenerateGroqChatAsync(prompt, GetSelectedGroqModel(), 768, cancellationToken);
            }

            if (provider == "nvidia" && HasNvidiaApiKey())
            {
                return await GenerateNvidiaChatAsync(prompt, _config.NvidiaModel, 768, cancellationToken);
            }

            if (provider == "cerebras" && HasCerebrasApiKey())
            {
                return await GenerateCerebrasChatAsync(prompt, _config.CerebrasModel, 768, cancellationToken);
            }
        }

        return BuildFallbackPlanReview(plan);
    }

    private static IReadOnlyList<string> NormalizePlanningProviderChain(IReadOnlyList<string>? providerChain)
    {
        if (providerChain == null || providerChain.Count == 0)
        {
            return new[] { "gemini", "groq", "nvidia", "cerebras" };
        }

        return providerChain
            .Select(item =>
            {
                var normalized = (item ?? string.Empty).Trim().ToLowerInvariant();
                return normalized is "nvidia-nim" or "nvidia_nim" or "nim"
                    ? "nvidia"
                    : normalized;
            })
            .Where(item =>
                item == "gemini"
                || item == "groq"
                || item == "nvidia"
                || item == "cerebras")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<string> GenerateGroqChatAsync(
        string userInput,
        string? modelOverride,
        CancellationToken cancellationToken
    )
    {
        return GenerateGroqChatAsync(userInput, modelOverride, _config.ChatMaxOutputTokens, cancellationToken);
    }

    public async Task<string> GenerateGroqChatAsync(
        string userInput,
        string? modelOverride,
        int maxOutputTokens,
        CancellationToken cancellationToken
    )
    {
        var groqApiKey = _runtimeSettings.GetGroqApiKey();
        if (string.IsNullOrWhiteSpace(groqApiKey))
        {
            return "Groq API 키가 설정되지 않았습니다.";
        }

        var model = string.IsNullOrWhiteSpace(modelOverride) ? GetSelectedGroqModel() : modelOverride.Trim();
        var endpoint = $"{_config.GroqBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Respond in Korean with concise and practical answers. "
            + "Answer only the latest user request. Do not switch to news, search summaries, 3D printing, or other unrelated topics unless the user explicitly asks for them. "
            + "If conversation history is provided above, treat the user's short follow-up messages (e.g., '그니까 잘 돌아가?', '이 환경에서?', '24GB 구성임') as continuations of the prior turns. Never reply '이전 대화 맥락이 제공되지 않아' when [최근 대화] is in the prompt — use it. "
            + "When the user asks for your judgment or opinion (어때, 어떻게 생각해, 잘 돌아갈까, 검토해봐, 추천해, 너 생각, 네 의견 등), give a concrete, decisive answer based on the conversation history and your knowledge. Do not deflect with generic disclaimers like '정확도/정밀도/재현율/F1-score 같은 객관적 지표가 필요하다' or '구체적인 평가 지표와 데이터셋이 필요하다'. State your best assessment with a 1-2 sentence rationale, then optionally note any uncertainty. "
            + "Do not repeat the same paragraphs across multiple turns. If the user asks the same thing again, dig deeper or take a clearer stance instead of repeating prior wording.";
        var requestedMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _config.ChatMaxOutputTokens);
        var effectiveMaxOutputTokens = ClampGroqMaxOutputTokensForModel(model, requestedMaxOutputTokens);
        var promptBudgetChars = ResolveGroqPromptBudgetChars(model);
        var promptForTurn = TruncatePromptForGroq(userInput, promptBudgetChars);
        var mergedBuilder = new StringBuilder();
        var rateLimitRetries = 0;
        var requestTooLargeRetries = 0;
        var tokenLimitRetries = 0;

        try
        {
            for (var turn = 0; turn < ChatContinuationRounds; turn++)
            {
                var promptForRequest = TruncatePromptForGroq(promptForTurn, promptBudgetChars);
                var multiTurn = SplitPromptToMultiTurn(promptForRequest);
                Console.WriteLine($"[DEBUG:groq] multiTurnCount={multiTurn.Count} promptLen={promptForRequest.Length} firstRole={multiTurn[0].Role} historyInPrompt={promptForRequest.Contains("[최근 대화]")}");
                string messagesJson;
                if (multiTurn.Count > 1 && multiTurn[0].Role != "user")
                {
                    // multi-turn 구조: system_prompt + 규칙/메모리는 user로, history는 교대 배치
                    var mb = new StringBuilder();
                    mb.Append($"{{\"role\":\"system\",\"content\":\"{EscapeJson(systemPrompt)}\"}}");
                    foreach (var (role, msgContent) in multiTurn)
                    {
                        var apiRole = role == "assistant" ? "assistant" : "user";
                        mb.Append($",{{\"role\":\"{apiRole}\",\"content\":\"{EscapeJson(msgContent)}\"}}");
                    }
                    messagesJson = mb.ToString();
                }
                else
                {
                    // fallback: 기존 단일 user 메시지
                    messagesJson = $"{{\"role\":\"system\",\"content\":\"{EscapeJson(systemPrompt)}\"}},"
                        + $"{{\"role\":\"user\",\"content\":\"{EscapeJson(promptForRequest)}\"}}";
                }

                var body = "{"
                    + $"\"model\":\"{EscapeJson(model)}\","
                    + "\"temperature\":0.3,"
                    + $"\"max_tokens\":{effectiveMaxOutputTokens},"
                    + "\"messages\":["
                    + messagesJson
                    + "]"
                    + "}";

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", groqApiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                CaptureGroqRateLimitHeaders(model, response.Headers);
                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode == 429 && rateLimitRetries < 2)
                    {
                        rateLimitRetries += 1;
                        await Task.Delay(GetGroqRetryDelayMs(response.Headers, rateLimitRetries == 1 ? 900 : 1800), cancellationToken);
                        turn -= 1;
                        continue;
                    }

                    if (TryExtractGroqMaxTokensLimit(responseBody, out var limit)
                        && limit >= 128
                        && effectiveMaxOutputTokens > limit
                        && tokenLimitRetries < 2)
                    {
                        tokenLimitRetries += 1;
                        effectiveMaxOutputTokens = ClampGroqMaxOutputTokensForModel(model, limit);
                        turn -= 1;
                        continue;
                    }

                    if (IsGroqRequestTooLarge(response.StatusCode, responseBody) && requestTooLargeRetries < 3)
                    {
                        requestTooLargeRetries += 1;
                        promptBudgetChars = Math.Max(1200, promptBudgetChars / 2);
                        promptForTurn = TruncatePromptForGroq(promptForTurn, promptBudgetChars);
                        effectiveMaxOutputTokens = Math.Max(256, effectiveMaxOutputTokens / 2);
                        turn -= 1;
                        continue;
                    }

                    Console.Error.WriteLine($"[groq] chat failed ({(int)response.StatusCode}): {responseBody}");
                    return $"Groq 요청 실패: {(int)response.StatusCode}";
                }

                rateLimitRetries = 0;
                requestTooLargeRetries = 0;
                CaptureGroqUsage(model, responseBody);
                var chunk = ExtractGroqChatChunk(responseBody);
                var chunkText = chunk.Content.Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    if (mergedBuilder.Length > 0)
                    {
                        mergedBuilder.AppendLine();
                    }
                    mergedBuilder.Append(chunkText);
                }

                if (!IsOpenAiCompatibleTruncated(chunk.FinishReason) || string.IsNullOrWhiteSpace(chunkText))
                {
                    break;
                }

                promptForTurn = TruncatePromptForGroq(BuildContinuationPrompt(userInput, mergedBuilder.ToString()), promptBudgetChars);
            }

            var content = mergedBuilder.ToString().Trim();
            return string.IsNullOrWhiteSpace(content) ? "Groq 응답이 비어 있습니다." : content;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[groq] chat error: {ex.Message}");
            return $"Groq 호출 오류: {ex.Message}";
        }
    }

    public async Task<string> GenerateGroqChatStreamingAsync(
        string userInput,
        string? modelOverride,
        int maxOutputTokens,
        Action<string>? deltaCallback,
        CancellationToken cancellationToken
    )
    {
        var groqApiKey = _runtimeSettings.GetGroqApiKey();
        if (string.IsNullOrWhiteSpace(groqApiKey))
        {
            return "Groq API 키가 설정되지 않았습니다.";
        }

        var model = string.IsNullOrWhiteSpace(modelOverride) ? GetSelectedGroqModel() : modelOverride.Trim();
        var endpoint = $"{_config.GroqBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Respond in Korean with concise and practical answers. "
            + "Answer only the latest user request. Do not switch to news, search summaries, 3D printing, or other unrelated topics unless the user explicitly asks for them. "
            + "If conversation history is provided above, treat the user's short follow-up messages (e.g., '그니까 잘 돌아가?', '이 환경에서?', '24GB 구성임') as continuations of the prior turns. Never reply '이전 대화 맥락이 제공되지 않아' when [최근 대화] is in the prompt — use it. "
            + "When the user asks for your judgment or opinion (어때, 어떻게 생각해, 잘 돌아갈까, 검토해봐, 추천해, 너 생각, 네 의견 등), give a concrete, decisive answer based on the conversation history and your knowledge. Do not deflect with generic disclaimers like '정확도/정밀도/재현율/F1-score 같은 객관적 지표가 필요하다' or '구체적인 평가 지표와 데이터셋이 필요하다'. State your best assessment with a 1-2 sentence rationale, then optionally note any uncertainty. "
            + "Do not repeat the same paragraphs across multiple turns. If the user asks the same thing again, dig deeper or take a clearer stance instead of repeating prior wording.";
        var requestedMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _config.ChatMaxOutputTokens);
        var effectiveMaxOutputTokens = ClampGroqMaxOutputTokensForModel(model, requestedMaxOutputTokens);
        var promptForRequest = TruncatePromptForGroq(userInput, ResolveGroqPromptBudgetChars(model));

        return await GenerateOpenAiCompatibleChatStreamingAsync(
            "groq",
            endpoint,
            groqApiKey,
            model,
            systemPrompt,
            promptForRequest,
            "max_tokens",
            effectiveMaxOutputTokens,
            deltaCallback,
            cancellationToken
        );
    }

    public async Task<string> GenerateGroqMultimodalChatAsync(
        string userInput,
        string? modelOverride,
        IReadOnlyList<InputAttachment> attachments,
        int maxOutputTokens,
        CancellationToken cancellationToken
    )
    {
        var groqApiKey = _runtimeSettings.GetGroqApiKey();
        if (string.IsNullOrWhiteSpace(groqApiKey))
        {
            return "Groq API 키가 설정되지 않았습니다.";
        }

        var model = string.IsNullOrWhiteSpace(modelOverride) ? GetSelectedGroqModel() : modelOverride.Trim();
        var imageAttachments = (attachments ?? Array.Empty<InputAttachment>())
            .Where(IsImageAttachment)
            .Take(4)
            .ToArray();
        if (imageAttachments.Length == 0)
        {
            return await GenerateGroqChatAsync(userInput, model, maxOutputTokens, cancellationToken);
        }

        var endpoint = $"{_config.GroqBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Analyze attached images and answer in Korean concisely.";
        var effectiveMaxOutputTokens = ClampGroqMaxOutputTokensForModel(model, NormalizeMaxOutputTokens(maxOutputTokens, _config.ChatMaxOutputTokens));

        try
        {
            var bodyBuilder = new StringBuilder();
            bodyBuilder.Append('{');
            bodyBuilder.Append($"\"model\":\"{EscapeJson(model)}\",");
            bodyBuilder.Append("\"temperature\":0.2,");
            bodyBuilder.Append($"\"max_tokens\":{effectiveMaxOutputTokens},");
            bodyBuilder.Append("\"messages\":[");
            bodyBuilder.Append($"{{\"role\":\"system\",\"content\":\"{EscapeJson(systemPrompt)}\"}},");
            bodyBuilder.Append("{\"role\":\"user\",\"content\":[");
            bodyBuilder.Append($"{{\"type\":\"text\",\"text\":\"{EscapeJson(userInput)}\"}}");
            foreach (var attachment in imageAttachments)
            {
                var mimeType = string.IsNullOrWhiteSpace(attachment.MimeType) ? "image/jpeg" : attachment.MimeType.Trim();
                var dataUrl = $"data:{mimeType};base64,{attachment.DataBase64}";
                bodyBuilder.Append(",");
                bodyBuilder.Append("{\"type\":\"image_url\",\"image_url\":{");
                bodyBuilder.Append($"\"url\":\"{EscapeJson(dataUrl)}\"");
                bodyBuilder.Append("}}");
            }

            bodyBuilder.Append("]}");
            bodyBuilder.Append("]}");
            var body = bodyBuilder.ToString();

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", groqApiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            CaptureGroqRateLimitHeaders(model, response.Headers);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 429)
                {
                    await Task.Delay(GetGroqRetryDelayMs(response.Headers, 1200), cancellationToken);
                    using var retryRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", groqApiKey);
                    retryRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

                    using var retryResponse = await _httpClient.SendAsync(retryRequest, cancellationToken);
                    var retryBody = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
                    CaptureGroqRateLimitHeaders(model, retryResponse.Headers);
                    if (!retryResponse.IsSuccessStatusCode)
                    {
                        Console.Error.WriteLine($"[groq] multimodal chat failed ({(int)retryResponse.StatusCode}): {retryBody}");
                        return $"Groq 요청 실패: {(int)retryResponse.StatusCode}";
                    }

                    responseBody = retryBody;
                }
                else
                {
                    if (IsGroqRequestTooLarge(response.StatusCode, responseBody))
                    {
                        var fallbackPrompt = "[이미지 첨부는 크기 제한으로 제외됨]\n" + userInput;
                        return await GenerateGroqChatAsync(fallbackPrompt, model, Math.Min(effectiveMaxOutputTokens, 1024), cancellationToken);
                    }

                    Console.Error.WriteLine($"[groq] multimodal chat failed ({(int)response.StatusCode}): {responseBody}");
                    return $"Groq 요청 실패: {(int)response.StatusCode}";
                }
            }

            CaptureGroqUsage(model, responseBody);
            var content = ExtractGroqContent(responseBody).Trim();
            return string.IsNullOrWhiteSpace(content) ? "Groq 응답이 비어 있습니다." : content;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[groq] multimodal chat error: {ex.Message}");
            return $"Groq 호출 오류: {ex.Message}";
        }
    }

    public Task<string> GenerateGeminiChatAsync(string userInput, CancellationToken cancellationToken)
    {
        return GenerateGeminiChatAsync(userInput, _config.GeminiModel, _config.ChatMaxOutputTokens, cancellationToken);
    }

    public Task<string> GenerateGeminiChatAsync(
        string userInput,
        int maxOutputTokens,
        CancellationToken cancellationToken
    )
    {
        return GenerateGeminiChatAsync(userInput, _config.GeminiModel, maxOutputTokens, cancellationToken);
    }

    public async Task<string> GenerateGeminiChatAsync(
        string userInput,
        string? modelOverride,
        int maxOutputTokens,
        CancellationToken cancellationToken
    )
    {
        var geminiApiKey = _runtimeSettings.GetGeminiApiKey();
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return "Gemini API 키가 설정되지 않았습니다.";
        }

        var selectedModel = string.IsNullOrWhiteSpace(modelOverride) ? _config.GeminiModel : modelOverride.Trim();
        var endpoint = $"{_config.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:generateContent";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _config.ChatMaxOutputTokens);
        var promptForTurn = "한국어로 실무적으로 답변하세요.\n\n사용자 입력:\n" + userInput;
        var mergedBuilder = new StringBuilder();

        try
        {
            for (var turn = 0; turn < ChatContinuationRounds; turn++)
            {
                var body = "{"
                    + "\"contents\":[{"
                    + "\"role\":\"user\","
                    + "\"parts\":["
                    + $"{{\"text\":\"{EscapeJson(promptForTurn)}\"}}"
                    + "]"
                    + "}],"
                    + $"\"generationConfig\":{{\"temperature\":0.3,\"maxOutputTokens\":{effectiveMaxOutputTokens}}}"
                    + "}";

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("x-goog-api-key", geminiApiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"[gemini] chat failed ({(int)response.StatusCode}): {responseBody}");
                    return $"Gemini 요청 실패: {(int)response.StatusCode}";
                }

                CaptureGeminiUsage(responseBody);
                var chunk = ExtractGeminiChatChunk(responseBody);
                var chunkText = chunk.Content.Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    if (mergedBuilder.Length > 0)
                    {
                        mergedBuilder.AppendLine();
                    }
                    mergedBuilder.Append(chunkText);
                }

                if (!IsGeminiTruncated(chunk.FinishReason) || string.IsNullOrWhiteSpace(chunkText))
                {
                    break;
                }

                promptForTurn = BuildContinuationPrompt(userInput, mergedBuilder.ToString());
            }

            var text = mergedBuilder.ToString().Trim();
            return string.IsNullOrWhiteSpace(text) ? "Gemini 응답이 비어 있습니다." : text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine($"[gemini] chat timeout (model={selectedModel})");
            return "Gemini 응답 시간이 초과되었습니다.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gemini] chat error: {ex.Message}");
            return $"Gemini 호출 오류: {ex.Message}";
        }
    }

    public async Task<string> GenerateGeminiChatStreamingAsync(
        string userInput,
        string? modelOverride,
        int maxOutputTokens,
        Action<string>? deltaCallback,
        CancellationToken cancellationToken
    )
    {
        var geminiApiKey = _runtimeSettings.GetGeminiApiKey();
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return "Gemini API 키가 설정되지 않았습니다.";
        }

        var selectedModel = string.IsNullOrWhiteSpace(modelOverride) ? _config.GeminiModel : modelOverride.Trim();
        var endpoint = $"{_config.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:streamGenerateContent?alt=sse";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _config.ChatMaxOutputTokens);
        var prompt = "한국어로 실무적으로 답변하세요.\n\n사용자 입력:\n" + userInput;
        var body = "{"
            + "\"contents\":[{"
            + "\"role\":\"user\","
            + "\"parts\":["
            + $"{{\"text\":\"{EscapeJson(prompt)}\"}}"
            + "]"
            + "}],"
            + $"\"generationConfig\":{{\"temperature\":0.3,\"maxOutputTokens\":{effectiveMaxOutputTokens}}}"
            + "}";

        var mergedBuilder = new StringBuilder();
        var eventBuilder = new StringBuilder();
        string? usagePayload = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-goog-api-key", geminiApiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                var failureBody = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.Error.WriteLine($"[gemini] chat stream failed ({(int)response.StatusCode}): {failureBody}");
                return $"Gemini 요청 실패: {(int)response.StatusCode}";
            }

            using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(responseStream, Encoding.UTF8);
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    ConsumeEvent(eventBuilder.ToString());
                    eventBuilder.Clear();
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var payloadLine = line[5..].TrimStart();
                    if (payloadLine.Length > 0)
                    {
                        if (eventBuilder.Length > 0)
                        {
                            eventBuilder.Append('\n');
                        }

                        eventBuilder.Append(payloadLine);
                    }
                }
            }

            if (eventBuilder.Length > 0)
            {
                ConsumeEvent(eventBuilder.ToString());
            }

            if (!string.IsNullOrWhiteSpace(usagePayload))
            {
                CaptureGeminiUsage(usagePayload);
            }

            var content = mergedBuilder.ToString().Trim();
            return string.IsNullOrWhiteSpace(content) ? "Gemini 응답이 비어 있습니다." : content;

            void ConsumeEvent(string eventPayload)
            {
                var trimmedPayload = (eventPayload ?? string.Empty).Trim();
                if (trimmedPayload.Length == 0 || trimmedPayload.Equals("[DONE]", StringComparison.Ordinal))
                {
                    return;
                }

                if (trimmedPayload.Contains("\"usageMetadata\"", StringComparison.Ordinal))
                {
                    usagePayload = trimmedPayload;
                }

                var chunk = ExtractGeminiChatChunk(trimmedPayload);
                var delta = NormalizeGeminiStreamDelta(chunk.Content, mergedBuilder.ToString());
                if (delta.Length == 0)
                {
                    return;
                }

                mergedBuilder.Append(delta);
                SafeEmitDelta(deltaCallback, delta);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine($"[gemini] chat stream timeout (model={selectedModel})");
            var partial = mergedBuilder.ToString().Trim();
            return string.IsNullOrWhiteSpace(partial) ? "Gemini 응답 시간이 초과되었습니다." : partial;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gemini] chat stream error: {ex.Message}");
            var partial = mergedBuilder.ToString().Trim();
            return string.IsNullOrWhiteSpace(partial) ? $"Gemini 호출 오류: {ex.Message}" : partial;
        }
    }

    public async Task<string> GenerateGeminiGroundedChatAsync(
        string prompt,
        string model,
        int maxOutputTokens,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        var geminiApiKey = _runtimeSettings.GetGeminiApiKey();
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return "Gemini API 키가 설정되지 않았습니다.";
        }

        var selectedModel = string.IsNullOrWhiteSpace(model) ? "gemini-3.1-flash-lite-preview" : model.Trim();
        var endpoint = $"{_config.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:generateContent";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, Math.Min(_config.ChatMaxOutputTokens, 4096));
        var effectiveTimeoutMs = NormalizeGeminiGroundedTimeoutMs(timeoutMs);
        var body = BuildGeminiGroundedBody(prompt, effectiveMaxOutputTokens);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveTimeoutMs));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-goog-api-key", geminiApiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[gemini] grounded chat failed ({(int)response.StatusCode}): {responseBody}");
                return $"Gemini 웹검색 요청 실패: {(int)response.StatusCode}";
            }

            CaptureGeminiUsage(responseBody);
            var content = ExtractGeminiText(responseBody).Trim();
            return string.IsNullOrWhiteSpace(content) ? "Gemini 웹검색 응답이 비어 있습니다." : content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine(
                $"[gemini] grounded chat timeout ({effectiveTimeoutMs} ms, model={selectedModel})"
            );
            return "Gemini 웹검색 응답 시간이 초과되었습니다.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gemini] grounded chat error: {ex.Message}");
            return $"Gemini 웹검색 호출 오류: {ex.Message}";
        }
    }

    public async Task<GeminiGroundedChatResponse> GenerateGeminiGroundedChatStreamingAsync(
        string prompt,
        string model,
        int maxOutputTokens,
        int timeoutMs,
        Action<string>? deltaCallback,
        CancellationToken cancellationToken
    )
    {
        var geminiApiKey = _runtimeSettings.GetGeminiApiKey();
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return new GeminiGroundedChatResponse("Gemini API 키가 설정되지 않았습니다.", 0, 0);
        }

        var selectedModel = string.IsNullOrWhiteSpace(model) ? "gemini-3.1-flash-lite-preview" : model.Trim();
        var endpoint = $"{_config.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:streamGenerateContent?alt=sse";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, Math.Min(_config.ChatMaxOutputTokens, 4096));
        var effectiveTimeoutMs = NormalizeGeminiGroundedTimeoutMs(timeoutMs);
        var effectiveFirstChunkTimeoutMs = NormalizeGeminiGroundedFirstChunkTimeoutMs(effectiveTimeoutMs);
        var body = BuildGeminiGroundedBody(prompt, effectiveMaxOutputTokens);
        var stopwatch = Stopwatch.StartNew();
        var firstChunkMs = 0L;
        var streamedTextStarted = false;
        var mergedBuilder = new StringBuilder();

        try
        {
            using var totalTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            totalTimeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveTimeoutMs));
            using var firstChunkTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                totalTimeoutCts.Token
            );
            firstChunkTimeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveFirstChunkTimeoutMs));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-goog-api-key", geminiApiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                firstChunkTimeoutCts.Token
            );
            if (!response.IsSuccessStatusCode)
            {
                var failureBody = await response.Content.ReadAsStringAsync(totalTimeoutCts.Token);
                Console.Error.WriteLine($"[gemini] grounded chat stream failed ({(int)response.StatusCode}): {failureBody}");
                return new GeminiGroundedChatResponse(
                    $"Gemini 웹검색 요청 실패: {(int)response.StatusCode}",
                    0,
                    Math.Max(0L, stopwatch.ElapsedMilliseconds)
                );
            }

            using var responseStream = await response.Content.ReadAsStreamAsync(firstChunkTimeoutCts.Token);
            using var reader = new StreamReader(responseStream);
            var eventBuilder = new StringBuilder();
            string? usagePayload = null;

            while (true)
            {
                var activeToken = streamedTextStarted ? totalTimeoutCts.Token : firstChunkTimeoutCts.Token;
                var line = await reader.ReadLineAsync(activeToken);
                if (line == null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    ConsumeEvent(eventBuilder.ToString());
                    eventBuilder.Clear();
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var payloadLine = line[5..].TrimStart();
                    if (payloadLine.Length > 0)
                    {
                        if (eventBuilder.Length > 0)
                        {
                            eventBuilder.Append('\n');
                        }

                        eventBuilder.Append(payloadLine);
                    }
                }
            }

            if (eventBuilder.Length > 0)
            {
                ConsumeEvent(eventBuilder.ToString());
            }

            if (!string.IsNullOrWhiteSpace(usagePayload))
            {
                CaptureGeminiUsage(usagePayload);
            }

            var content = mergedBuilder.ToString().Trim();
            return new GeminiGroundedChatResponse(
                string.IsNullOrWhiteSpace(content) ? "Gemini 웹검색 응답이 비어 있습니다." : content,
                firstChunkMs,
                Math.Max(0L, stopwatch.ElapsedMilliseconds)
            );

            void ConsumeEvent(string eventPayload)
            {
                var trimmedPayload = (eventPayload ?? string.Empty).Trim();
                if (trimmedPayload.Length == 0 || trimmedPayload.Equals("[DONE]", StringComparison.Ordinal))
                {
                    return;
                }

                if (trimmedPayload.Contains("\"usageMetadata\"", StringComparison.Ordinal))
                {
                    usagePayload = trimmedPayload;
                }

                var chunk = ExtractGeminiChatChunk(trimmedPayload);
                var delta = NormalizeGeminiStreamDelta(chunk.Content, mergedBuilder.ToString());
                if (delta.Length == 0)
                {
                    return;
                }

                mergedBuilder.Append(delta);
                if (!streamedTextStarted)
                {
                    streamedTextStarted = true;
                    firstChunkMs = Math.Max(0L, stopwatch.ElapsedMilliseconds);
                    firstChunkTimeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
                }

                if (deltaCallback != null)
                {
                    try
                    {
                        deltaCallback(delta);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var timeoutKind = streamedTextStarted ? "total" : "first_chunk";
            var timeoutValueMs = streamedTextStarted ? effectiveTimeoutMs : effectiveFirstChunkTimeoutMs;
            Console.Error.WriteLine($"[gemini] grounded chat stream timeout ({timeoutKind}={timeoutValueMs} ms, model={selectedModel})");
            var partialContent = mergedBuilder.ToString().Trim();
            if (streamedTextStarted && !string.IsNullOrWhiteSpace(partialContent))
            {
                return new GeminiGroundedChatResponse(
                    partialContent + "\n\n" + BuildPartialTruncationSuffix("gemini", $"timeout_{timeoutKind}"),
                    firstChunkMs,
                    Math.Max(0L, stopwatch.ElapsedMilliseconds)
                );
            }

            return new GeminiGroundedChatResponse(
                "Gemini 웹검색 응답 시간이 초과되었습니다.",
                firstChunkMs,
                Math.Max(0L, stopwatch.ElapsedMilliseconds)
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gemini] grounded chat stream error: {ex.Message}");
            var partialContent = mergedBuilder.ToString().Trim();
            if (streamedTextStarted && !string.IsNullOrWhiteSpace(partialContent))
            {
                return new GeminiGroundedChatResponse(
                    partialContent + "\n\n" + BuildPartialTruncationSuffix("gemini", ex.Message),
                    firstChunkMs,
                    Math.Max(0L, stopwatch.ElapsedMilliseconds)
                );
            }

            return new GeminiGroundedChatResponse(
                $"Gemini 웹검색 호출 오류: {ex.Message}",
                0,
                Math.Max(0L, stopwatch.ElapsedMilliseconds)
            );
        }
    }

    public async Task<GeminiUrlContextChatResponse> GenerateGeminiUrlContextChatAsync(
        string prompt,
        string model,
        int maxOutputTokens,
        int timeoutMs,
        bool includeGoogleSearch,
        CancellationToken cancellationToken
    )
    {
        var geminiApiKey = _runtimeSettings.GetGeminiApiKey();
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return new GeminiUrlContextChatResponse(
                "Gemini API 키가 설정되지 않았습니다.",
                0,
                0,
                Array.Empty<SearchCitationReference>()
            );
        }

        var selectedModel = string.IsNullOrWhiteSpace(model) ? _config.GeminiModel : model.Trim();
        var endpoint = $"{_config.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:generateContent";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, Math.Min(_config.ChatMaxOutputTokens, 2048));
        var effectiveTimeoutMs = NormalizeGeminiGroundedTimeoutMs(timeoutMs);
        var stopwatch = Stopwatch.StartNew();
        var promptForTurn = prompt;
        var mergedBuilder = new StringBuilder();
        var citationByKey = new Dictionary<string, SearchCitationReference>(StringComparer.OrdinalIgnoreCase);

        try
        {
            for (var turn = 0; turn < ChatContinuationRounds; turn++)
            {
                var body = BuildGeminiUrlContextBody(promptForTurn, effectiveMaxOutputTokens, includeGoogleSearch);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveTimeoutMs));
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("x-goog-api-key", geminiApiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
                var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"[gemini] url context failed ({(int)response.StatusCode}): {responseBody}");
                    return new GeminiUrlContextChatResponse(
                        $"Gemini URL 참조 요청 실패: {(int)response.StatusCode}",
                        0,
                        Math.Max(0L, stopwatch.ElapsedMilliseconds),
                        Array.Empty<SearchCitationReference>()
                    );
                }

                CaptureGeminiUsage(responseBody);
                MergeCitations(ExtractGeminiUrlContextCitations(responseBody));
                MergeCitations(ExtractGeminiGroundingCitations(responseBody));
                var chunk = ExtractGeminiChatChunk(responseBody);
                var chunkText = chunk.Content.Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    if (mergedBuilder.Length > 0)
                    {
                        mergedBuilder.AppendLine();
                    }

                    mergedBuilder.Append(chunkText);
                }

                if (!IsGeminiTruncated(chunk.FinishReason) || string.IsNullOrWhiteSpace(chunkText))
                {
                    break;
                }

                promptForTurn = BuildContinuationPrompt(prompt, mergedBuilder.ToString());
            }

            var content = mergedBuilder.ToString().Trim();
            return new GeminiUrlContextChatResponse(
                string.IsNullOrWhiteSpace(content) ? "Gemini URL 참조 응답이 비어 있습니다." : content,
                0,
                Math.Max(0L, stopwatch.ElapsedMilliseconds),
                citationByKey.Values.ToArray()
            );

            void MergeCitations(IEnumerable<SearchCitationReference> citations)
            {
                foreach (var citation in citations)
                {
                    var key = BuildCitationDedupKey(citation);
                    if (key.Length == 0)
                    {
                        continue;
                    }

                    citationByKey[key] = citation;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine(
                $"[gemini] url context timeout ({effectiveTimeoutMs} ms, model={selectedModel})"
            );
            return new GeminiUrlContextChatResponse(
                "Gemini URL 참조 응답 시간이 초과되었습니다.",
                0,
                Math.Max(0L, stopwatch.ElapsedMilliseconds),
                Array.Empty<SearchCitationReference>()
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gemini] url context error: {ex.Message}");
            return new GeminiUrlContextChatResponse(
                $"Gemini URL 참조 호출 오류: {ex.Message}",
                0,
                Math.Max(0L, stopwatch.ElapsedMilliseconds),
                Array.Empty<SearchCitationReference>()
            );
        }
    }

    public async Task<GeminiUrlContextChatResponse> GenerateGeminiUrlContextChatStreamingAsync(
        string prompt,
        string model,
        int maxOutputTokens,
        int timeoutMs,
        bool includeGoogleSearch,
        Action<string>? deltaCallback,
        CancellationToken cancellationToken
    )
    {
        var geminiApiKey = _runtimeSettings.GetGeminiApiKey();
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return new GeminiUrlContextChatResponse(
                "Gemini API 키가 설정되지 않았습니다.",
                0,
                0,
                Array.Empty<SearchCitationReference>()
            );
        }

        var selectedModel = string.IsNullOrWhiteSpace(model) ? _config.GeminiModel : model.Trim();
        var endpoint = $"{_config.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:streamGenerateContent?alt=sse";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, Math.Min(_config.ChatMaxOutputTokens, 2048));
        var effectiveTimeoutMs = NormalizeGeminiGroundedTimeoutMs(timeoutMs);
        var effectiveFirstChunkTimeoutMs = NormalizeGeminiUrlContextFirstChunkTimeoutMs(effectiveTimeoutMs);
        var body = BuildGeminiUrlContextBody(prompt, effectiveMaxOutputTokens, includeGoogleSearch);
        var stopwatch = Stopwatch.StartNew();
        var firstChunkMs = 0L;
        var streamedTextStarted = false;
        var mergedBuilder = new StringBuilder();
        var citationByUrl = new Dictionary<string, SearchCitationReference>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var totalTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            totalTimeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveTimeoutMs));
            using var firstChunkTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                totalTimeoutCts.Token
            );
            firstChunkTimeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveFirstChunkTimeoutMs));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-goog-api-key", geminiApiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                firstChunkTimeoutCts.Token
            );
            if (!response.IsSuccessStatusCode)
            {
                var failureBody = await response.Content.ReadAsStringAsync(totalTimeoutCts.Token);
                Console.Error.WriteLine($"[gemini] url context stream failed ({(int)response.StatusCode}): {failureBody}");
                return new GeminiUrlContextChatResponse(
                    $"Gemini URL 참조 요청 실패: {(int)response.StatusCode}",
                    0,
                    Math.Max(0L, stopwatch.ElapsedMilliseconds),
                    Array.Empty<SearchCitationReference>()
                );
            }

            using var responseStream = await response.Content.ReadAsStreamAsync(firstChunkTimeoutCts.Token);
            using var reader = new StreamReader(responseStream);
            var eventBuilder = new StringBuilder();
            string? usagePayload = null;

            while (true)
            {
                var activeToken = streamedTextStarted ? totalTimeoutCts.Token : firstChunkTimeoutCts.Token;
                var line = await reader.ReadLineAsync(activeToken);
                if (line == null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    ConsumeEvent(eventBuilder.ToString());
                    eventBuilder.Clear();
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var payloadLine = line[5..].TrimStart();
                    if (payloadLine.Length > 0)
                    {
                        if (eventBuilder.Length > 0)
                        {
                            eventBuilder.Append('\n');
                        }

                        eventBuilder.Append(payloadLine);
                    }
                }
            }

            if (eventBuilder.Length > 0)
            {
                ConsumeEvent(eventBuilder.ToString());
            }

            if (!string.IsNullOrWhiteSpace(usagePayload))
            {
                CaptureGeminiUsage(usagePayload);
            }

            var content = mergedBuilder.ToString().Trim();
            return new GeminiUrlContextChatResponse(
                string.IsNullOrWhiteSpace(content) ? "Gemini URL 참조 응답이 비어 있습니다." : content,
                firstChunkMs,
                Math.Max(0L, stopwatch.ElapsedMilliseconds),
                citationByUrl.Values.ToArray()
            );

            void ConsumeEvent(string eventPayload)
            {
                var trimmedPayload = (eventPayload ?? string.Empty).Trim();
                if (trimmedPayload.Length == 0 || trimmedPayload.Equals("[DONE]", StringComparison.Ordinal))
                {
                    return;
                }

                if (trimmedPayload.Contains("\"usageMetadata\"", StringComparison.Ordinal))
                {
                    usagePayload = trimmedPayload;
                }

                foreach (var citation in ExtractGeminiUrlContextCitations(trimmedPayload))
                {
                    citationByUrl[BuildCitationDedupKey(citation)] = citation;
                }

                foreach (var citation in ExtractGeminiGroundingCitations(trimmedPayload))
                {
                    citationByUrl[BuildCitationDedupKey(citation)] = citation;
                }

                var chunk = ExtractGeminiChatChunk(trimmedPayload);
                var delta = NormalizeGeminiStreamDelta(chunk.Content, mergedBuilder.ToString());
                if (delta.Length == 0)
                {
                    return;
                }

                mergedBuilder.Append(delta);
                if (!streamedTextStarted)
                {
                    streamedTextStarted = true;
                    firstChunkMs = Math.Max(0L, stopwatch.ElapsedMilliseconds);
                    firstChunkTimeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
                }

                if (deltaCallback != null)
                {
                    try
                    {
                        deltaCallback(delta);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var timeoutKind = streamedTextStarted ? "total" : "first_chunk";
            var timeoutValueMs = streamedTextStarted ? effectiveTimeoutMs : effectiveFirstChunkTimeoutMs;
            Console.Error.WriteLine($"[gemini] url context stream timeout ({timeoutKind}={timeoutValueMs} ms, model={selectedModel})");
            var partialContent = mergedBuilder.ToString().Trim();
            if (streamedTextStarted && !string.IsNullOrWhiteSpace(partialContent))
            {
                return new GeminiUrlContextChatResponse(
                    partialContent + "\n\n" + BuildPartialTruncationSuffix("gemini", $"timeout_{timeoutKind}"),
                    firstChunkMs,
                    Math.Max(0L, stopwatch.ElapsedMilliseconds),
                    citationByUrl.Values.ToArray()
                );
            }

            return new GeminiUrlContextChatResponse(
                "Gemini URL 참조 응답 시간이 초과되었습니다.",
                firstChunkMs,
                Math.Max(0L, stopwatch.ElapsedMilliseconds),
                citationByUrl.Values.ToArray()
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gemini] url context stream error: {ex.Message}");
            var partialContent = mergedBuilder.ToString().Trim();
            if (streamedTextStarted && !string.IsNullOrWhiteSpace(partialContent))
            {
                return new GeminiUrlContextChatResponse(
                    partialContent + "\n\n" + BuildPartialTruncationSuffix("gemini", ex.Message),
                    firstChunkMs,
                    Math.Max(0L, stopwatch.ElapsedMilliseconds),
                    citationByUrl.Values.ToArray()
                );
            }

            return new GeminiUrlContextChatResponse(
                $"Gemini URL 참조 호출 오류: {ex.Message}",
                0,
                Math.Max(0L, stopwatch.ElapsedMilliseconds),
                citationByUrl.Values.ToArray()
            );
        }
    }

    public async Task<string> GenerateGeminiMultimodalChatAsync(
        string userInput,
        string? modelOverride,
        IReadOnlyList<InputAttachment> attachments,
        int maxOutputTokens,
        CancellationToken cancellationToken
    )
    {
        var geminiApiKey = _runtimeSettings.GetGeminiApiKey();
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return "Gemini API 키가 설정되지 않았습니다.";
        }

        var selectedModel = string.IsNullOrWhiteSpace(modelOverride) ? _config.GeminiModel : modelOverride.Trim();
        var endpoint = $"{_config.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:generateContent";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _config.ChatMaxOutputTokens);
        var binaryAttachments = (attachments ?? Array.Empty<InputAttachment>())
            .Where(item => !string.IsNullOrWhiteSpace(item.DataBase64))
            .Take(6)
            .ToArray();
        if (binaryAttachments.Length == 0)
        {
            return await GenerateGeminiChatAsync(userInput, selectedModel, maxOutputTokens, cancellationToken);
        }

        try
        {
            var bodyBuilder = new StringBuilder();
            bodyBuilder.Append('{');
            bodyBuilder.Append("\"contents\":[{\"role\":\"user\",\"parts\":[");
            bodyBuilder.Append($"{{\"text\":\"{EscapeJson(userInput)}\"}}");
            foreach (var attachment in binaryAttachments)
            {
                var mimeType = string.IsNullOrWhiteSpace(attachment.MimeType) ? "application/octet-stream" : attachment.MimeType.Trim();
                bodyBuilder.Append(",");
                bodyBuilder.Append("{\"inline_data\":{");
                bodyBuilder.Append($"\"mime_type\":\"{EscapeJson(mimeType)}\",");
                bodyBuilder.Append($"\"data\":\"{EscapeJson(attachment.DataBase64)}\"");
                bodyBuilder.Append("}}");
            }

            bodyBuilder.Append("]}],");
            bodyBuilder.Append($"\"generationConfig\":{{\"temperature\":0.2,\"maxOutputTokens\":{effectiveMaxOutputTokens}}}");
            bodyBuilder.Append("}");
            var body = bodyBuilder.ToString();

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-goog-api-key", geminiApiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[gemini] multimodal chat failed ({(int)response.StatusCode}): {responseBody}");
                return $"Gemini 요청 실패: {(int)response.StatusCode}";
            }

            CaptureGeminiUsage(responseBody);
            var text = ExtractGeminiText(responseBody).Trim();
            return string.IsNullOrWhiteSpace(text) ? "Gemini 응답이 비어 있습니다." : text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine($"[gemini] multimodal chat timeout (model={selectedModel})");
            return "Gemini 응답 시간이 초과되었습니다.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gemini] multimodal chat error: {ex.Message}");
            return $"Gemini 호출 오류: {ex.Message}";
        }
    }

    private static TimeSpan ResolveSharedHttpTimeout(AppConfig config)
    {
        var llmTimeoutMs = Math.Max(5000, config.LlmTimeoutSec * 1000);
        var providerTimeoutMs = Math.Max(
            Math.Max(config.NvidiaTimeoutSec, config.CerebrasTimeoutSec),
            45
        ) * 1000;
        var geminiWebTimeoutMs = NormalizeGeminiGroundedTimeoutMs(config.GeminiWebTimeoutMs);
        return TimeSpan.FromMilliseconds(Math.Max(Math.Max(llmTimeoutMs, providerTimeoutMs + 5000), geminiWebTimeoutMs + 5000));
    }

    private static int NormalizeGeminiGroundedTimeoutMs(int timeoutMs)
    {
        if (timeoutMs <= 0)
        {
            return 30000;
        }

        return Math.Clamp(timeoutMs, 5000, 60000);
    }

    private static int NormalizeGeminiGroundedFirstChunkTimeoutMs(int totalTimeoutMs)
    {
        var normalizedTotal = NormalizeGeminiGroundedTimeoutMs(totalTimeoutMs);
        var derived = Math.Min(7000, Math.Max(3000, normalizedTotal / 4));
        return Math.Clamp(derived, 3000, normalizedTotal);
    }

    private static int NormalizeGeminiUrlContextFirstChunkTimeoutMs(int totalTimeoutMs)
    {
        var normalizedTotal = NormalizeGeminiGroundedTimeoutMs(totalTimeoutMs);
        var derived = Math.Min(30000, Math.Max(8000, normalizedTotal));
        return Math.Clamp(derived, 8000, normalizedTotal);
    }

    private static string BuildGeminiGroundedBody(string prompt, int maxOutputTokens)
    {
        return "{"
            + "\"contents\":[{"
            + "\"role\":\"user\","
            + "\"parts\":["
            + $"{{\"text\":\"{EscapeJson(prompt)}\"}}"
            + "]"
            + "}],"
            + "\"tools\":[{\"google_search\":{}}],"
            + "\"generationConfig\":{"
            + "\"temperature\":0.1,"
            + $"\"maxOutputTokens\":{maxOutputTokens}"
            + "}"
            + "}";
    }

    private static string BuildGeminiUrlContextBody(string prompt, int maxOutputTokens, bool includeGoogleSearch)
    {
        var tools = includeGoogleSearch
            ? "[{\"url_context\":{}},{\"google_search\":{}}]"
            : "[{\"url_context\":{}}]";
        return "{"
            + "\"contents\":[{"
            + "\"role\":\"user\","
            + "\"parts\":["
            + $"{{\"text\":\"{EscapeJson(prompt)}\"}}"
            + "]"
            + "}],"
            + $"\"tools\":{tools},"
            + "\"generationConfig\":{"
            + "\"temperature\":0.1,"
            + $"\"maxOutputTokens\":{maxOutputTokens}"
            + "}"
            + "}";
    }

    private static string NormalizeGeminiStreamDelta(string chunkText, string currentText)
    {
        var nextChunk = chunkText ?? string.Empty;
        if (nextChunk.Length == 0)
        {
            return string.Empty;
        }

        var merged = currentText ?? string.Empty;
        if (merged.Length == 0)
        {
            return nextChunk;
        }

        if (nextChunk.StartsWith(merged, StringComparison.Ordinal))
        {
            return nextChunk[merged.Length..];
        }

        if (merged.EndsWith(nextChunk, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return nextChunk;
    }

    public async Task<string> GenerateCerebrasChatAsync(
        string userInput,
        string? modelOverride,
        int maxOutputTokens,
        CancellationToken cancellationToken
    )
    {
        var cerebrasApiKey = _runtimeSettings.GetCerebrasApiKey();
        if (string.IsNullOrWhiteSpace(cerebrasApiKey))
        {
            return "Cerebras API 키가 설정되지 않았습니다.";
        }

        var selectedModel = string.IsNullOrWhiteSpace(modelOverride) ? _config.CerebrasModel : modelOverride.Trim();
        var effectiveModel = selectedModel;
        var fallbackRetried = false;
        var endpoint = $"{_config.CerebrasBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Respond in Korean with concise and practical answers. "
            + "Answer only the latest user request. Do not switch to news, search summaries, 3D printing, or other unrelated topics unless the user explicitly asks for them. "
            + "If conversation history is provided above, treat the user's short follow-up messages (e.g., '그니까 잘 돌아가?', '이 환경에서?', '24GB 구성임') as continuations of the prior turns. Never reply '이전 대화 맥락이 제공되지 않아' when [최근 대화] is in the prompt — use it. "
            + "When the user asks for your judgment or opinion (어때, 어떻게 생각해, 잘 돌아갈까, 검토해봐, 추천해, 너 생각, 네 의견 등), give a concrete, decisive answer based on the conversation history and your knowledge. Do not deflect with generic disclaimers like '정확도/정밀도/재현율/F1-score 같은 객관적 지표가 필요하다' or '구체적인 평가 지표와 데이터셋이 필요하다'. State your best assessment with a 1-2 sentence rationale, then optionally note any uncertainty. "
            + "Do not repeat the same paragraphs across multiple turns. If the user asks the same thing again, dig deeper or take a clearer stance instead of repeating prior wording.";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _config.ChatMaxOutputTokens);
        var promptForTurn = userInput;
        var mergedBuilder = new StringBuilder();

        try
        {
            for (var turn = 0; turn < ChatContinuationRounds; turn++)
            {
                var body = "{"
                    + $"\"model\":\"{EscapeJson(effectiveModel)}\","
                    + "\"temperature\":0.3,"
                    + $"\"max_completion_tokens\":{effectiveMaxOutputTokens},"
                    + "\"messages\":["
                    + $"{{\"role\":\"system\",\"content\":\"{EscapeJson(systemPrompt)}\"}},"
                    + $"{{\"role\":\"user\",\"content\":\"{EscapeJson(promptForTurn)}\"}}"
                    + "]"
                    + "}";

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cerebrasApiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if (!fallbackRetried
                        && response.StatusCode == System.Net.HttpStatusCode.NotFound
                        && IsCerebrasModelNotFound(responseBody))
                    {
                        // catalog API로 사용자 키가 access 가능한 실제 모델 fetching → 첫 모델로 retry.
                        var resolved = await ResolveAvailableCerebrasModelAsync(cerebrasApiKey, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(resolved)
                            && !string.Equals(resolved, effectiveModel, StringComparison.OrdinalIgnoreCase))
                        {
                            fallbackRetried = true;
                            Console.Error.WriteLine($"[cerebras] model_not_found fallback (from catalog): {effectiveModel} -> {resolved}");
                            effectiveModel = resolved;
                            promptForTurn = userInput;
                            mergedBuilder.Clear();
                            turn = -1;
                            continue;
                        }

                        Console.Error.WriteLine($"[cerebras] chat failed ({(int)response.StatusCode}): {responseBody} | catalog로도 가용 모델을 찾지 못했습니다. API 키 권한을 확인하세요.");
                        return $"Cerebras 키가 어떤 모델에도 접근 권한이 없습니다. https://cloud.cerebras.ai 에서 키 권한과 사용 가능한 모델 목록을 확인하세요. (요청한 모델: {effectiveModel})";
                    }

                    Console.Error.WriteLine($"[cerebras] chat failed ({(int)response.StatusCode}): {responseBody}");
                    return BuildCerebrasHttpFailureText(response.StatusCode, effectiveModel);
                }

                var chunk = ExtractGroqChatChunk(responseBody);
                var chunkText = chunk.Content.Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    if (mergedBuilder.Length > 0)
                    {
                        mergedBuilder.AppendLine();
                    }

                    mergedBuilder.Append(chunkText);
                }

                if (!IsOpenAiCompatibleTruncated(chunk.FinishReason) || string.IsNullOrWhiteSpace(chunkText))
                {
                    break;
                }

                promptForTurn = BuildContinuationPrompt(userInput, mergedBuilder.ToString());
            }

            var content = mergedBuilder.ToString().Trim();
            return string.IsNullOrWhiteSpace(content) ? "Cerebras 응답이 비어 있습니다." : content;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cerebras] chat error: {ex.Message}");
            return $"Cerebras 호출 오류: {ex.Message}";
        }
    }

    public async Task<string> GenerateCerebrasChatStreamingAsync(
        string userInput,
        string? modelOverride,
        int maxOutputTokens,
        Action<string>? deltaCallback,
        CancellationToken cancellationToken
    )
    {
        var cerebrasApiKey = _runtimeSettings.GetCerebrasApiKey();
        if (string.IsNullOrWhiteSpace(cerebrasApiKey))
        {
            return "Cerebras API 키가 설정되지 않았습니다.";
        }

        var initialModel = string.IsNullOrWhiteSpace(modelOverride) ? _config.CerebrasModel : modelOverride.Trim();
        // 직전 호출에서 catalog로 가용 모델을 이미 알아냈다면 그것을 우선 사용
        if (!string.IsNullOrWhiteSpace(_cerebrasResolvedModelCache)
            && (DateTime.UtcNow - _cerebrasResolvedModelCachedAt).TotalSeconds < 60
            && string.IsNullOrWhiteSpace(modelOverride))
        {
            initialModel = _cerebrasResolvedModelCache!;
        }
        var endpoint = $"{_config.CerebrasBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Respond in Korean with concise and practical answers. "
            + "Answer only the latest user request. Do not switch to news, search summaries, 3D printing, or other unrelated topics unless the user explicitly asks for them. "
            + "If conversation history is provided above, treat the user's short follow-up messages (e.g., '그니까 잘 돌아가?', '이 환경에서?', '24GB 구성임') as continuations of the prior turns. Never reply '이전 대화 맥락이 제공되지 않아' when [최근 대화] is in the prompt — use it. "
            + "When the user asks for your judgment or opinion (어때, 어떻게 생각해, 잘 돌아갈까, 검토해봐, 추천해, 너 생각, 네 의견 등), give a concrete, decisive answer based on the conversation history and your knowledge. Do not deflect with generic disclaimers like '정확도/정밀도/재현율/F1-score 같은 객관적 지표가 필요하다' or '구체적인 평가 지표와 데이터셋이 필요하다'. State your best assessment with a 1-2 sentence rationale, then optionally note any uncertainty. "
            + "Do not repeat the same paragraphs across multiple turns. If the user asks the same thing again, dig deeper or take a clearer stance instead of repeating prior wording.";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _config.ChatMaxOutputTokens);

        var firstResult = await GenerateOpenAiCompatibleChatStreamingAsync(
            "cerebras",
            endpoint,
            cerebrasApiKey,
            initialModel,
            systemPrompt,
            userInput,
            "max_completion_tokens",
            effectiveMaxOutputTokens,
            deltaCallback,
            cancellationToken
        );

        // 404로 시작하면 catalog 호출 후 재시도
        if (firstResult.StartsWith("Cerebras 요청 실패: 404", StringComparison.Ordinal))
        {
            var resolved = await ResolveAvailableCerebrasModelAsync(cerebrasApiKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(resolved)
                && !string.Equals(resolved, initialModel, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"[cerebras] streaming fallback (from catalog): {initialModel} -> {resolved}");
                return await GenerateOpenAiCompatibleChatStreamingAsync(
                    "cerebras",
                    endpoint,
                    cerebrasApiKey,
                    resolved,
                    systemPrompt,
                    userInput,
                    "max_completion_tokens",
                    effectiveMaxOutputTokens,
                    deltaCallback,
                    cancellationToken
                );
            }
            return $"Cerebras 키가 어떤 모델에도 접근 권한이 없습니다. https://cloud.cerebras.ai 에서 키 권한과 사용 가능한 모델 목록을 확인하세요. (요청한 모델: {initialModel})";
        }

        return firstResult;
    }

    public async Task<string> GenerateNvidiaChatAsync(
        string userInput,
        string? modelOverride,
        int maxOutputTokens,
        CancellationToken cancellationToken
    )
    {
        var nvidiaApiKey = _runtimeSettings.GetNvidiaApiKey();
        if (string.IsNullOrWhiteSpace(nvidiaApiKey))
        {
            return "NVIDIA NIM API 키가 설정되지 않았습니다.";
        }

        var model = string.IsNullOrWhiteSpace(modelOverride) ? _config.NvidiaModel : modelOverride.Trim();
        var endpoint = $"{_config.NvidiaBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Respond in Korean with concise and practical answers. "
            + "Answer only the latest user request. Do not switch to news, search summaries, 3D printing, or other unrelated topics unless the user explicitly asks for them. "
            + "If conversation history is provided above, treat the user's short follow-up messages (e.g., '그니까 잘 돌아가?', '이 환경에서?', '24GB 구성임') as continuations of the prior turns. Never reply '이전 대화 맥락이 제공되지 않아' when [최근 대화] is in the prompt — use it. "
            + "When the user asks for your judgment or opinion (어때, 어떻게 생각해, 잘 돌아갈까, 검토해봐, 추천해, 너 생각, 네 의견 등), give a concrete, decisive answer based on the conversation history and your knowledge. Do not deflect with generic disclaimers like '정확도/정밀도/재현율/F1-score 같은 객관적 지표가 필요하다' or '구체적인 평가 지표와 데이터셋이 필요하다'. State your best assessment with a 1-2 sentence rationale, then optionally note any uncertainty. "
            + "Do not repeat the same paragraphs across multiple turns. If the user asks the same thing again, dig deeper or take a clearer stance instead of repeating prior wording.";
        var effectiveMaxOutputTokens = NormalizeNvidiaMaxOutputTokens(maxOutputTokens);
        var promptForTurn = userInput;
        var mergedBuilder = new StringBuilder();

        try
        {
            for (var turn = 0; turn < ChatContinuationRounds; turn++)
            {
                var body = BuildOpenAiCompatibleChatBody(
                    model,
                    systemPrompt,
                    promptForTurn,
                    "max_tokens",
                    effectiveMaxOutputTokens,
                    stream: false
                );

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", nvidiaApiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    responseBody = await PollNvidiaStatusAsync(nvidiaApiKey, responseBody, cancellationToken);
                }
                else if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"[nvidia] chat failed ({(int)response.StatusCode}): {responseBody}");
                    return $"NVIDIA NIM 요청 실패: {(int)response.StatusCode}";
                }

                var chunk = ExtractGroqChatChunk(responseBody);
                var chunkText = chunk.Content.Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    if (mergedBuilder.Length > 0)
                    {
                        mergedBuilder.AppendLine();
                    }

                    mergedBuilder.Append(chunkText);
                }

                if (!IsOpenAiCompatibleTruncated(chunk.FinishReason) || string.IsNullOrWhiteSpace(chunkText))
                {
                    break;
                }

                promptForTurn = BuildContinuationPrompt(userInput, mergedBuilder.ToString());
            }

            var content = mergedBuilder.ToString().Trim();
            return string.IsNullOrWhiteSpace(content) ? "NVIDIA NIM 응답이 비어 있습니다." : content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "NVIDIA NIM 응답 시간이 초과되었습니다.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[nvidia] chat error: {ex.Message}");
            return $"NVIDIA NIM 호출 오류: {ex.Message}";
        }
    }

    public async Task<string> GenerateNvidiaChatStreamingAsync(
        string userInput,
        string? modelOverride,
        int maxOutputTokens,
        Action<string>? deltaCallback,
        CancellationToken cancellationToken
    )
    {
        var nvidiaApiKey = _runtimeSettings.GetNvidiaApiKey();
        if (string.IsNullOrWhiteSpace(nvidiaApiKey))
        {
            return "NVIDIA NIM API 키가 설정되지 않았습니다.";
        }

        var model = string.IsNullOrWhiteSpace(modelOverride) ? _config.NvidiaModel : modelOverride.Trim();
        var endpoint = $"{_config.NvidiaBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Respond in Korean with concise and practical answers. "
            + "Answer only the latest user request. Do not switch to news, search summaries, 3D printing, or other unrelated topics unless the user explicitly asks for them. "
            + "If conversation history is provided above, treat the user's short follow-up messages (e.g., '그니까 잘 돌아가?', '이 환경에서?', '24GB 구성임') as continuations of the prior turns. Never reply '이전 대화 맥락이 제공되지 않아' when [최근 대화] is in the prompt — use it. "
            + "When the user asks for your judgment or opinion (어때, 어떻게 생각해, 잘 돌아갈까, 검토해봐, 추천해, 너 생각, 네 의견 등), give a concrete, decisive answer based on the conversation history and your knowledge. Do not deflect with generic disclaimers like '정확도/정밀도/재현율/F1-score 같은 객관적 지표가 필요하다' or '구체적인 평가 지표와 데이터셋이 필요하다'. State your best assessment with a 1-2 sentence rationale, then optionally note any uncertainty. "
            + "Do not repeat the same paragraphs across multiple turns. If the user asks the same thing again, dig deeper or take a clearer stance instead of repeating prior wording.";
        return await GenerateOpenAiCompatibleChatStreamingAsync(
            "nvidia",
            endpoint,
            nvidiaApiKey,
            model,
            systemPrompt,
            userInput,
            "max_tokens",
            NormalizeNvidiaMaxOutputTokens(maxOutputTokens),
            deltaCallback,
            cancellationToken
        );
    }

    private async Task<string?> ResolveAvailableCerebrasModelAsync(string apiKey, CancellationToken cancellationToken)
    {
        // 60초 캐시
        if (_cerebrasResolvedModelCache != null
            && (DateTime.UtcNow - _cerebrasResolvedModelCachedAt).TotalSeconds < 60)
        {
            return _cerebrasResolvedModelCache;
        }
        try
        {
            var endpoint = $"{_config.CerebrasBaseUrl.TrimEnd('/')}/models";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[cerebras] catalog fetch failed ({(int)response.StatusCode}): {body}");
                return null;
            }
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            string? firstId = null;
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    firstId = id;
                    break;
                }
            }
            if (!string.IsNullOrWhiteSpace(firstId))
            {
                _cerebrasResolvedModelCache = firstId;
                _cerebrasResolvedModelCachedAt = DateTime.UtcNow;
                Console.Error.WriteLine($"[cerebras] resolved available model from catalog: {firstId}");
            }
            return firstId;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cerebras] catalog fetch error: {ex.Message}");
            return null;
        }
    }

    private static bool IsCerebrasModelNotFound(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (TryGetPropertyCaseInsensitive(doc.RootElement, "code", out var codeElement)
                && codeElement.ValueKind == JsonValueKind.String
                && codeElement.GetString()?.Trim().Equals("model_not_found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }
        catch
        {
        }

        return responseBody.IndexOf("model_not_found", StringComparison.OrdinalIgnoreCase) >= 0
               || responseBody.IndexOf("does not exist or you do not have access", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string BuildCerebrasHttpFailureText(System.Net.HttpStatusCode statusCode, string model)
    {
        var normalizedModel = (model ?? string.Empty).Trim();
        if (statusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            if (string.Equals(normalizedModel, "zai-glm-4.7", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedModel, "qwen-3-235b-a22b-instruct-2507", StringComparison.OrdinalIgnoreCase))
            {
                return $"Cerebras 요청 실패: 429 (rate limit). 현재 {normalizedModel} (preview)는 수요가 높아 free-tier rate limit이 일시적으로 줄어든 상태입니다. 오늘 처음 써도 걸릴 수 있으니 잠시 후 다시 시도하거나 `gpt-oss-120b`로 바꿔 보세요.";
            }

            return "Cerebras 요청 실패: 429 (rate limit). 짧은 시간에 허용된 요청 수를 넘었거나 현재 free-tier 제한에 걸린 상태입니다. 잠시 후 다시 시도해 주세요.";
        }

        if (statusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            if (string.Equals(normalizedModel, "zai-glm-4.7", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedModel, "qwen-3-235b-a22b-instruct-2507", StringComparison.OrdinalIgnoreCase))
            {
                return $"Cerebras 요청 실패: 503 (service unavailable). 현재 {normalizedModel} (preview) 쪽 서버가 일시적으로 과부하이거나 불안정한 상태일 수 있습니다. 잠시 후 다시 시도하거나 `gpt-oss-120b`로 바꿔 보세요.";
            }

            return "Cerebras 요청 실패: 503 (service unavailable). Cerebras 서버가 일시적으로 과부하이거나 불안정한 상태입니다. 잠시 후 다시 시도해 주세요.";
        }

        return $"Cerebras 요청 실패: {(int)statusCode}";
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static int ClampGroqMaxOutputTokens(int value)
    {
        return Math.Clamp(value, 256, 8192);
    }

    private static int ClampGroqMaxOutputTokensForModel(string model, int value)
    {
        var clamped = ClampGroqMaxOutputTokens(value);
        if (IsGroqCompoundModel(model))
        {
            return Math.Min(clamped, 1024);
        }

        return clamped;
    }

    private int NormalizeNvidiaMaxOutputTokens(int requested)
    {
        return Math.Clamp(NormalizeMaxOutputTokens(requested, Math.Min(_config.ChatMaxOutputTokens, 4096)), 256, 4096);
    }

    private static bool IsGroqCompoundModel(string model)
    {
        var normalized = (model ?? string.Empty).Trim();
        return normalized.StartsWith("groq/compound", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveGroqPromptBudgetChars(string model)
    {
        return IsGroqCompoundModel(model) ? 12000 : 22000;
    }

    /// <summary>
    /// 단일 user 프롬프트에서 [최근 대화] 섹션을 파싱해 multi-turn messages로 분리.
    /// [최근 대화] 안의 [user]/[assistant] 블록을 개별 메시지로, [새 요청]은 마지막 user로.
    /// 파싱 실패 시 원본 그대로 단일 user 메시지로 폴백.
    /// </summary>
    private static List<(string Role, string Content)> SplitPromptToMultiTurn(string prompt)
    {
        var fallback = new List<(string, string)> { ("user", prompt) };
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return fallback;
        }

        // [새 요청] 마커 위치 찾기
        var requestIdx = prompt.IndexOf("\n[새 요청]", StringComparison.Ordinal);
        if (requestIdx < 0)
        {
            return fallback;
        }

        // [최근 대화] 섹션 찾기
        var historyMarker = "\n[최근 대화]";
        var historyIdx = prompt.IndexOf(historyMarker, StringComparison.Ordinal);
        if (historyIdx < 0 || historyIdx > requestIdx)
        {
            return fallback;
        }

        // history 섹션 텍스트 추출 ([최근 대화] 이후 ~ [새 요청] 이전)
        var historyStart = historyIdx + historyMarker.Length;
        var historyText = prompt[historyStart..requestIdx].Trim();

        // [새 요청] 이후 텍스트
        var requestText = prompt[(requestIdx + "\n[새 요청]".Length)..].Trim();

        // [컨텍스트 사용 규칙] 등 헤더 부분 (있으면 system 메시지로)
        var headerText = prompt[..historyIdx].Trim();
        var headerClean = headerText
            .Replace("[컨텍스트 사용 규칙]", "", StringComparison.Ordinal)
            .Trim();

        var messages = new List<(string, string)>();

        // 규칙/메모리 노트는 system 메시지에 포함 (있으면)
        var systemContent = headerClean.Length > 0 ? headerClean : "";

        // history 내의 [user]/[assistant] 블록 파싱
        var historyMessages = ParseHistoryBlocks(historyText);

        if (historyMessages.Count == 0)
        {
            return fallback;
        }

        // system + 규칙
        if (systemContent.Length > 0)
        {
            messages.Add(("system_context", systemContent));
        }

        // history 메시지들
        foreach (var (role, content) in historyMessages)
        {
            messages.Add((role, content));
        }

        // 새 요청
        messages.Add(("user", requestText));

        return messages;
    }

    /// <summary>
    /// "[최근 대화]" 안의 [user]/[assistant] 블록을 파싱.
    /// [이전 대화 압축]은 assistant 메시지로 처리.
    /// </summary>
    private static List<(string Role, string Content)> ParseHistoryBlocks(string historyText)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(historyText))
        {
            return result;
        }

        // [이전 대화 압축] 블록 처리
        var compressionIdx = historyText.IndexOf("[이전 대화 압축]", StringComparison.Ordinal);
        var recentTurnIdx = historyText.IndexOf("[최근 턴]", StringComparison.Ordinal);

        if (compressionIdx >= 0)
        {
            var compressionEnd = recentTurnIdx >= 0 ? recentTurnIdx : historyText.Length;
            var compressionText = historyText[compressionIdx..compressionEnd]
                .Replace("[이전 대화 압축]", "", StringComparison.Ordinal).Trim();
            if (compressionText.Length > 0)
            {
                result.Add(("assistant", $"[이전 대화 요약] {compressionText}"));
            }
        }

        // [최근 턴] 이후 또는 전체에서 [user]/[assistant] 블록 파싱
        var parseTarget = recentTurnIdx >= 0
            ? historyText[recentTurnIdx..]
            : (compressionIdx >= 0 ? historyText[..compressionIdx] : historyText);

        var lines = parseTarget.Split('\n');
        string? currentRole = null;
        var currentContent = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[user]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentRole != null && currentContent.Count > 0)
                {
                    result.Add((currentRole, string.Join('\n', currentContent).Trim()));
                }
                currentRole = "user";
                currentContent = new List<string>();
                var rest = trimmed["[user]".Length..].Trim();
                if (rest.Length > 0) currentContent.Add(rest);
            }
            else if (trimmed.StartsWith("[assistant]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentRole != null && currentContent.Count > 0)
                {
                    result.Add((currentRole, string.Join('\n', currentContent).Trim()));
                }
                currentRole = "assistant";
                currentContent = new List<string>();
                var rest = trimmed["[assistant]".Length..].Trim();
                if (rest.Length > 0) currentContent.Add(rest);
            }
            else if (trimmed.StartsWith("[최근 턴]", StringComparison.OrdinalIgnoreCase))
            {
                // 마커 무시
                continue;
            }
            else if (currentRole != null)
            {
                currentContent.Add(line);
            }
        }

        if (currentRole != null && currentContent.Count > 0)
        {
            result.Add((currentRole, string.Join('\n', currentContent).Trim()));
        }

        return result;
    }

    private static string TruncatePromptForGroq(string prompt, int maxChars)
    {
        var normalized = (prompt ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        var safeMax = Math.Max(1200, maxChars);
        if (normalized.Length <= safeMax)
        {
            return normalized;
        }

        var headLen = (int)Math.Round(safeMax * 0.62);
        var tailLen = Math.Max(200, safeMax - headLen - 80);
        if (headLen + tailLen >= normalized.Length)
        {
            return normalized[..safeMax];
        }

        var head = normalized[..headLen].TrimEnd();
        var tail = normalized[^tailLen..].TrimStart();
        return head
               + "\n\n[중간 내용 생략됨: 요청 크기 제한으로 축약]\n\n"
               + tail;
    }

    private static bool TryExtractGroqMaxTokensLimit(string responseBody, out int limit)
    {
        limit = 0;
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        var match = GroqMaxTokensLimitRegex.Match(responseBody);
        if (!match.Success)
        {
            return false;
        }

        var raw = match.Groups["limit"].Success ? match.Groups["limit"].Value : match.Groups["limit2"].Value;
        return int.TryParse(raw, out limit) && limit > 0;
    }

    private static bool IsGroqRequestTooLarge(System.Net.HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode == System.Net.HttpStatusCode.RequestEntityTooLarge)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        return responseBody.IndexOf("request_too_large", StringComparison.OrdinalIgnoreCase) >= 0
               || responseBody.IndexOf("entity too large", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int GetGroqRetryDelayMs(HttpResponseHeaders headers, int fallbackMs)
    {
        var retryAfter = ReadHeaderString(headers, "retry-after");
        if (int.TryParse(retryAfter, out var seconds) && seconds > 0)
        {
            return Math.Clamp(seconds * 1000, 400, 5000);
        }

        return Math.Clamp(fallbackMs, 400, 5000);
    }

    private static int NormalizeMaxOutputTokens(int requested, int fallback)
    {
        var value = requested > 0 ? requested : fallback;
        if (value <= 0)
        {
            value = 4096;
        }

        return Math.Clamp(value, 256, 32768);
    }

    private static bool IsImageAttachment(InputAttachment attachment)
    {
        if (attachment.IsImage)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(attachment.MimeType)
            && attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = (attachment.Name ?? string.Empty).Trim().ToLowerInvariant();
        return name.EndsWith(".png", StringComparison.Ordinal)
               || name.EndsWith(".jpg", StringComparison.Ordinal)
               || name.EndsWith(".jpeg", StringComparison.Ordinal)
               || name.EndsWith(".webp", StringComparison.Ordinal)
               || name.EndsWith(".gif", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private void CaptureGroqUsage(string model, string responseBody)
    {
        var changed = false;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("usage", out var usageElement) || usageElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var promptTokens = GetInt(usageElement, "prompt_tokens");
            var completionTokens = GetInt(usageElement, "completion_tokens");
            var totalTokens = GetInt(usageElement, "total_tokens");

            lock (_groqLock)
            {
                if (!_groqUsageByModel.TryGetValue(model, out var usage))
                {
                    usage = new GroqUsage();
                    _groqUsageByModel[model] = usage;
                }

                usage.Requests += 1;
                usage.PromptTokens += promptTokens;
                usage.CompletionTokens += completionTokens;
                usage.TotalTokens += totalTokens;
                usage.LastUpdatedUtc = DateTimeOffset.UtcNow;
                changed = true;
            }
        }
        catch
        {
        }

        if (changed)
        {
            SaveUsageState();
        }
    }

    private void CaptureGroqRateLimitHeaders(string model, HttpResponseHeaders headers)
    {
        var limitRequests = ReadHeaderLong(headers, "x-ratelimit-limit-requests");
        var remainingRequests = ReadHeaderLong(headers, "x-ratelimit-remaining-requests");
        var limitTokens = ReadHeaderLong(headers, "x-ratelimit-limit-tokens");
        var remainingTokens = ReadHeaderLong(headers, "x-ratelimit-remaining-tokens");
        var resetRequests = ReadHeaderString(headers, "x-ratelimit-reset-requests");
        var resetTokens = ReadHeaderString(headers, "x-ratelimit-reset-tokens");

        lock (_groqLock)
        {
            _groqRateByModel[model] = new GroqRateLimit
            {
                LimitRequests = limitRequests,
                RemainingRequests = remainingRequests,
                LimitTokens = limitTokens,
                RemainingTokens = remainingTokens,
                ResetRequests = resetRequests,
                ResetTokens = resetTokens,
                LastUpdatedUtc = DateTimeOffset.UtcNow
            };
        }

        SaveUsageState();
    }

    private void CaptureGeminiUsage(string responseBody)
    {
        var promptTokens = 0;
        var completionTokens = 0;
        var totalTokens = 0;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("usageMetadata", out var usageElement) || usageElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            promptTokens = GetInt(usageElement, "promptTokenCount");
            completionTokens = GetInt(usageElement, "candidatesTokenCount");
            totalTokens = GetInt(usageElement, "totalTokenCount");
        }
        catch
        {
            return;
        }

        var addedCost = ((decimal)promptTokens * _config.GeminiInputPricePerMillionUsd / 1_000_000m)
                        + ((decimal)completionTokens * _config.GeminiOutputPricePerMillionUsd / 1_000_000m);
        lock (_geminiLock)
        {
            _geminiUsage.Requests += 1;
            _geminiUsage.PromptTokens += promptTokens;
            _geminiUsage.CompletionTokens += completionTokens;
            _geminiUsage.TotalTokens += totalTokens;
            _geminiUsage.EstimatedCostUsd += addedCost;
            _geminiUsage.LastUpdatedUtc = DateTimeOffset.UtcNow;
        }

        SaveUsageState();
    }

    private void LoadUsageState()
    {
        try
        {
            var fullPath = Path.GetFullPath(_usageStatePath);
            if (!File.Exists(fullPath))
            {
                return;
            }

            var json = File.ReadAllText(fullPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var state = JsonSerializer.Deserialize(json, OmniJsonContext.Default.LlmUsageState);
            if (state == null)
            {
                return;
            }

            lock (_groqLock)
            {
                _groqUsageByModel.Clear();
                foreach (var item in state.GroqUsageByModel)
                {
                    _groqUsageByModel[item.Key] = item.Value ?? new GroqUsage();
                }

                _groqRateByModel.Clear();
                foreach (var item in state.GroqRateByModel)
                {
                    _groqRateByModel[item.Key] = item.Value ?? new GroqRateLimit();
                }
            }

            lock (_geminiLock)
            {
                _geminiUsage = state.GeminiUsage ?? new GeminiUsage();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[usage] load failed: {ex.Message}");
        }
    }

    private void SaveUsageState()
    {
        try
        {
            var fullPath = Path.GetFullPath(_usageStatePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            Directory.CreateDirectory(dir);
            var state = new LlmUsageState();
            lock (_groqLock)
            {
                state.GroqUsageByModel = _groqUsageByModel.ToDictionary(
                    x => x.Key,
                    x => x.Value.Clone(),
                    StringComparer.OrdinalIgnoreCase
                );
                state.GroqRateByModel = _groqRateByModel.ToDictionary(
                    x => x.Key,
                    x => x.Value.Clone(),
                    StringComparer.OrdinalIgnoreCase
                );
            }

            lock (_geminiLock)
            {
                state.GeminiUsage = _geminiUsage.Clone();
            }

            var json = JsonSerializer.Serialize(state, OmniJsonContext.Default.LlmUsageState);
            AtomicFileStore.WriteAllText(fullPath, json, ownerOnly: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[usage] save failed: {ex.Message}");
        }
    }

    private static long? ReadHeaderLong(HttpResponseHeaders headers, string key)
    {
        if (!headers.TryGetValues(key, out var values))
        {
            return null;
        }

        var first = values.FirstOrDefault();
        if (long.TryParse(first, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? ReadHeaderString(HttpResponseHeaders headers, string key)
    {
        if (!headers.TryGetValues(key, out var values))
        {
            return null;
        }

        return values.FirstOrDefault();
    }

    private static int GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            return 0;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static RouterIntent ClassifyIntentFallback(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();

        if (normalized.StartsWith("/kill ", StringComparison.Ordinal) || normalized.Contains("terminate", StringComparison.Ordinal))
        {
            return RouterIntent.OsControl;
        }

        if (normalized.StartsWith("/metrics", StringComparison.Ordinal)
            || normalized.Contains("status", StringComparison.Ordinal)
            || normalized.Contains("로그", StringComparison.Ordinal))
        {
            return RouterIntent.QuerySystem;
        }

        if (normalized.StartsWith("/code ", StringComparison.Ordinal)
            || normalized.Contains("script", StringComparison.Ordinal)
            || normalized.Contains("파이썬", StringComparison.Ordinal))
        {
            return RouterIntent.DynamicCode;
        }

        return RouterIntent.Unknown;
    }

    private static string BuildFallbackPlan(string userInput, string systemContext)
    {
        return $"""
                [Execution Plan]
                1. Validate requested task from user input.
                2. Use current system context snapshot for safety checks.
                3. Generate a minimal script to satisfy the request.
                4. Print deterministic stdout only.
                Input: {userInput}
                Context: {systemContext}
                """;
    }

    private static string BuildPlanningPrompt(
        string objective,
        IReadOnlyList<string> constraints,
        string systemContext,
        string mode
    )
    {
        var constraintsText = constraints == null || constraints.Count == 0
            ? "- 없음"
            : string.Join('\n', constraints.Select(item => $"- {item}"));
        var normalizedMode = string.Equals(mode, "interview", StringComparison.OrdinalIgnoreCase)
            ? "interview"
            : "fast";

        return $"""
                한국어로만 답하라.
                너의 역할은 Omni-node Planner다.
                아래 목표를 실제 구현 가능한 작업 계획으로 분해하라.
                과도한 설명 없이 바로 실행 가능한 단계만 작성하라.

                반드시 아래 형식만 사용한다.
                TITLE: 한 줄 제목
                STEPS:
                1. ...
                2. ...
                3. ...

                규칙:
                - 단계는 4개 이상 8개 이하
                - 각 단계는 실제 저장소 작업 단위로 쓴다
                - 문서/검증 단계도 포함한다
                - mode=interview면 첫 단계에 모호한 지점 확인을 넣는다

                [목표]
                {objective}

                [제약사항]
                {constraintsText}

                [planning_mode]
                {normalizedMode}

                [context]
                {systemContext}
                """;
    }

    private static string BuildPlanReviewPrompt(WorkPlan plan, string systemContext)
    {
        var builder = new StringBuilder();
        builder.AppendLine("한국어로만 답하라.");
        builder.AppendLine("너의 역할은 Omni-node Reviewer다.");
        builder.AppendLine("아래 계획에서 빠진 검증, 위험, 범위 누락만 짚어라.");
        builder.AppendLine("200자 이내의 짧은 리뷰 요약만 출력하라.");
        builder.AppendLine();
        builder.AppendLine("[목표]");
        builder.AppendLine(plan.Objective);
        builder.AppendLine();
        builder.AppendLine("[제약사항]");
        if (plan.Constraints.Count == 0)
        {
            builder.AppendLine("- 없음");
        }
        else
        {
            foreach (var item in plan.Constraints)
            {
                builder.AppendLine($"- {item}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("[단계]");
        foreach (var step in plan.Steps)
        {
            builder.AppendLine($"- {step.StepId}: {step.Description}");
            foreach (var item in step.Verification)
            {
                builder.AppendLine($"  verification: {item}");
            }
        }

        if (!string.IsNullOrWhiteSpace(systemContext))
        {
            builder.AppendLine();
            builder.AppendLine("[project_context]");
            builder.AppendLine(systemContext);
        }

        return builder.ToString().Trim();
    }

    private static string BuildFallbackPlanReview(WorkPlan plan)
    {
        var verificationGap = plan.Steps.Any(step => step.Verification == null || step.Verification.Count == 0);
        var constraintsMissing = plan.Constraints.Count == 0;
        if (!verificationGap && !constraintsMissing)
        {
            return "큰 누락은 보이지 않지만 실행 전에 검증 명령과 변경 범위를 마지막으로 확인해야 합니다.";
        }

        var issues = new List<string>();
        if (verificationGap)
        {
            issues.Add("검증 단계 보강 필요");
        }

        if (constraintsMissing)
        {
            issues.Add("제약사항 명시 필요");
        }

        return $"보완 필요: {string.Join(", ", issues)}.";
    }

    private static string ExtractGroqContent(string json)
    {
        return ExtractGroqChatChunk(json).Content;
    }

    private static string ExtractGeminiText(string json)
    {
        return ExtractGeminiChatChunk(json).Content;
    }

    private async Task<string> GenerateOpenAiCompatibleChatStreamingAsync(
        string provider,
        string endpoint,
        string apiKey,
        string model,
        string systemPrompt,
        string userInput,
        string maxTokensProperty,
        int maxOutputTokens,
        Action<string>? deltaCallback,
        CancellationToken cancellationToken
    )
    {
        var mergedBuilder = new StringBuilder();
        var promptForTurn = userInput;

        try
        {
            for (var turn = 0; turn < ChatContinuationRounds; turn++)
            {
                var body = BuildOpenAiCompatibleChatBody(
                    model,
                    systemPrompt,
                    promptForTurn,
                    maxTokensProperty,
                    maxOutputTokens,
                    stream: true
                );
                var eventBuilder = new StringBuilder();
                var turnBuilder = new StringBuilder();
                var turnFinishReason = string.Empty;

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );
                if (provider.Equals("nvidia", StringComparison.OrdinalIgnoreCase)
                    && response.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    var acceptedBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    var polled = await PollNvidiaStatusAsync(apiKey, acceptedBody, cancellationToken);
                    var polledChunk = ExtractGroqChatChunk(polled);
                    var finalContent = polledChunk.Content.Trim();
                    if (!string.IsNullOrWhiteSpace(finalContent))
                    {
                        AppendGeneratedChunk(mergedBuilder, finalContent);
                        SafeEmitDelta(deltaCallback, finalContent);
                    }

                    if (!IsOpenAiCompatibleTruncated(polledChunk.FinishReason)
                        || string.IsNullOrWhiteSpace(finalContent))
                    {
                        break;
                    }

                    promptForTurn = BuildContinuationPrompt(userInput, mergedBuilder.ToString());
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var failureBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    Console.Error.WriteLine($"[{provider}] chat stream failed ({(int)response.StatusCode}): {failureBody}");
                    return BuildOpenAiCompatibleFailureMessage(provider, response.StatusCode, failureBody);
                }

                if (provider.Equals("groq", StringComparison.OrdinalIgnoreCase))
                {
                    CaptureGroqRateLimitHeaders(model, response.Headers);
                }

                using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(responseStream, Encoding.UTF8);
                while (true)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line == null)
                    {
                        break;
                    }

                    if (line.Length == 0)
                    {
                        ConsumeOpenAiStreamEvent(eventBuilder.ToString());
                        eventBuilder.Clear();
                        continue;
                    }

                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        var payloadLine = line[5..].TrimStart();
                        if (payloadLine.Length > 0)
                        {
                            if (eventBuilder.Length > 0)
                            {
                                eventBuilder.Append('\n');
                            }

                            eventBuilder.Append(payloadLine);
                        }
                    }
                }

                if (eventBuilder.Length > 0)
                {
                    ConsumeOpenAiStreamEvent(eventBuilder.ToString());
                }

                if (!IsOpenAiCompatibleTruncated(turnFinishReason) || turnBuilder.Length == 0)
                {
                    break;
                }

                promptForTurn = BuildContinuationPrompt(userInput, mergedBuilder.ToString());

                void ConsumeOpenAiStreamEvent(string eventPayload)
                {
                    var payload = (eventPayload ?? string.Empty).Trim();
                    if (payload.Length == 0 || payload.Equals("[DONE]", StringComparison.Ordinal))
                    {
                        return;
                    }

                    var chunk = ExtractOpenAiStreamChunk(payload);
                    if (!string.IsNullOrWhiteSpace(chunk.FinishReason))
                    {
                        turnFinishReason = chunk.FinishReason;
                    }

                    var delta = chunk.Content;
                    if (delta.Length == 0)
                    {
                        return;
                    }

                    mergedBuilder.Append(delta);
                    turnBuilder.Append(delta);
                    SafeEmitDelta(deltaCallback, delta);
                }
            }

            var content = mergedBuilder.ToString().Trim();
            return string.IsNullOrWhiteSpace(content)
                ? $"{ProviderDisplayName(provider)} 응답이 비어 있습니다."
                : content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine($"[{provider}] chat stream timeout (model={model})");
            var partial = mergedBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(partial))
            {
                return BuildOpenAiCompatibleTimeoutMessage(provider);
            }
            return partial + "\n\n" + BuildPartialTruncationSuffix(provider, "timeout");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{provider}] chat stream error: {ex.Message}");
            var partial = mergedBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(partial))
            {
                return $"{ProviderDisplayName(provider)} 호출 오류: {ex.Message}";
            }
            return partial + "\n\n" + BuildPartialTruncationSuffix(provider, ex.Message);
        }
    }

    // 부분 스트림이 끊겼을 때 사용자가 즉시 인식할 수 있도록 한 줄 안내를 본문 끝에 덧붙인다.
    private static string BuildPartialTruncationSuffix(string provider, string reasonHint)
    {
        var name = ProviderDisplayName(provider);
        var safeReason = string.IsNullOrWhiteSpace(reasonHint) ? "stream interrupted" : reasonHint.Trim();
        if (safeReason.Length > 80)
        {
            safeReason = safeReason[..80] + "…";
        }
        return $"⚠️ [{name} 응답이 도중에 끊겼습니다 — {safeReason}. 다시 시도해 주세요.]";
    }

    // NVIDIA NIM에서 자주 보이는 quota/rate 응답을 사용자 친화 메시지로 변환.
    private static string BuildOpenAiCompatibleTimeoutMessage(string provider)
    {
        if (string.Equals(provider, "nvidia", StringComparison.OrdinalIgnoreCase))
        {
            return "NVIDIA NIM 응답 시간이 초과되었습니다. 무료 할당량 한계나 큐잉 지연일 수 있습니다. 잠시 후 다시 시도하거나 다른 provider로 바꿔 보세요.";
        }
        return $"{ProviderDisplayName(provider)} 응답 시간이 초과되었습니다.";
    }

    // OpenAI 호환 응답이 비정상 상태일 때 provider별로 알기 쉬운 메시지를 만든다. NVIDIA의 quota/rate-limit 본문을 식별해 안내.
    private static string BuildOpenAiCompatibleFailureMessage(
        string provider,
        System.Net.HttpStatusCode statusCode,
        string failureBody
    )
    {
        var name = ProviderDisplayName(provider);
        var statusInt = (int)statusCode;
        var lowered = (failureBody ?? string.Empty).ToLowerInvariant();
        var looksLikeQuota = lowered.Contains("quota")
                             || lowered.Contains("rate limit")
                             || lowered.Contains("rate_limit")
                             || lowered.Contains("too many requests")
                             || lowered.Contains("credits");

        if (string.Equals(provider, "nvidia", StringComparison.OrdinalIgnoreCase))
        {
            if (statusCode == System.Net.HttpStatusCode.TooManyRequests || looksLikeQuota)
            {
                return $"{name} 무료 할당량(또는 rate limit)에 도달했습니다 ({statusInt}). 잠시 후 다시 시도하거나 다른 provider(예: Cerebras·Groq·Gemini)로 바꿔 보세요.";
            }
            if (statusCode == System.Net.HttpStatusCode.Unauthorized || statusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return $"{name} 인증 실패 ({statusInt}). API 키를 확인해 주세요.";
            }
            if (statusCode == System.Net.HttpStatusCode.ServiceUnavailable || statusCode == System.Net.HttpStatusCode.BadGateway)
            {
                return $"{name} 서버가 일시적으로 불안정합니다 ({statusInt}). 잠시 후 다시 시도해 주세요.";
            }
        }

        if (statusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            return $"{name} rate limit ({statusInt}). 잠시 후 다시 시도해 주세요.";
        }

        return $"{name} 요청 실패: {statusInt}";
    }

    private static void SafeEmitDelta(Action<string>? deltaCallback, string delta)
    {
        if (deltaCallback == null || string.IsNullOrEmpty(delta))
        {
            return;
        }

        try
        {
            deltaCallback(delta);
        }
        catch
        {
        }
    }

    private static string BuildOpenAiCompatibleChatBody(
        string model,
        string systemPrompt,
        string userInput,
        string maxTokensProperty,
        int maxOutputTokens,
        bool stream
    )
    {
        return "{"
            + $"\"model\":\"{EscapeJson(model)}\","
            + "\"temperature\":0.3,"
            + $"\"stream\":{(stream ? "true" : "false")},"
            + $"\"{EscapeJson(maxTokensProperty)}\":{maxOutputTokens},"
            + "\"messages\":["
            + $"{{\"role\":\"system\",\"content\":\"{EscapeJson(systemPrompt)}\"}},"
            + $"{{\"role\":\"user\",\"content\":\"{EscapeJson(userInput)}\"}}"
            + "]"
            + "}";
    }

    private async Task<string> PollNvidiaStatusAsync(
        string apiKey,
        string acceptedBody,
        CancellationToken cancellationToken
    )
    {
        var requestId = ExtractNvidiaRequestId(acceptedBody);
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new InvalidOperationException("NVIDIA NIM 202 응답에 requestId가 없습니다.");
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(3, _config.NvidiaTimeoutSec));
        var endpoint = $"{_config.NvidiaBaseUrl.TrimEnd('/')}/status/{Uri.EscapeDataString(requestId)}";
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(750, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                continue;
            }

            Console.Error.WriteLine($"[nvidia] status poll failed ({(int)response.StatusCode}): {body}");
            throw new InvalidOperationException($"NVIDIA NIM status polling failed: {(int)response.StatusCode}");
        }

        throw new TimeoutException("NVIDIA NIM status polling timed out.");
    }

    private static string ExtractNvidiaRequestId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (TryGetPropertyCaseInsensitive(doc.RootElement, "requestId", out var requestId)
                && requestId.ValueKind == JsonValueKind.String)
            {
                return requestId.GetString() ?? string.Empty;
            }

            if (TryGetPropertyCaseInsensitive(doc.RootElement, "id", out var id)
                && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString() ?? string.Empty;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string ProviderDisplayName(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "groq" => "Groq",
            "gemini" => "Gemini",
            "cerebras" => "Cerebras",
            "nvidia" => "NVIDIA NIM",
            _ => provider
        };
    }

    private static OpenAiCompatibleStreamChunk ExtractOpenAiStreamChunk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return new OpenAiCompatibleStreamChunk(string.Empty, string.Empty);
            }

            var first = choices[0];
            var finishReason = first.TryGetProperty("finish_reason", out var finishReasonElement)
                ? finishReasonElement.GetString() ?? string.Empty
                : string.Empty;
            if (!first.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
            {
                return new OpenAiCompatibleStreamChunk(string.Empty, finishReason);
            }

            if (!delta.TryGetProperty("content", out var content))
            {
                return new OpenAiCompatibleStreamChunk(string.Empty, finishReason);
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                return new OpenAiCompatibleStreamChunk(content.GetString() ?? string.Empty, finishReason);
            }

            if (content.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder();
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(part.GetString());
                    }
                    else if (part.ValueKind == JsonValueKind.Object
                             && part.TryGetProperty("text", out var textPart)
                             && textPart.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(textPart.GetString());
                    }
                }

                return new OpenAiCompatibleStreamChunk(builder.ToString(), finishReason);
            }
        }
        catch (JsonException)
        {
        }

        return new OpenAiCompatibleStreamChunk(string.Empty, string.Empty);
    }

    private static string ExtractSttText(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("text", out var textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                return textElement.GetString() ?? string.Empty;
            }
        }
        catch
        {
            return json;
        }

        return json;
    }

    private static GroqChatChunk ExtractGroqChatChunk(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            return new GroqChatChunk(string.Empty, string.Empty);
        }

        var first = choices[0];
        var finishReason = first.TryGetProperty("finish_reason", out var finishReasonElement)
            ? finishReasonElement.GetString() ?? string.Empty
            : string.Empty;

        if (!first.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return new GroqChatChunk(string.Empty, finishReason);
        }

        if (!message.TryGetProperty("content", out var content))
        {
            return new GroqChatChunk(string.Empty, finishReason);
        }

        return new GroqChatChunk(content.GetString() ?? string.Empty, finishReason);
    }

    private static GeminiChatChunk ExtractGeminiChatChunk(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
        {
            return new GeminiChatChunk(string.Empty, string.Empty);
        }

        var first = candidates[0];
        var finishReason = first.TryGetProperty("finishReason", out var finishElement)
            ? finishElement.GetString() ?? string.Empty
            : string.Empty;

        if (!first.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
        {
            return new GeminiChatChunk(string.Empty, finishReason);
        }

        if (!content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0)
        {
            return new GeminiChatChunk(string.Empty, finishReason);
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textElement))
            {
                builder.AppendLine(textElement.GetString());
            }
        }

        return new GeminiChatChunk(builder.ToString().Trim(), finishReason);
    }

    private static SearchCitationReference[] ExtractGeminiUrlContextCitations(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                return Array.Empty<SearchCitationReference>();
            }

            var first = candidates[0];
            if (!TryGetPropertyIgnoreCase(first, "urlContextMetadata", out var metadata)
                || metadata.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<SearchCitationReference>();
            }

            if (!TryGetPropertyIgnoreCase(metadata, "urlMetadata", out var urlMetadata)
                || urlMetadata.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SearchCitationReference>();
            }

            var citations = new List<SearchCitationReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in urlMetadata.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var url = GetJsonString(item, "retrievedUrl", "retrieved_url");
                if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
                {
                    continue;
                }

                var status = GetJsonString(item, "urlRetrievalStatus", "url_retrieval_status");
                var citationId = $"urlctx-{citations.Count + 1}";
                citations.Add(new SearchCitationReference(
                    citationId,
                    BuildUrlContextCitationTitle(url),
                    url,
                    string.Empty,
                    string.IsNullOrWhiteSpace(status) ? "URL_CONTEXT" : status,
                    "url_context"
                ));
            }

            return citations.ToArray();
        }
        catch
        {
            return Array.Empty<SearchCitationReference>();
        }
    }

    private static SearchCitationReference[] ExtractGeminiGroundingCitations(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                return Array.Empty<SearchCitationReference>();
            }

            var first = candidates[0];
            if (!TryGetPropertyIgnoreCase(first, "groundingMetadata", out var metadata)
                || metadata.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<SearchCitationReference>();
            }

            if (!TryGetPropertyIgnoreCase(metadata, "groundingChunks", out var groundingChunks)
                || groundingChunks.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SearchCitationReference>();
            }

            var citations = new List<SearchCitationReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in groundingChunks.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryGetPropertyIgnoreCase(item, "web", out var webChunk)
                    || webChunk.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var url = GetJsonString(webChunk, "uri");
                if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
                {
                    continue;
                }

                var title = GetJsonString(webChunk, "title");
                var citationId = $"gsearch-{citations.Count + 1}";
                citations.Add(new SearchCitationReference(
                    citationId,
                    string.IsNullOrWhiteSpace(title) ? BuildUrlContextCitationTitle(url) : title,
                    url,
                    string.Empty,
                    "GOOGLE_SEARCH",
                    "google_search"
                ));
            }

            return citations.ToArray();
        }
        catch
        {
            return Array.Empty<SearchCitationReference>();
        }
    }

    private static string BuildUrlContextCitationTitle(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var host = uri.Host?.Trim() ?? string.Empty;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        return string.IsNullOrWhiteSpace(host) ? url : host;
    }

    private static string GetJsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetPropertyIgnoreCase(element, name, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string BuildCitationDedupKey(SearchCitationReference citation)
    {
        var url = (citation.Url ?? string.Empty).Trim();
        if (url.Length > 0)
        {
            return url;
        }

        var title = (citation.Title ?? string.Empty).Trim();
        return title;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) || property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static bool IsGroqTruncated(string? finishReason)
    {
        return string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenAiCompatibleTruncated(string? finishReason)
    {
        return IsGroqTruncated(finishReason)
               || string.Equals(finishReason, "max_tokens", StringComparison.OrdinalIgnoreCase)
               || string.Equals(finishReason, "token_limit", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendGeneratedChunk(StringBuilder builder, string chunk)
    {
        var normalized = (chunk ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(normalized);
    }

    private static bool IsGeminiTruncated(string? finishReason)
    {
        if (string.IsNullOrWhiteSpace(finishReason))
        {
            return false;
        }

        return finishReason.Contains("MAX_TOKENS", StringComparison.OrdinalIgnoreCase)
               || finishReason.Contains("LENGTH", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildContinuationPrompt(string originalInput, string writtenText)
    {
        var tail = writtenText.Length <= 6000 ? writtenText : writtenText[^6000..];
        return """
               아래 답변이 길이 제한으로 중간에서 끊겼습니다.
               이미 작성된 내용은 절대 반복하지 말고, 바로 다음 문장부터 자연스럽게 이어서만 작성하세요.
               마크다운 머리말/서론 재출력 금지.

               [원래 사용자 입력]
               """
               + originalInput
               + """

               [이미 작성된 답변 끝부분]
               """
               + tail;
    }

    private static string ExtractGeminiBlockReason(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("promptFeedback", out var promptFeedback) || promptFeedback.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!promptFeedback.TryGetProperty("blockReason", out var blockReasonElement))
        {
            return string.Empty;
        }

        return blockReasonElement.GetString() ?? string.Empty;
    }

    private static RouterIntent MapIntent(string content)
    {
        var normalized = content.Trim().ToUpperInvariant();

        if (normalized.Contains("OS_CONTROL", StringComparison.Ordinal))
        {
            return RouterIntent.OsControl;
        }

        if (normalized.Contains("QUERY_SYSTEM", StringComparison.Ordinal))
        {
            return RouterIntent.QuerySystem;
        }

        if (normalized.Contains("DYNAMIC_CODE", StringComparison.Ordinal))
        {
            return RouterIntent.DynamicCode;
        }

        return RouterIntent.Unknown;
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\b", "\\b", StringComparison.Ordinal)
            .Replace("\f", "\\f", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private sealed record GroqChatChunk(string Content, string FinishReason);
    private sealed record GeminiChatChunk(string Content, string FinishReason);
}

public sealed class GroqUsage
{
    public long Requests { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; }

    public GroqUsage Clone()
    {
        return new GroqUsage
        {
            Requests = Requests,
            PromptTokens = PromptTokens,
            CompletionTokens = CompletionTokens,
            TotalTokens = TotalTokens,
            LastUpdatedUtc = LastUpdatedUtc
        };
    }
}

public sealed class GroqRateLimit
{
    public long? LimitRequests { get; set; }
    public long? RemainingRequests { get; set; }
    public long? LimitTokens { get; set; }
    public long? RemainingTokens { get; set; }
    public string? ResetRequests { get; set; }
    public string? ResetTokens { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; }

    public GroqRateLimit Clone()
    {
        return new GroqRateLimit
        {
            LimitRequests = LimitRequests,
            RemainingRequests = RemainingRequests,
            LimitTokens = LimitTokens,
            RemainingTokens = RemainingTokens,
            ResetRequests = ResetRequests,
            ResetTokens = ResetTokens,
            LastUpdatedUtc = LastUpdatedUtc
        };
    }
}

public sealed class GeminiUsage
{
    public long Requests { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; }

    public GeminiUsage Clone()
    {
        return new GeminiUsage
        {
            Requests = Requests,
            PromptTokens = PromptTokens,
            CompletionTokens = CompletionTokens,
            TotalTokens = TotalTokens,
            EstimatedCostUsd = EstimatedCostUsd,
            LastUpdatedUtc = LastUpdatedUtc
        };
    }
}

public sealed class LlmUsageState
{
    public Dictionary<string, GroqUsage> GroqUsageByModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, GroqRateLimit> GroqRateByModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public GeminiUsage GeminiUsage { get; set; } = new();
}
