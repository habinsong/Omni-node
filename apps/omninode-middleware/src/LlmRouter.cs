using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

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
    private readonly ProviderOptions _providers;
    private readonly PathOptions _paths;
    private readonly ContextOptions _context;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly HttpClient _httpClient;
    private readonly SttTranscriptionAdapter _sttTranscriptionAdapter;
    private readonly NvidiaStatusPollingAdapter _nvidiaStatusPollingAdapter;
    private readonly CerebrasModelCatalog _cerebrasModelCatalog;
    private readonly bool _ownsCerebrasModelCatalog;
    private readonly IProviderChatAdapter _openAiCompatibleChatAdapter;
    private readonly IProviderStreamingChatAdapter _openAiCompatibleStreamingChatAdapter;
    private readonly IGeminiGenerateContentAdapter _geminiGenerateContentAdapter;
    private readonly IGeminiStreamingAdapter _geminiStreamingAdapter;
    private readonly object _groqLock = new();
    private readonly object _geminiLock = new();
    private readonly Dictionary<string, GroqUsage> _groqUsageByModel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GroqRateLimit> _groqRateByModel = new(StringComparer.OrdinalIgnoreCase);
    private GeminiUsage _geminiUsage = new();
    private readonly string _usageStatePath;
    private string _selectedGroqModel;
    private readonly AsyncLocal<TokenUsage?> _responseTokenUsage = new();

    public LlmRouter(
        ProviderOptions providers,
        PathOptions paths,
        ContextOptions context,
        RuntimeSettings runtimeSettings,
        CerebrasModelCatalog? cerebrasModelCatalog = null)
    {
        _providers = providers;
        _paths = paths;
        _context = context;
        _runtimeSettings = runtimeSettings;
        _selectedGroqModel = _providers.GroqModel;
        _usageStatePath = _paths.LlmUsageStatePath;
        _httpClient = new HttpClient
        {
            Timeout = ProviderTimeoutPolicy.ResolveSharedHttpTimeout(_providers, _context)
        };
        _sttTranscriptionAdapter = new SttTranscriptionAdapter(_httpClient);
        _nvidiaStatusPollingAdapter = new NvidiaStatusPollingAdapter(_httpClient);
        _cerebrasModelCatalog = cerebrasModelCatalog ?? new CerebrasModelCatalog(_providers, _runtimeSettings);
        _ownsCerebrasModelCatalog = cerebrasModelCatalog == null;
        _openAiCompatibleChatAdapter = new OpenAiCompatibleChatAdapter(_httpClient);
        _openAiCompatibleStreamingChatAdapter = new OpenAiCompatibleStreamingChatAdapter(_httpClient);
        _geminiGenerateContentAdapter = new GeminiGenerateContentAdapter(_httpClient);
        _geminiStreamingAdapter = new GeminiStreamingAdapter(_httpClient);

        LoadUsageState();
    }

    public string GetSelectedGroqModel()
    {
        lock (_groqLock)
        {
            return _selectedGroqModel;
        }
    }

    public void ClearLastResponseTokenUsage()
    {
        _responseTokenUsage.Value = null;
    }

    public TokenUsage? ConsumeLastResponseTokenUsage()
    {
        var usage = _responseTokenUsage.Value;
        _responseTokenUsage.Value = null;
        return usage;
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
        return !string.IsNullOrWhiteSpace(_providers.SttBaseUrl)
               && !string.IsNullOrWhiteSpace(_providers.SttModel)
               && !string.IsNullOrWhiteSpace(_runtimeSettings.GetSttApiKey());
    }

    public async Task<string> TranscribeAudioAsync(InputAttachment attachment, CancellationToken cancellationToken)
    {
        if (!HasSttSettings())
        {
            return "음성 변환 설정 필요: OMNINODE_STT_BASE_URL, OMNINODE_STT_MODEL, OMNINODE_STT_API_KEY를 설정하세요.";
        }

        return await _sttTranscriptionAdapter.TranscribeAsync(
            attachment,
            _providers.SttBaseUrl,
            _providers.SttModel,
            _runtimeSettings.GetSttApiKey() ?? string.Empty,
            cancellationToken
        );
    }

    public async Task<RouterIntent> ClassifyIntentAsync(string input, CancellationToken cancellationToken)
    {
        var fallback = RouterIntentClassifier.ClassifyHeuristic(input);
        var groqApiKey = _runtimeSettings.GetGroqApiKey();
        if (string.IsNullOrWhiteSpace(groqApiKey))
        {
            return fallback;
        }

        var model = GetSelectedGroqModel();
        var endpoint = $"{_providers.GroqBaseUrl.TrimEnd('/')}/chat/completions";
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
            var content = ProviderResponseParser.ExtractOpenAiCompatibleChunk(responseBody).Content;
            var mapped = RouterIntentClassifier.MapFromLlmContent(content);
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
        var fallback = PlanningPromptPolicy.BuildFallbackPlan(userInput, systemContext);
        var geminiApiKey = _runtimeSettings.GetGeminiApiKey();
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return fallback;
        }

        var endpoint = $"{_providers.GeminiBaseUrl.TrimEnd('/')}/models/{_providers.GeminiModel}:generateContent";

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
            var result = await _geminiGenerateContentAdapter.SendAsync(
                new GeminiGenerateContentRequest(endpoint, geminiApiKey, body),
                cancellationToken
            );
            if (!result.IsSuccess)
            {
                Console.Error.WriteLine($"[gemini] plan failed ({(int)result.StatusCode}): {result.ResponseBody}");
                return fallback;
            }

            CaptureGeminiUsage(result.ResponseBody);
            var plan = ProviderResponseParser.ExtractGeminiChunk(result.ResponseBody).Content;
            if (string.IsNullOrWhiteSpace(plan))
            {
                var blockReason = ProviderResponseParser.ExtractGeminiBlockReason(result.ResponseBody);
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

        foreach (var provider in PlanningPromptPolicy.NormalizeProviderChain(providerChain))
        {
            if (provider == "gemini" && HasGeminiApiKey())
            {
                return $"{routeRole}:gemini:{_providers.GeminiModel}";
            }

            if (provider == "groq" && HasGroqApiKey())
            {
                return $"{routeRole}:groq:{GetSelectedGroqModel()}";
            }

            if (provider == "nvidia" && HasNvidiaApiKey())
            {
                return $"{routeRole}:nvidia:{_providers.NvidiaModel}";
            }

            if (provider == "cerebras" && HasCerebrasApiKey())
            {
                return $"{routeRole}:cerebras:{_providers.CerebrasModel}";
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
        var prompt = PlanningPromptPolicy.BuildPlanningPrompt(objective, constraints, systemContext, mode);
        foreach (var provider in PlanningPromptPolicy.NormalizeProviderChain(providerChain))
        {
            if (provider == "gemini" && HasGeminiApiKey())
            {
                return await GenerateGeminiChatAsync(prompt, _providers.GeminiModel, 1024, cancellationToken);
            }

            if (provider == "groq" && HasGroqApiKey())
            {
                return await GenerateGroqChatAsync(prompt, GetSelectedGroqModel(), 1024, cancellationToken);
            }

            if (provider == "nvidia" && HasNvidiaApiKey())
            {
                return await GenerateNvidiaChatAsync(prompt, _providers.NvidiaModel, 1024, cancellationToken);
            }

            if (provider == "cerebras" && HasCerebrasApiKey())
            {
                return await GenerateCerebrasChatAsync(prompt, _providers.CerebrasModel, 1024, cancellationToken);
            }
        }

        return PlanningPromptPolicy.BuildFallbackPlan(objective, systemContext);
    }

    public async Task<string> ReviewWorkPlanAsync(
        WorkPlan plan,
        string systemContext,
        IReadOnlyList<string>? providerChain,
        CancellationToken cancellationToken
    )
    {
        var prompt = PlanningPromptPolicy.BuildPlanReviewPrompt(plan, systemContext);
        foreach (var provider in PlanningPromptPolicy.NormalizeProviderChain(providerChain))
        {
            if (provider == "gemini" && HasGeminiApiKey())
            {
                return await GenerateGeminiChatAsync(prompt, _providers.GeminiModel, 768, cancellationToken);
            }

            if (provider == "groq" && HasGroqApiKey())
            {
                return await GenerateGroqChatAsync(prompt, GetSelectedGroqModel(), 768, cancellationToken);
            }

            if (provider == "nvidia" && HasNvidiaApiKey())
            {
                return await GenerateNvidiaChatAsync(prompt, _providers.NvidiaModel, 768, cancellationToken);
            }

            if (provider == "cerebras" && HasCerebrasApiKey())
            {
                return await GenerateCerebrasChatAsync(prompt, _providers.CerebrasModel, 768, cancellationToken);
            }
        }

        return PlanningPromptPolicy.BuildFallbackPlanReview(plan);
    }

    public Task<string> GenerateGroqChatAsync(
        string userInput,
        string? modelOverride,
        CancellationToken cancellationToken
    )
    {
        return GenerateGroqChatAsync(userInput, modelOverride, _context.ChatMaxOutputTokens, cancellationToken);
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
        var endpoint = $"{_providers.GroqBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Respond in Korean with concise and practical answers. "
            + "Answer only the latest user request. Do not switch to news, search summaries, 3D printing, or other unrelated topics unless the user explicitly asks for them. "
            + "If conversation history is provided above, treat the user's short follow-up messages (e.g., '그니까 잘 돌아가?', '이 환경에서?', '24GB 구성임') as continuations of the prior turns. Never reply '이전 대화 맥락이 제공되지 않아' when [최근 대화] is in the prompt — use it. "
            + "When the user asks for your judgment or opinion (어때, 어떻게 생각해, 잘 돌아갈까, 검토해봐, 추천해, 너 생각, 네 의견 등), give a concrete, decisive answer based on the conversation history and your knowledge. Do not deflect with generic disclaimers like '정확도/정밀도/재현율/F1-score 같은 객관적 지표가 필요하다' or '구체적인 평가 지표와 데이터셋이 필요하다'. State your best assessment with a 1-2 sentence rationale, then optionally note any uncertainty. "
            + "Do not repeat the same paragraphs across multiple turns. If the user asks the same thing again, dig deeper or take a clearer stance instead of repeating prior wording.";
        var requestedMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _context.ChatMaxOutputTokens);
        var effectiveMaxOutputTokens = GroqPromptPolicy.ClampGroqMaxOutputTokensForModel(model, requestedMaxOutputTokens);
        var promptBudgetChars = GroqPromptPolicy.ResolveGroqPromptBudgetChars(model);
        var promptForTurn = GroqPromptPolicy.TruncatePromptForGroq(userInput, promptBudgetChars);
        var mergedBuilder = new StringBuilder();
        var rateLimitRetries = 0;
        var requestTooLargeRetries = 0;
        var tokenLimitRetries = 0;

        try
        {
            for (var turn = 0; turn < ChatContinuationRounds; turn++)
            {
                var promptForRequest = GroqPromptPolicy.TruncatePromptForGroq(promptForTurn, promptBudgetChars);
                var multiTurn = GroqPromptPolicy.SplitPromptToMultiTurn(promptForRequest);
                var result = await _openAiCompatibleChatAdapter.SendAsync(
                    new ProviderChatAdapterRequest(
                        "groq",
                        endpoint,
                        groqApiKey,
                        model,
                        systemPrompt,
                        promptForRequest,
                        multiTurn,
                        "max_tokens",
                        effectiveMaxOutputTokens,
                        OnResponseHeaders: headers => CaptureGroqRateLimitHeaders(model, headers)
                    ),
                    cancellationToken
                );
                if (!result.IsSuccess)
                {
                    if ((int)result.StatusCode == 429 && rateLimitRetries < 2)
                    {
                        rateLimitRetries += 1;
                        await Task.Delay(
                            GroqPromptPolicy.GetGroqRetryDelayMs(
                                result.ResponseHeaders,
                                rateLimitRetries == 1 ? 900 : 1800
                            ),
                            cancellationToken
                        );
                        turn -= 1;
                        continue;
                    }

                    if (GroqPromptPolicy.TryExtractGroqMaxTokensLimit(result.ResponseBody, out var limit)
                        && limit >= 128
                        && effectiveMaxOutputTokens > limit
                        && tokenLimitRetries < 2)
                    {
                        tokenLimitRetries += 1;
                        effectiveMaxOutputTokens = GroqPromptPolicy.ClampGroqMaxOutputTokensForModel(model, limit);
                        turn -= 1;
                        continue;
                    }

                    if (GroqPromptPolicy.IsGroqRequestTooLarge(result.StatusCode, result.ResponseBody) && requestTooLargeRetries < 3)
                    {
                        requestTooLargeRetries += 1;
                        promptBudgetChars = Math.Max(1200, promptBudgetChars / 2);
                        promptForTurn = GroqPromptPolicy.TruncatePromptForGroq(promptForTurn, promptBudgetChars);
                        effectiveMaxOutputTokens = Math.Max(256, effectiveMaxOutputTokens / 2);
                        turn -= 1;
                        continue;
                    }

                    return $"Groq 요청 실패: {(int)result.StatusCode}";
                }

                rateLimitRetries = 0;
                requestTooLargeRetries = 0;
                CaptureGroqUsage(model, result.ResponseBody);
                var chunk = ProviderResponseParser.ExtractOpenAiCompatibleChunk(result.ResponseBody);
                var chunkText = chunk.Content.Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    if (mergedBuilder.Length > 0)
                    {
                        mergedBuilder.AppendLine();
                    }
                    mergedBuilder.Append(chunkText);
                }

                if (!ProviderResponseParser.IsOpenAiCompatibleTruncated(chunk.FinishReason) || string.IsNullOrWhiteSpace(chunkText))
                {
                    break;
                }

                promptForTurn = GroqPromptPolicy.TruncatePromptForGroq(ChatStreamingContinuation.BuildContinuationPrompt(userInput, mergedBuilder.ToString()), promptBudgetChars);
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
        var endpoint = $"{_providers.GroqBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Respond in Korean with concise and practical answers. "
            + "Answer only the latest user request. Do not switch to news, search summaries, 3D printing, or other unrelated topics unless the user explicitly asks for them. "
            + "If conversation history is provided above, treat the user's short follow-up messages (e.g., '그니까 잘 돌아가?', '이 환경에서?', '24GB 구성임') as continuations of the prior turns. Never reply '이전 대화 맥락이 제공되지 않아' when [최근 대화] is in the prompt — use it. "
            + "When the user asks for your judgment or opinion (어때, 어떻게 생각해, 잘 돌아갈까, 검토해봐, 추천해, 너 생각, 네 의견 등), give a concrete, decisive answer based on the conversation history and your knowledge. Do not deflect with generic disclaimers like '정확도/정밀도/재현율/F1-score 같은 객관적 지표가 필요하다' or '구체적인 평가 지표와 데이터셋이 필요하다'. State your best assessment with a 1-2 sentence rationale, then optionally note any uncertainty. "
            + "Do not repeat the same paragraphs across multiple turns. If the user asks the same thing again, dig deeper or take a clearer stance instead of repeating prior wording.";
        var requestedMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _context.ChatMaxOutputTokens);
        var effectiveMaxOutputTokens = GroqPromptPolicy.ClampGroqMaxOutputTokensForModel(model, requestedMaxOutputTokens);
        var promptForRequest = GroqPromptPolicy.TruncatePromptForGroq(userInput, GroqPromptPolicy.ResolveGroqPromptBudgetChars(model));
        var multiTurn = GroqPromptPolicy.SplitPromptToMultiTurn(promptForRequest);

        return await GenerateOpenAiCompatibleChatStreamingAsync(
            "groq",
            endpoint,
            groqApiKey,
            model,
            systemPrompt,
            promptForRequest,
            multiTurn,
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

        var endpoint = $"{_providers.GroqBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Analyze attached images and answer in Korean concisely.";
        var effectiveMaxOutputTokens = GroqPromptPolicy.ClampGroqMaxOutputTokensForModel(model, NormalizeMaxOutputTokens(maxOutputTokens, _context.ChatMaxOutputTokens));

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
                    await Task.Delay(GroqPromptPolicy.GetGroqRetryDelayMs(response.Headers, 1200), cancellationToken);
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
                    if (GroqPromptPolicy.IsGroqRequestTooLarge(response.StatusCode, responseBody))
                    {
                        var fallbackPrompt = "[이미지 첨부는 크기 제한으로 제외됨]\n" + userInput;
                        return await GenerateGroqChatAsync(fallbackPrompt, model, Math.Min(effectiveMaxOutputTokens, 1024), cancellationToken);
                    }

                    Console.Error.WriteLine($"[groq] multimodal chat failed ({(int)response.StatusCode}): {responseBody}");
                    return $"Groq 요청 실패: {(int)response.StatusCode}";
                }
            }

            CaptureGroqUsage(model, responseBody);
            var content = ProviderResponseParser.ExtractOpenAiCompatibleChunk(responseBody).Content.Trim();
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
        return GenerateGeminiChatAsync(userInput, _providers.GeminiModel, _context.ChatMaxOutputTokens, cancellationToken);
    }

    public Task<string> GenerateGeminiChatAsync(
        string userInput,
        int maxOutputTokens,
        CancellationToken cancellationToken
    )
    {
        return GenerateGeminiChatAsync(userInput, _providers.GeminiModel, maxOutputTokens, cancellationToken);
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

        var selectedModel = string.IsNullOrWhiteSpace(modelOverride) ? _providers.GeminiModel : modelOverride.Trim();
        var endpoint = $"{_providers.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:generateContent";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _context.ChatMaxOutputTokens);
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

                var result = await _geminiGenerateContentAdapter.SendAsync(
                    new GeminiGenerateContentRequest(endpoint, geminiApiKey, body),
                    cancellationToken
                );
                if (!result.IsSuccess)
                {
                    Console.Error.WriteLine($"[gemini] chat failed ({(int)result.StatusCode}): {result.ResponseBody}");
                    return $"Gemini 요청 실패: {(int)result.StatusCode}";
                }

                CaptureGeminiUsage(result.ResponseBody);
                var chunk = ProviderResponseParser.ExtractGeminiChunk(result.ResponseBody);
                var chunkText = chunk.Content.Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    if (mergedBuilder.Length > 0)
                    {
                        mergedBuilder.AppendLine();
                    }
                    mergedBuilder.Append(chunkText);
                }

                if (!ProviderResponseParser.IsGeminiTruncated(chunk.FinishReason) || string.IsNullOrWhiteSpace(chunkText))
                {
                    break;
                }

                promptForTurn = ChatStreamingContinuation.BuildContinuationPrompt(userInput, mergedBuilder.ToString());
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

        var selectedModel = string.IsNullOrWhiteSpace(modelOverride) ? _providers.GeminiModel : modelOverride.Trim();
        var endpoint = $"{_providers.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:streamGenerateContent?alt=sse";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _context.ChatMaxOutputTokens);
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
        string? usagePayload = null;
        try
        {
            var result = await _geminiStreamingAdapter.StreamAsync(
                new GeminiStreamingAdapterRequest(
                    endpoint,
                    geminiApiKey,
                    body,
                    cancellationToken,
                    cancellationToken,
                    () => cancellationToken,
                    ConsumeEvent
                ),
                cancellationToken
            );
            if (!result.IsSuccess)
            {
                Console.Error.WriteLine($"[gemini] chat stream failed ({(int)result.StatusCode}): {result.FailureBody}");
                return $"Gemini 요청 실패: {(int)result.StatusCode}";
            }

            if (!string.IsNullOrWhiteSpace(usagePayload))
            {
                CaptureGeminiUsage(usagePayload);
            }

            var content = mergedBuilder.ToString().Trim();
            return string.IsNullOrWhiteSpace(content) ? "Gemini 응답이 비어 있습니다." : content;

            void ConsumeEvent(string eventPayload)
            {
                if (eventPayload.Contains("\"usageMetadata\"", StringComparison.Ordinal))
                {
                    usagePayload = eventPayload;
                }

                var chunk = ProviderResponseParser.ExtractGeminiChunk(eventPayload);
                var delta = GeminiRequestPolicy.NormalizeStreamDelta(chunk.Content, mergedBuilder.ToString());
                if (delta.Length == 0)
                {
                    return;
                }

                mergedBuilder.Append(delta);
                ChatStreamingContinuation.SafeEmitDelta(deltaCallback, delta);
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
        var endpoint = $"{_providers.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:generateContent";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, Math.Min(_context.ChatMaxOutputTokens, 4096));
        var effectiveTimeoutMs = ProviderTimeoutPolicy.NormalizeGeminiGroundedTimeoutMs(timeoutMs);
        var body = GeminiRequestPolicy.BuildGroundedBody(prompt, effectiveMaxOutputTokens);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveTimeoutMs));
            var result = await _geminiGenerateContentAdapter.SendAsync(
                new GeminiGenerateContentRequest(endpoint, geminiApiKey, body),
                timeoutCts.Token
            );
            if (!result.IsSuccess)
            {
                Console.Error.WriteLine($"[gemini] grounded chat failed ({(int)result.StatusCode}): {result.ResponseBody}");
                return $"Gemini 웹검색 요청 실패: {(int)result.StatusCode}";
            }

            CaptureGeminiUsage(result.ResponseBody);
            var content = ProviderResponseParser.ExtractGeminiChunk(result.ResponseBody).Content.Trim();
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
        var endpoint = $"{_providers.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:streamGenerateContent?alt=sse";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, Math.Min(_context.ChatMaxOutputTokens, 4096));
        var effectiveTimeoutMs = ProviderTimeoutPolicy.NormalizeGeminiGroundedTimeoutMs(timeoutMs);
        var effectiveFirstChunkTimeoutMs = ProviderTimeoutPolicy.NormalizeGeminiGroundedFirstChunkTimeoutMs(effectiveTimeoutMs);
        var body = GeminiRequestPolicy.BuildGroundedBody(prompt, effectiveMaxOutputTokens);
        var stopwatch = Stopwatch.StartNew();
        var firstChunkMs = 0L;
        var streamedTextStarted = false;
        var mergedBuilder = new StringBuilder();
        string? usagePayload = null;

        try
        {
            using var totalTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            totalTimeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveTimeoutMs));
            using var firstChunkTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                totalTimeoutCts.Token
            );
            firstChunkTimeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveFirstChunkTimeoutMs));
            var result = await _geminiStreamingAdapter.StreamAsync(
                new GeminiStreamingAdapterRequest(
                    endpoint,
                    geminiApiKey,
                    body,
                    firstChunkTimeoutCts.Token,
                    totalTimeoutCts.Token,
                    () => streamedTextStarted ? totalTimeoutCts.Token : firstChunkTimeoutCts.Token,
                    ConsumeEvent
                ),
                totalTimeoutCts.Token
            );
            if (!result.IsSuccess)
            {
                Console.Error.WriteLine($"[gemini] grounded chat stream failed ({(int)result.StatusCode}): {result.FailureBody}");
                return new GeminiGroundedChatResponse(
                    $"Gemini 웹검색 요청 실패: {(int)result.StatusCode}",
                    0,
                    Math.Max(0L, stopwatch.ElapsedMilliseconds)
                );
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
                if (eventPayload.Contains("\"usageMetadata\"", StringComparison.Ordinal))
                {
                    usagePayload = eventPayload;
                }

                var chunk = ProviderResponseParser.ExtractGeminiChunk(eventPayload);
                var delta = GeminiRequestPolicy.NormalizeStreamDelta(chunk.Content, mergedBuilder.ToString());
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
                    partialContent + "\n\n" + ChatStreamingContinuation.BuildPartialTruncationSuffix("gemini", $"timeout_{timeoutKind}"),
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
                    partialContent + "\n\n" + ChatStreamingContinuation.BuildPartialTruncationSuffix("gemini", ex.Message),
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

        var selectedModel = string.IsNullOrWhiteSpace(model) ? _providers.GeminiModel : model.Trim();
        var endpoint = $"{_providers.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:generateContent";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, Math.Min(_context.ChatMaxOutputTokens, 2048));
        var effectiveTimeoutMs = ProviderTimeoutPolicy.NormalizeGeminiGroundedTimeoutMs(timeoutMs);
        var stopwatch = Stopwatch.StartNew();
        var promptForTurn = prompt;
        var mergedBuilder = new StringBuilder();
        var citationAccumulator = new CitationAccumulator();

        try
        {
            for (var turn = 0; turn < ChatContinuationRounds; turn++)
            {
                var body = GeminiRequestPolicy.BuildUrlContextBody(promptForTurn, effectiveMaxOutputTokens, includeGoogleSearch);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveTimeoutMs));
                var result = await _geminiGenerateContentAdapter.SendAsync(
                    new GeminiGenerateContentRequest(endpoint, geminiApiKey, body),
                    timeoutCts.Token
                );
                if (!result.IsSuccess)
                {
                    Console.Error.WriteLine($"[gemini] url context failed ({(int)result.StatusCode}): {result.ResponseBody}");
                    return new GeminiUrlContextChatResponse(
                        $"Gemini URL 참조 요청 실패: {(int)result.StatusCode}",
                        0,
                        Math.Max(0L, stopwatch.ElapsedMilliseconds),
                        Array.Empty<SearchCitationReference>()
                    );
                }

                CaptureGeminiUsage(result.ResponseBody);
                citationAccumulator.MergeFromGeminiPayload(result.ResponseBody);
                var chunk = ProviderResponseParser.ExtractGeminiChunk(result.ResponseBody);
                var chunkText = chunk.Content.Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    if (mergedBuilder.Length > 0)
                    {
                        mergedBuilder.AppendLine();
                    }

                    mergedBuilder.Append(chunkText);
                }

                if (!ProviderResponseParser.IsGeminiTruncated(chunk.FinishReason) || string.IsNullOrWhiteSpace(chunkText))
                {
                    break;
                }

                promptForTurn = ChatStreamingContinuation.BuildContinuationPrompt(prompt, mergedBuilder.ToString());
            }

            var content = mergedBuilder.ToString().Trim();
            return new GeminiUrlContextChatResponse(
                string.IsNullOrWhiteSpace(content) ? "Gemini URL 참조 응답이 비어 있습니다." : content,
                0,
                Math.Max(0L, stopwatch.ElapsedMilliseconds),
                citationAccumulator.ToArray()
            );
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

        var selectedModel = string.IsNullOrWhiteSpace(model) ? _providers.GeminiModel : model.Trim();
        var endpoint = $"{_providers.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:streamGenerateContent?alt=sse";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, Math.Min(_context.ChatMaxOutputTokens, 2048));
        var effectiveTimeoutMs = ProviderTimeoutPolicy.NormalizeGeminiGroundedTimeoutMs(timeoutMs);
        var effectiveFirstChunkTimeoutMs = ProviderTimeoutPolicy.NormalizeGeminiUrlContextFirstChunkTimeoutMs(effectiveTimeoutMs);
        var body = GeminiRequestPolicy.BuildUrlContextBody(prompt, effectiveMaxOutputTokens, includeGoogleSearch);
        var stopwatch = Stopwatch.StartNew();
        var firstChunkMs = 0L;
        var streamedTextStarted = false;
        var mergedBuilder = new StringBuilder();
        var citationAccumulator = new CitationAccumulator();
        string? usagePayload = null;

        try
        {
            using var totalTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            totalTimeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveTimeoutMs));
            using var firstChunkTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                totalTimeoutCts.Token
            );
            firstChunkTimeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveFirstChunkTimeoutMs));
            var result = await _geminiStreamingAdapter.StreamAsync(
                new GeminiStreamingAdapterRequest(
                    endpoint,
                    geminiApiKey,
                    body,
                    firstChunkTimeoutCts.Token,
                    totalTimeoutCts.Token,
                    () => streamedTextStarted ? totalTimeoutCts.Token : firstChunkTimeoutCts.Token,
                    ConsumeEvent
                ),
                totalTimeoutCts.Token
            );
            if (!result.IsSuccess)
            {
                Console.Error.WriteLine($"[gemini] url context stream failed ({(int)result.StatusCode}): {result.FailureBody}");
                return new GeminiUrlContextChatResponse(
                    $"Gemini URL 참조 요청 실패: {(int)result.StatusCode}",
                    0,
                    Math.Max(0L, stopwatch.ElapsedMilliseconds),
                    Array.Empty<SearchCitationReference>()
                );
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
                citationAccumulator.ToArray()
            );

            void ConsumeEvent(string eventPayload)
            {
                if (eventPayload.Contains("\"usageMetadata\"", StringComparison.Ordinal))
                {
                    usagePayload = eventPayload;
                }

                citationAccumulator.MergeFromGeminiPayload(eventPayload);

                var chunk = ProviderResponseParser.ExtractGeminiChunk(eventPayload);
                var delta = GeminiRequestPolicy.NormalizeStreamDelta(chunk.Content, mergedBuilder.ToString());
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
                    partialContent + "\n\n" + ChatStreamingContinuation.BuildPartialTruncationSuffix("gemini", $"timeout_{timeoutKind}"),
                    firstChunkMs,
                    Math.Max(0L, stopwatch.ElapsedMilliseconds),
                    citationAccumulator.ToArray()
                );
            }

            return new GeminiUrlContextChatResponse(
                "Gemini URL 참조 응답 시간이 초과되었습니다.",
                firstChunkMs,
                Math.Max(0L, stopwatch.ElapsedMilliseconds),
                citationAccumulator.ToArray()
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gemini] url context stream error: {ex.Message}");
            var partialContent = mergedBuilder.ToString().Trim();
            if (streamedTextStarted && !string.IsNullOrWhiteSpace(partialContent))
            {
                return new GeminiUrlContextChatResponse(
                    partialContent + "\n\n" + ChatStreamingContinuation.BuildPartialTruncationSuffix("gemini", ex.Message),
                    firstChunkMs,
                    Math.Max(0L, stopwatch.ElapsedMilliseconds),
                    citationAccumulator.ToArray()
                );
            }

            return new GeminiUrlContextChatResponse(
                $"Gemini URL 참조 호출 오류: {ex.Message}",
                0,
                Math.Max(0L, stopwatch.ElapsedMilliseconds),
                citationAccumulator.ToArray()
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

        var selectedModel = string.IsNullOrWhiteSpace(modelOverride) ? _providers.GeminiModel : modelOverride.Trim();
        var endpoint = $"{_providers.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:generateContent";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _context.ChatMaxOutputTokens);
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

            var result = await _geminiGenerateContentAdapter.SendAsync(
                new GeminiGenerateContentRequest(endpoint, geminiApiKey, body),
                cancellationToken
            );
            if (!result.IsSuccess)
            {
                Console.Error.WriteLine($"[gemini] multimodal chat failed ({(int)result.StatusCode}): {result.ResponseBody}");
                return $"Gemini 요청 실패: {(int)result.StatusCode}";
            }

            CaptureGeminiUsage(result.ResponseBody);
            var text = ProviderResponseParser.ExtractGeminiChunk(result.ResponseBody).Content.Trim();
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

        var selectedModel = string.IsNullOrWhiteSpace(modelOverride) ? _providers.CerebrasModel : modelOverride.Trim();
        var effectiveModel = selectedModel;
        var fallbackRetried = false;
        var endpoint = $"{_providers.CerebrasBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Respond in Korean with concise and practical answers. "
            + "Answer only the latest user request. Do not switch to news, search summaries, 3D printing, or other unrelated topics unless the user explicitly asks for them. "
            + "If conversation history is provided above, treat the user's short follow-up messages (e.g., '그니까 잘 돌아가?', '이 환경에서?', '24GB 구성임') as continuations of the prior turns. Never reply '이전 대화 맥락이 제공되지 않아' when [최근 대화] is in the prompt — use it. "
            + "When the user asks for your judgment or opinion (어때, 어떻게 생각해, 잘 돌아갈까, 검토해봐, 추천해, 너 생각, 네 의견 등), give a concrete, decisive answer based on the conversation history and your knowledge. Do not deflect with generic disclaimers like '정확도/정밀도/재현율/F1-score 같은 객관적 지표가 필요하다' or '구체적인 평가 지표와 데이터셋이 필요하다'. State your best assessment with a 1-2 sentence rationale, then optionally note any uncertainty. "
            + "Do not repeat the same paragraphs across multiple turns. If the user asks the same thing again, dig deeper or take a clearer stance instead of repeating prior wording.";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _context.ChatMaxOutputTokens);
        var promptForTurn = userInput;
        var mergedBuilder = new StringBuilder();

        try
        {
            for (var turn = 0; turn < ChatContinuationRounds; turn++)
            {
                var result = await _openAiCompatibleChatAdapter.SendAsync(
                    new ProviderChatAdapterRequest(
                        "cerebras",
                        endpoint,
                        cerebrasApiKey,
                        effectiveModel,
                        systemPrompt,
                        promptForTurn,
                        null,
                        "max_completion_tokens",
                        effectiveMaxOutputTokens
                    ),
                    cancellationToken
                );
                if (!result.IsSuccess)
                {
                    if (!fallbackRetried
                        && result.StatusCode == System.Net.HttpStatusCode.NotFound
                        && CerebrasErrorPolicy.IsModelNotFound(result.ResponseBody))
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

                        Console.Error.WriteLine($"[cerebras] chat failed ({(int)result.StatusCode}): {result.ResponseBody} | catalog로도 가용 모델을 찾지 못했습니다. API 키 권한을 확인하세요.");
                        return $"Cerebras 키가 어떤 모델에도 접근 권한이 없습니다. https://cloud.cerebras.ai 에서 키 권한과 사용 가능한 모델 목록을 확인하세요. (요청한 모델: {effectiveModel})";
                    }

                    return CerebrasErrorPolicy.BuildHttpFailureText(result.StatusCode, effectiveModel);
                }

                CaptureOpenAiCompatibleTokenUsage(result.ResponseBody);
                var chunk = ProviderResponseParser.ExtractOpenAiCompatibleChunk(result.ResponseBody);
                var chunkText = chunk.Content.Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    if (mergedBuilder.Length > 0)
                    {
                        mergedBuilder.AppendLine();
                    }

                    mergedBuilder.Append(chunkText);
                }

                if (!ProviderResponseParser.IsOpenAiCompatibleTruncated(chunk.FinishReason) || string.IsNullOrWhiteSpace(chunkText))
                {
                    break;
                }

                promptForTurn = ChatStreamingContinuation.BuildContinuationPrompt(userInput, mergedBuilder.ToString());
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

        var initialModel = string.IsNullOrWhiteSpace(modelOverride) ? _providers.CerebrasModel : modelOverride.Trim();
        if (string.IsNullOrWhiteSpace(modelOverride))
        {
            var cachedModel = _cerebrasModelCatalog.GetCachedResolvedModel();
            if (!string.IsNullOrWhiteSpace(cachedModel))
            {
                initialModel = cachedModel;
            }
        }
        var endpoint = $"{_providers.CerebrasBaseUrl.TrimEnd('/')}/chat/completions";
        var systemPrompt = "You are Omni-node assistant. Respond in Korean with concise and practical answers. "
            + "Answer only the latest user request. Do not switch to news, search summaries, 3D printing, or other unrelated topics unless the user explicitly asks for them. "
            + "If conversation history is provided above, treat the user's short follow-up messages (e.g., '그니까 잘 돌아가?', '이 환경에서?', '24GB 구성임') as continuations of the prior turns. Never reply '이전 대화 맥락이 제공되지 않아' when [최근 대화] is in the prompt — use it. "
            + "When the user asks for your judgment or opinion (어때, 어떻게 생각해, 잘 돌아갈까, 검토해봐, 추천해, 너 생각, 네 의견 등), give a concrete, decisive answer based on the conversation history and your knowledge. Do not deflect with generic disclaimers like '정확도/정밀도/재현율/F1-score 같은 객관적 지표가 필요하다' or '구체적인 평가 지표와 데이터셋이 필요하다'. State your best assessment with a 1-2 sentence rationale, then optionally note any uncertainty. "
            + "Do not repeat the same paragraphs across multiple turns. If the user asks the same thing again, dig deeper or take a clearer stance instead of repeating prior wording.";
        var effectiveMaxOutputTokens = NormalizeMaxOutputTokens(maxOutputTokens, _context.ChatMaxOutputTokens);

        var firstResult = await GenerateOpenAiCompatibleChatStreamingAsync(
            "cerebras",
            endpoint,
            cerebrasApiKey,
            initialModel,
            systemPrompt,
            userInput,
            null,
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
                    null,
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

        var model = string.IsNullOrWhiteSpace(modelOverride) ? _providers.NvidiaModel : modelOverride.Trim();
        var endpoint = $"{_providers.NvidiaBaseUrl.TrimEnd('/')}/chat/completions";
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
                var result = await _openAiCompatibleChatAdapter.SendAsync(
                    new ProviderChatAdapterRequest(
                        "nvidia",
                        endpoint,
                        nvidiaApiKey,
                        model,
                        systemPrompt,
                        promptForTurn,
                        null,
                        "max_tokens",
                        effectiveMaxOutputTokens,
                        AcceptedResponseResolver: (acceptedBody, token) => PollNvidiaStatusAsync(nvidiaApiKey, acceptedBody, token)
                    ),
                    cancellationToken
                );
                if (!result.IsSuccess)
                {
                    return result.FailureMessage;
                }

                CaptureOpenAiCompatibleTokenUsage(result.ResponseBody);
                var chunk = ProviderResponseParser.ExtractOpenAiCompatibleChunk(result.ResponseBody);
                var chunkText = chunk.Content.Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    if (mergedBuilder.Length > 0)
                    {
                        mergedBuilder.AppendLine();
                    }

                    mergedBuilder.Append(chunkText);
                }

                if (!ProviderResponseParser.IsOpenAiCompatibleTruncated(chunk.FinishReason) || string.IsNullOrWhiteSpace(chunkText))
                {
                    break;
                }

                promptForTurn = ChatStreamingContinuation.BuildContinuationPrompt(userInput, mergedBuilder.ToString());
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

        var model = string.IsNullOrWhiteSpace(modelOverride) ? _providers.NvidiaModel : modelOverride.Trim();
        var endpoint = $"{_providers.NvidiaBaseUrl.TrimEnd('/')}/chat/completions";
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
            null,
            "max_tokens",
            NormalizeNvidiaMaxOutputTokens(maxOutputTokens),
            deltaCallback,
            cancellationToken
        );
    }

    private async Task<string?> ResolveAvailableCerebrasModelAsync(string apiKey, CancellationToken cancellationToken)
    {
        return await _cerebrasModelCatalog.ResolveFirstAvailableModelAsync(apiKey, cancellationToken);
    }

    private int NormalizeNvidiaMaxOutputTokens(int requested)
    {
        return Math.Clamp(NormalizeMaxOutputTokens(requested, Math.Min(_context.ChatMaxOutputTokens, 4096)), 256, 4096);
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
        if (_ownsCerebrasModelCatalog)
        {
            _cerebrasModelCatalog.Dispose();
        }
    }

    private void CaptureGroqUsage(string model, string responseBody)
    {
        if (!ProviderResponseParser.TryParseOpenAiCompatibleUsage(responseBody, out var parsed))
        {
            return;
        }

        CaptureResponseTokenUsage(new TokenUsage(parsed.PromptTokens, parsed.CompletionTokens, parsed.TotalTokens, TokenUsageEstimator.SourceExact));

        lock (_groqLock)
        {
            if (!_groqUsageByModel.TryGetValue(model, out var usage))
            {
                usage = new GroqUsage();
                _groqUsageByModel[model] = usage;
            }

            usage.Requests += 1;
            usage.PromptTokens += parsed.PromptTokens;
            usage.CompletionTokens += parsed.CompletionTokens;
            usage.TotalTokens += parsed.TotalTokens;
            usage.LastUpdatedUtc = DateTimeOffset.UtcNow;
        }

        SaveUsageState();
    }

    private void CaptureGroqRateLimitHeaders(string model, HttpResponseHeaders headers)
    {
        var snapshot = GroqRateLimitHeaderParser.Parse(headers, DateTimeOffset.UtcNow);

        lock (_groqLock)
        {
            _groqRateByModel[model] = snapshot;
        }

        SaveUsageState();
    }

    private void CaptureGeminiUsage(string responseBody)
    {
        if (!ProviderResponseParser.TryParseGeminiUsage(responseBody, out var parsed))
        {
            return;
        }

        CaptureResponseTokenUsage(new TokenUsage(parsed.PromptTokens, parsed.CompletionTokens, parsed.TotalTokens, TokenUsageEstimator.SourceExact));

        var addedCost = ((decimal)parsed.PromptTokens * _providers.GeminiInputPricePerMillionUsd / 1_000_000m)
                        + ((decimal)parsed.CompletionTokens * _providers.GeminiOutputPricePerMillionUsd / 1_000_000m);
        lock (_geminiLock)
        {
            _geminiUsage.Requests += 1;
            _geminiUsage.PromptTokens += parsed.PromptTokens;
            _geminiUsage.CompletionTokens += parsed.CompletionTokens;
            _geminiUsage.TotalTokens += parsed.TotalTokens;
            _geminiUsage.EstimatedCostUsd += addedCost;
            _geminiUsage.LastUpdatedUtc = DateTimeOffset.UtcNow;
        }

        SaveUsageState();
    }

    private void CaptureOpenAiCompatibleTokenUsage(string responseBody)
    {
        if (!ProviderResponseParser.TryParseOpenAiCompatibleUsage(responseBody, out var parsed))
        {
            return;
        }

        CaptureResponseTokenUsage(new TokenUsage(parsed.PromptTokens, parsed.CompletionTokens, parsed.TotalTokens, TokenUsageEstimator.SourceExact));
    }

    private void CaptureResponseTokenUsage(TokenUsage usage)
    {
        var normalized = TokenUsageEstimator.Normalize(usage, TokenUsageEstimator.SourceExact);
        _responseTokenUsage.Value = TokenUsageEstimator.Combine(_responseTokenUsage.Value, normalized);
    }

    private void LoadUsageState()
    {
        try
        {
            var state = UsageStatePersistence.LoadLlmUsageState(_usageStatePath);
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

    private async Task<string> GenerateOpenAiCompatibleChatStreamingAsync(
        string provider,
        string endpoint,
        string apiKey,
        string model,
        string systemPrompt,
        string userInput,
        List<(string Role, string Content)>? multiTurn,
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
                var turnBuilder = new StringBuilder();
                var result = await _openAiCompatibleStreamingChatAdapter.SendTurnAsync(
                    new ProviderStreamingAdapterRequest(
                        provider,
                        endpoint,
                        apiKey,
                        model,
                        systemPrompt,
                        promptForTurn,
                        multiTurn,
                        maxTokensProperty,
                        maxOutputTokens,
                        AcceptedResponseResolver: provider.Equals("nvidia", StringComparison.OrdinalIgnoreCase)
                            ? (acceptedBody, token) => PollNvidiaStatusAsync(apiKey, acceptedBody, token)
                            : null,
                        OnResponseHeaders: provider.Equals("groq", StringComparison.OrdinalIgnoreCase)
                            ? headers => CaptureGroqRateLimitHeaders(model, headers)
                            : null,
                        OnRawPayload: CaptureOpenAiCompatibleTokenUsage,
                        OnDelta: delta =>
                        {
                            mergedBuilder.Append(delta);
                            turnBuilder.Append(delta);
                            ChatStreamingContinuation.SafeEmitDelta(deltaCallback, delta);
                        }
                    ),
                    cancellationToken
                );
                if (!result.IsSuccess)
                {
                    return result.FailureMessage;
                }

                if (!string.IsNullOrWhiteSpace(result.AcceptedResponseBody))
                {
                    CaptureOpenAiCompatibleTokenUsage(result.AcceptedResponseBody);
                    var polledChunk = ProviderResponseParser.ExtractOpenAiCompatibleChunk(result.AcceptedResponseBody);
                    var finalContent = polledChunk.Content.Trim();
                    if (!string.IsNullOrWhiteSpace(finalContent))
                    {
                        ChatStreamingContinuation.AppendChunk(mergedBuilder, finalContent);
                        ChatStreamingContinuation.SafeEmitDelta(deltaCallback, finalContent);
                    }

                    if (!ProviderResponseParser.IsOpenAiCompatibleTruncated(polledChunk.FinishReason)
                        || string.IsNullOrWhiteSpace(finalContent))
                    {
                        break;
                    }

                    promptForTurn = ChatStreamingContinuation.BuildContinuationPrompt(userInput, mergedBuilder.ToString());
                    continue;
                }

                if (!ProviderResponseParser.IsOpenAiCompatibleTruncated(result.FinishReason) || turnBuilder.Length == 0)
                {
                    break;
                }

                promptForTurn = ChatStreamingContinuation.BuildContinuationPrompt(userInput, mergedBuilder.ToString());
            }

            var content = mergedBuilder.ToString().Trim();
            return string.IsNullOrWhiteSpace(content)
                ? $"{OpenAiCompatibleProtocol.DisplayName(provider)} 응답이 비어 있습니다."
                : content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine($"[{provider}] chat stream timeout (model={model})");
            var partial = mergedBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(partial))
            {
                return ChatStreamingContinuation.BuildTimeoutMessage(provider);
            }
            return partial + "\n\n" + ChatStreamingContinuation.BuildPartialTruncationSuffix(provider, "timeout");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{provider}] chat stream error: {ex.Message}");
            var partial = mergedBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(partial))
            {
                return $"{OpenAiCompatibleProtocol.DisplayName(provider)} 호출 오류: {ex.Message}";
            }
            return partial + "\n\n" + ChatStreamingContinuation.BuildPartialTruncationSuffix(provider, ex.Message);
        }
    }

    // 부분 스트림이 끊겼을 때 사용자가 즉시 인식할 수 있도록 한 줄 안내를 본문 끝에 덧붙인다.
    private async Task<string> PollNvidiaStatusAsync(
        string apiKey,
        string acceptedBody,
        CancellationToken cancellationToken
    )
    {
        return await _nvidiaStatusPollingAdapter.PollAsync(
            apiKey,
            acceptedBody,
            _providers.NvidiaBaseUrl,
            _providers.NvidiaTimeoutSec,
            cancellationToken
        );
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
