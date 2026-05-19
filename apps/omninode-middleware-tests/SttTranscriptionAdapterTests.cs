using System.Net;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class SttTranscriptionAdapterTests
{
    [Fact]
    public async Task TranscribeAsyncPostsMultipartRequestAndParsesText()
    {
        HttpMethod? capturedMethod = null;
        string? capturedUri = null;
        string? capturedAuthScheme = null;
        string? capturedAuthParameter = null;
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedMethod = request.Method;
            capturedUri = request.RequestUri!.ToString();
            capturedAuthScheme = request.Headers.Authorization!.Scheme;
            capturedAuthParameter = request.Headers.Authorization.Parameter;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"text":"전사 결과"}""")
            };
        });
        var adapter = new SttTranscriptionAdapter(new HttpClient(handler));

        var result = await adapter.TranscribeAsync(
            new InputAttachment("voice.ogg", "audio/ogg", Convert.ToBase64String(new byte[] { 1, 2, 3 })),
            "https://stt.example.com/v1",
            "whisper-large",
            "secret-key",
            CancellationToken.None
        );

        Assert.Equal("전사 결과", result);
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("https://stt.example.com/v1/audio/transcriptions", capturedUri);
        Assert.Equal("Bearer", capturedAuthScheme);
        Assert.Equal("secret-key", capturedAuthParameter);
        Assert.Contains("whisper-large", capturedBody);
        Assert.Contains("voice.ogg", capturedBody);
    }

    [Fact]
    public void ResolveEndpointKeepsFullTranscriptionEndpoint()
    {
        Assert.Equal(
            "https://stt.example.com/audio/transcriptions",
            SttTranscriptionAdapter.ResolveEndpoint("https://stt.example.com/audio/transcriptions")
        );
    }

    [Fact]
    public async Task TranscribeAsyncRejectsInvalidBase64BeforeHttpCall()
    {
        var calls = 0;
        var adapter = new SttTranscriptionAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
        {
            calls += 1;
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));

        var result = await adapter.TranscribeAsync(
            new InputAttachment("voice.ogg", "audio/ogg", "not-base64"),
            "https://stt.example.com",
            "model",
            "key",
            CancellationToken.None
        );

        Assert.Equal("음성 파일을 읽을 수 없습니다.", result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task TranscribeAsyncMapsHttpFailure()
    {
        var adapter = new SttTranscriptionAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":"rate_limited"}""")
            })));

        var result = await adapter.TranscribeAsync(
            new InputAttachment("voice.ogg", "audio/ogg", Convert.ToBase64String(new byte[] { 9 })),
            "https://stt.example.com",
            "model",
            "key",
            CancellationToken.None
        );

        Assert.Equal("음성 변환 실패: 429", result);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(_handler(request));
        }
    }
}
