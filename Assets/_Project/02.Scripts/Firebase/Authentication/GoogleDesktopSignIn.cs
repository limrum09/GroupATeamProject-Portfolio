using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 데스크톱 브라우저를 이용한 Google 인증과 ID 토큰 발급
/// </summary>
public class GoogleDesktopSignIn
{
    const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    readonly string _clientId;
    readonly string _clientSecret;

    int _isSigningIn;

    static string EncodeBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    static string CreateRandomValue()
    {
        byte[] bytes = new byte[32];

        using RandomNumberGenerator random = RandomNumberGenerator.Create();

        random.GetBytes(bytes);
        return EncodeBase64Url(bytes);
    }

    static string CreateCodeChallenge(string codeVerifier)
    {
        using SHA256 sha256 = SHA256.Create();

        byte[] bytes = Encoding.ASCII.GetBytes(codeVerifier);
        return EncodeBase64Url(sha256.ComputeHash(bytes));
    }

    string CreateAuthorizationUrl(string redirectUri, string state, string codeChallenge)
    {
        return AuthorizationEndpoint
            + "?client_id=" + Uri.EscapeDataString(_clientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
            + "&response_type=code"
            + "&scope=openid%20email%20profile"
            + "&state=" + state
            + "&code_challenge=" + codeChallenge
            + "&code_challenge_method=S256"
            + "&prompt=select_account";
    }

    static Dictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string part in query.TrimStart('?').Split('&'))
        {
            if (string.IsNullOrEmpty(part))
                continue;

            int separator = part.IndexOf('=');

            string key = WebUtility.UrlDecode(separator < 0 ? part : part.Substring(0, separator));
            string value = separator < 0 ? string.Empty: WebUtility.UrlDecode(part.Substring(separator + 1));

            // 중복 파라미터를 포함한 콜백 거부
            if (values.ContainsKey(key))
                return null;

            values.Add(key, value);
        }

        return values;
    }

    static async Task<Uri> ReadRequestUriAsync(NetworkStream stream, Uri redirectUri, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8192];
        int received = 0;

        while (received < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer, received, buffer.Length - received, cancellationToken).ConfigureAwait(false);

            if (read == 0)
                return null;

            received += read;

            string headers = Encoding.ASCII.GetString(buffer, 0, received);

            if (!headers.Contains("\r\n\r\n"))
                continue;

            int lineEnd = headers.IndexOf("\r\n", StringComparison.Ordinal);

            string[] requestLine = headers.Substring(0, lineEnd).Split(' ');

            if (requestLine.Length != 3 || requestLine[0] != "GET")
                return null;

            if (!requestLine[1].StartsWith("/", StringComparison.Ordinal))
                return null;

            if (!Uri.TryCreate(redirectUri, requestLine[1], out Uri requestUri))
                return null;

            if (requestUri.Scheme != Uri.UriSchemeHttp || requestUri.Host != "127.0.0.1" || requestUri.Port != redirectUri.Port || requestUri.AbsolutePath != "/")
                return null;

            return requestUri;
        }

        // 허용 크기를 초과한 HTTP 요청 거부
        return null;
    }

    static async Task SendBrowserResponseAsync(NetworkStream stream, bool accepted, CancellationToken cancellationToken)
    {
        string message = accepted ? "인증 응답을 받았습니다. 이 창을 닫고 게임으로 돌아가세요." : "유효하지 않은 요청입니다.";

        byte[] body = Encoding.UTF8.GetBytes("<!doctype html><html><head>" + "<meta charset=\"utf-8\"><title>Google 로그인</title>" + "</head><body><p>" + message + "</p></body></html>");

        string status = accepted ? "200 OK" : "400 Bad Request";

        byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\n" + "Content-Type: text/html; charset=utf-8\r\n" + "Cache-Control: no-store\r\n" + "Referrer-Policy: no-referrer\r\n" + "Content-Security-Policy: default-src 'none'\r\n" + $"Content-Length: {body.Length}\r\n" + "Connection: close\r\n\r\n");

        try
        {
            await stream.WriteAsync(headers, 0, headers.Length, cancellationToken).ConfigureAwait(false);

            await stream.WriteAsync(body, 0, body.Length, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException || exception is SocketException || exception is ObjectDisposedException || (exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // 브라우저 종료로 안내 페이지 전송에 실패해도 수신된 인증 결과 유지
        }
    }

    static async Task<string> ReceiveCodeAsync(TcpListener listener, Uri redirectUri, string expectedState, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            using NetworkStream stream = client.GetStream();
            using CancellationTokenSource requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 불완전한 연결 하나가 전체 로그인 대기를 점유하지 않도록 제한
            requestCancellation.CancelAfter(TimeSpan.FromSeconds(5));

            using CancellationTokenRegistration closeConnection = requestCancellation.Token.Register(client.Close);

            Uri requestUri;

            try
            {
                requestUri = await ReadRequestUriAsync(stream, redirectUri, requestCancellation.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException || exception is SocketException || exception is ObjectDisposedException || exception is OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 종료되거나 시간 초과된 연결을 정리하고 다음 요청 대기
                continue;
            }

            Dictionary<string, string> query = requestUri == null ? null : ParseQuery(requestUri.Query);

            if (query == null || !query.TryGetValue("state", out string returnedState) || !string.Equals(returnedState, expectedState, StringComparison.Ordinal))
            {
                await SendBrowserResponseAsync(stream, false, requestCancellation.Token).ConfigureAwait(false);

                continue;
            }

            bool hasError = query.TryGetValue("error", out string error);
            bool hasCode = query.TryGetValue("code", out string code) && !string.IsNullOrWhiteSpace(code);

            if (hasError == hasCode)
            {
                await SendBrowserResponseAsync(stream, false, requestCancellation.Token).ConfigureAwait(false);

                continue;
            }

            await SendBrowserResponseAsync(stream, true, requestCancellation.Token).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (hasError)
            {
                if (error == "access_denied")
                    throw new OperationCanceledException("Google 로그인 취소");

                throw new InvalidOperationException("Google 계정 인증 실패");
            }

            return code;
        }
    }

    async Task<string> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        Dictionary<string, string> values = new Dictionary<string, string>
            {
                { "client_id", _clientId },
                { "code", code },
                { "redirect_uri", redirectUri },
                { "code_verifier", codeVerifier },
                { "grant_type", "authorization_code" }
            };

        if (!string.IsNullOrWhiteSpace(_clientSecret))
            values.Add("client_secret", _clientSecret);

        using HttpClientHandler handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };

        using HttpClient client = new HttpClient(handler);
        // 전체 로그인 작업의 취소와 시간 제한 사용
        client.Timeout = Timeout.InfiniteTimeSpan;

        using FormUrlEncodedContent content = new FormUrlEncodedContent(values);
        using HttpResponseMessage response = await client.PostAsync(TokenEndpoint, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string errorJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            string errorCode = "unknown_error";
            string description = "오류 설명 없음";

            try
            {
                JObject errorResponse = JObject.Parse(errorJson);

                errorCode = errorResponse.Value<string>("error") ?? errorCode;

                description = errorResponse.Value<string>("error_description") ?? description;
            }
            catch (JsonException)
            {
                description = "Google 오류 응답이 JSON 형식이 아님";
            }

            throw new InvalidOperationException($"Google 토큰 교환 실패: HTTP {(int)response.StatusCode}" + $" / {errorCode} / {description}");
        }

        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        JObject result;

        try
        {
            result = JObject.Parse(json);
        }
        catch (JsonException)
        {
            // 인증 토큰이 포함될 수 있는 원문 응답 노출 방지
            throw new InvalidOperationException("Google 토큰 응답 형식 오류");
        }

        JToken idToken = result["id_token"];

        if (idToken == null || idToken.Type != JTokenType.String || string.IsNullOrWhiteSpace(idToken.Value<string>()))
            throw new InvalidOperationException("Google ID 토큰 누락");

        return idToken.Value<string>();
    }

    /// <summary>
    /// 데스크톱 OAuth 클라이언트 정보로 로그인 처리기 구성
    /// </summary>
    /// <param name="clientId">Google 데스크톱 OAuth 클라이언트 ID</param>
    /// <param name="clientSecret">해당 클라이언트의 보안 비밀 값, 사용하지 않으면 null</param>
    public GoogleDesktopSignIn(string clientId, string clientSecret = null)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Google 클라이언트 ID 필요", nameof(clientId));

        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    /// <summary>
    /// Unity 메인 스레드에서 브라우저 로그인 시작과 Google ID 토큰 반환
    /// </summary>
    /// <param name="cancellationToken">사용자 취소 또는 소유자 종료를 전달할 토큰</param>
    /// <returns>Firebase 인증에 전달할 Google ID 토큰</returns>
    public async Task<string> SignInAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref _isSigningIn, 1, 0) != 0)
            throw new InvalidOperationException("이미 Google 로그인 진행 중");

        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            timeout.CancelAfter(TimeSpan.FromMinutes(2));

            try
            {
                // 운영체제가 선택한 빈 포트를 로그인 완료까지 유지
                listener.Start();

                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string redirectUri = $"http://127.0.0.1:{port}/";

                string state = CreateRandomValue();
                string codeVerifier = CreateRandomValue();
                string codeChallenge = CreateCodeChallenge(codeVerifier);

                using CancellationTokenRegistration stopListener = timeout.Token.Register(listener.Stop);

                timeout.Token.ThrowIfCancellationRequested();

                Application.OpenURL(CreateAuthorizationUrl(redirectUri, state, codeChallenge));

                string code = await ReceiveCodeAsync(listener, new Uri(redirectUri), state, timeout.Token).ConfigureAwait(false);

                return await ExchangeCodeAsync(code, redirectUri, codeVerifier, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (timeout.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException("Google 로그인 취소", exception, cancellationToken);

                throw new TimeoutException("Google 로그인 제한 시간 초과", exception);
            }
        }
        finally
        {
            listener.Stop();
            Interlocked.Exchange(ref _isSigningIn, 0);
        }
    }
}