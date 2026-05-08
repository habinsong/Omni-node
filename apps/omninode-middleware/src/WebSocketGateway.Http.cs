using System.Globalization;
using System.Net;
using System.Text;

namespace OmniNode.Middleware;

public sealed partial class WebSocketGateway
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var rebindTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _requestListenerRebind = () => rebindTcs.TrySetResult();
            var listenerPrefix = ResolveListenerPrefix();
            SetGatewayHealthState(
                status: "starting",
                listenerPrefix: listenerPrefix,
                listenerBound: false,
                degradedMode: false
            );

            using var listener = new HttpListener();
            listener.Prefixes.Add(listenerPrefix);
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                Console.Error.WriteLine(
                    $"[web] listener start failed (prefix={listenerPrefix}, error={ex.ErrorCode}): {ex.Message}"
                );
                Console.Error.WriteLine("[web] degraded mode enabled: websocket/dashboard listener unavailable");
                SetGatewayHealthState(
                    status: "degraded",
                    listenerPrefix: listenerPrefix,
                    listenerBound: false,
                    degradedMode: true,
                    listenerErrorCode: ex.ErrorCode,
                    listenerErrorMessage: ex.Message
                );
                await Task.WhenAny(WaitForCancellationAsync(cancellationToken), rebindTcs.Task);
                SetGatewayHealthState(
                    status: "stopped",
                    listenerPrefix: listenerPrefix,
                    listenerBound: false,
                    degradedMode: true,
                    listenerErrorCode: ex.ErrorCode,
                    listenerErrorMessage: ex.Message
                );
                continue;
            }

            var hostForLog = _settingsService.GetSettingsSnapshot().ExternalDashboardEnabled ? "0.0.0.0" : "127.0.0.1";
            Console.WriteLine($"[web] dashboard=http://{hostForLog}:{_port}/ ws=ws://{hostForLog}:{_port}/ws/");
            SetGatewayHealthState(
                status: "ok",
                listenerPrefix: listenerPrefix,
                listenerBound: true,
                degradedMode: false
            );

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var contextTask = listener.GetContextAsync();
                    var cancellableContextTask = contextTask.WaitAsync(cancellationToken);
                    var completed = await Task.WhenAny(cancellableContextTask, rebindTcs.Task);
                    if (completed == rebindTcs.Task)
                    {
                        break;
                    }

                    HttpListenerContext context;
                    try
                    {
                        context = await cancellableContextTask;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (HttpListenerException)
                    {
                        break;
                    }

                    _ = HandleContextAsync(context, cancellationToken);
                }
            }
            finally
            {
                if (listener.IsListening)
                {
                    listener.Stop();
                }

                SetGatewayHealthState(
                    status: "stopped",
                    listenerPrefix: listenerPrefix,
                    listenerBound: false,
                    degradedMode: false
                );
            }
        }
    }

    private string ResolveListenerPrefix()
    {
        var host = _settingsService.GetSettingsSnapshot().ExternalDashboardEnabled ? "+" : "127.0.0.1";
        return $"http://{host}:{_port}/";
    }

    private static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 종료 시그널 수신 시 정상 종료 경로로 복귀한다.
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";

        if (context.Request.IsWebSocketRequest && (path == "/ws" || path == "/ws/"))
        {
            await HandleWebSocketAsync(context, cancellationToken);
            return;
        }

        if (_config.EnableHealthEndpoint && TryResolveProbeStatus(path, out var probeStatus))
        {
            var method = context.Request.HttpMethod?.ToUpperInvariant() ?? "GET";
            if (method != "GET" && method != "HEAD")
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                context.Response.Headers["Allow"] = "GET, HEAD";
                await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Method Not Allowed", cancellationToken);
                return;
            }

            var probeOk = probeStatus != "ready" || IsReadyProbeSatisfied();
            context.Response.StatusCode = probeOk
                ? (int)HttpStatusCode.OK
                : (int)HttpStatusCode.ServiceUnavailable;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            if (method == "HEAD")
            {
                context.Response.ContentLength64 = 0;
                context.Response.OutputStream.Close();
                return;
            }

            await WriteResponseAsync(
                context.Response,
                "application/json; charset=utf-8",
                BuildProbeResponseJson(probeStatus, probeOk),
                cancellationToken
            );
            return;
        }

        if (await _apiEndpoint.TryHandleAsync(context, path, cancellationToken))
        {
            return;
        }

        if (await _staticFileEndpoint.TryHandleAsync(context, path, cancellationToken))
        {
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "not found", cancellationToken);
    }

    private static async Task WriteResponseAsync(
        HttpListenerResponse response,
        string contentType,
        string body,
        CancellationToken cancellationToken
    )
    {
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.OutputStream.Close();
    }

    private static async Task WriteBinaryResponseAsync(
        HttpListenerResponse response,
        string contentType,
        byte[] body,
        CancellationToken cancellationToken
    )
    {
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body, cancellationToken);
        response.OutputStream.Close();
    }
}
