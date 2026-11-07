using System.Net;

namespace Inlabs.Download;

/// <summary>
/// Cliente HTTP do INLABS: login por formulário, sessão por cookie e download por streaming.
/// Uma instância atende uma execução (um login, downloads sequenciais).
/// </summary>
internal sealed class InlabsClient : IDisposable
{
    public const string SessionCookieName = "inlabs_session_cookie";
    public const string OriginHeaderName = "origem";
    public const string OriginHeaderValue = "736372697074";
    public const int MaxRedirects = 10;

    public static readonly Uri ProductionBaseUri = new("https://inlabs.in.gov.br/");
    public static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(300);

    private const string LoginAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
    private const int BufferSize = 81920;

    private readonly HttpClient _http;
    private readonly CookieContainer _cookies;
    private readonly Uri _baseUri;
    private readonly TimeSpan _operationTimeout;

    /// <param name="handler">Transporte HTTP (substituível em testes).</param>
    /// <param name="cookies">Container usado pelo handler para guardar a sessão desta execução.</param>
    public InlabsClient(HttpMessageHandler handler, CookieContainer cookies, Uri? baseUri = null, TimeSpan? operationTimeout = null)
    {
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _cookies = cookies;
        _baseUri = baseUri ?? ProductionBaseUri;
        _operationTimeout = operationTimeout ?? DefaultOperationTimeout;
    }

    /// <summary>Cria o cliente com <see cref="HttpClientHandler"/> real, cookies em memória e validação TLS normal.</summary>
    public static InlabsClient CreateDefault(Uri? baseUri = null, TimeSpan? operationTimeout = null)
    {
        var cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookies,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = MaxRedirects,
            AutomaticDecompression = DecompressionMethods.All,
        };
        return new InlabsClient(handler, cookies, baseUri, operationTimeout);
    }

    public void Dispose() => _http.Dispose();

    public async Task<DownloadResult> LoginAsync(string email, string password, CancellationToken ct)
    {
        using var cts = CreateOperationSource(ct);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "logar.php"))
            {
                Content = new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("email", email),
                    new KeyValuePair<string, string>("password", password),
                ]),
            };
            request.Headers.TryAddWithoutValidation("Accept", LoginAccept);
            request.Headers.TryAddWithoutValidation(OriginHeaderName, OriginHeaderValue);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            DownloadResult? problem = ClassifyStatus(response.StatusCode, allowNotFound: false);
            if (problem is not null)
            {
                return problem with { Message = "Login: " + problem.Message };
            }

            if (!HasSessionCookie())
            {
                return DownloadResult.Auth(
                    "Login concluído sem cookie de sessão aplicável. Verifique as credenciais e a resposta do serviço.");
            }

            return DownloadResult.Ok("Sessão estabelecida.");
        }
        catch (Exception ex) when (TryMapException(ex, ct, _operationTimeout, "no login", out DownloadResult? mapped))
        {
            return mapped!;
        }
    }

    public async Task<DownloadResult> DownloadAsync(DownloadCandidate candidate, string outputDirectory, CancellationToken ct)
    {
        using var cts = CreateOperationSource(ct);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, candidate.GetUri(_baseUri));
            request.Headers.TryAddWithoutValidation(OriginHeaderName, OriginHeaderValue);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            DownloadResult? problem = ClassifyStatus(response.StatusCode, allowNotFound: true);
            if (problem is not null)
            {
                return problem;
            }

            return await SaveAsync(candidate, response, outputDirectory, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (TryMapException(ex, ct, _operationTimeout, "no download", out DownloadResult? mapped))
        {
            return mapped!;
        }
    }

    private CancellationTokenSource CreateOperationSource(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_operationTimeout);
        return cts;
    }

    private bool HasSessionCookie()
    {
        foreach (Cookie cookie in _cookies.GetCookies(new Uri(_baseUri, "index.php")))
        {
            if (cookie.Name == SessionCookieName && !string.IsNullOrEmpty(cookie.Value) && !cookie.Expired)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Retorna null quando o status é 200; caso contrário, o resultado correspondente.</summary>
    private static DownloadResult? ClassifyStatus(HttpStatusCode status, bool allowNotFound)
    {
        int code = (int)status;
        string text = $"HTTP {code} ({status})";

        if (status == HttpStatusCode.OK)
        {
            return null;
        }

        if (allowNotFound && status == HttpStatusCode.NotFound)
        {
            return DownloadResult.NotFound(text);
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return DownloadResult.Auth($"{text}: acesso negado ou sessão inválida.");
        }

        if (code == 429)
        {
            return DownloadResult.Fail($"{text}: limite de requisições do serviço.");
        }

        if (code >= 500)
        {
            return DownloadResult.Fail($"{text}: erro no serviço.");
        }

        if (code is >= 300 and < 400)
        {
            return DownloadResult.Fail($"{text}: redirecionamento não concluído (limite de {MaxRedirects} redirecionamentos).");
        }

        return DownloadResult.Fail($"{text}: status inesperado.");
    }

    private static bool TryMapException(Exception ex, CancellationToken ct, TimeSpan timeout, string where, out DownloadResult? result)
    {
        switch (ex)
        {
            case OperationCanceledException when ct.IsCancellationRequested:
                result = DownloadResult.Cancel();
                return true;
            case OperationCanceledException:
                result = DownloadResult.Fail($"Tempo limite de {timeout.TotalSeconds:0.###} s excedido {where}.");
                return true;
            case HttpRequestException or IOException:
                // Erros de disco são tratados em SaveAsync; o que chega aqui é de comunicação.
                string detail = ex.InnerException is null ? ex.Message : $"{ex.Message} {ex.InnerException.Message}";
                result = DownloadResult.Fail($"Falha de comunicação {where}: {detail}");
                return true;
            default:
                result = null;
                return false;
        }
    }

    private static async Task<DownloadResult> SaveAsync(
        DownloadCandidate candidate,
        HttpResponseMessage response,
        string outputDirectory,
        CancellationToken token)
    {
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
        {
            return DownloadResult.Fail($"Resposta HTML inesperada ({mediaType}) em {candidate.Description}; arquivo não salvo.");
        }

        if (response.Content.Headers.ContentLength == 0)
        {
            return DownloadResult.Fail($"Resposta vazia em {candidate.Description}; arquivo não salvo.");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        byte[] buffer = new byte[BufferSize];

        int count = await source.ReadAtLeastAsync(buffer, 5, throwOnEndOfStream: false, token).ConfigureAwait(false);
        if (count == 0)
        {
            return DownloadResult.Fail($"Resposta vazia em {candidate.Description}; arquivo não salvo.");
        }

        if (!HasExpectedSignature(candidate.Format, buffer.AsSpan(0, count)))
        {
            string expected = candidate.Format == OutputFormat.Pdf ? "PDF" : "ZIP";
            return DownloadResult.Fail($"O conteúdo recebido em {candidate.Description} não tem assinatura de {expected}; arquivo não salvo.");
        }

        string finalPath = Path.Combine(outputDirectory, candidate.LocalName);
        string tempPath = Path.Combine(outputDirectory, $".{candidate.LocalName}.{Guid.NewGuid():N}.tmp");
        bool moved = false;
        long total = 0;

        try
        {
            FileStream file;
            try
            {
                file = new FileStream(tempPath, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous,
                });
            }
            catch (Exception ex) when (IsDiskError(ex))
            {
                return DiskFailure(tempPath, ex);
            }

            try
            {
                int n = count;
                while (n > 0)
                {
                    try
                    {
                        await file.WriteAsync(buffer.AsMemory(0, n), token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (IsDiskError(ex))
                    {
                        return DiskFailure(tempPath, ex);
                    }

                    total += n;

                    // Falhas de leitura (rede) propagam para o chamador, que as classifica.
                    n = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                }

                try
                {
                    await file.FlushAsync(token).ConfigureAwait(false);
                    await file.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (IsDiskError(ex))
                {
                    return DiskFailure(tempPath, ex);
                }
            }
            finally
            {
                try
                {
                    await file.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (IsDiskError(ex))
                {
                    // O erro relevante, se houve, já foi reportado acima.
                }
            }

            try
            {
                File.Move(tempPath, finalPath, overwrite: true);
                moved = true;
            }
            catch (Exception ex) when (IsDiskError(ex))
            {
                return DiskFailure(finalPath, ex);
            }

            return DownloadResult.Ok($"{total} bytes", finalPath);
        }
        finally
        {
            if (!moved)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex) when (IsDiskError(ex))
                {
                    // Nada a fazer: o temporário é exclusivo desta operação.
                }
            }
        }
    }

    private static bool IsDiskError(Exception ex) => ex is IOException or UnauthorizedAccessException;

    private static DownloadResult DiskFailure(string path, Exception ex) =>
        DownloadResult.Fail($"Falha ao gravar em disco ('{path}'): {ex.Message}");

    private static bool HasExpectedSignature(OutputFormat format, ReadOnlySpan<byte> head)
    {
        if (format == OutputFormat.Pdf)
        {
            // "%PDF-"
            return head.Length >= 5 && head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46 && head[4] == 0x2D;
        }

        // ZIP: cabeçalho local de arquivo "PK\x03\x04" ou, em um ZIP vazio válido
        // (sem entradas), o registro de fim do diretório central "PK\x05\x06".
        return head.Length >= 4
            && head[0] == 0x50 && head[1] == 0x4B
            && ((head[2] == 0x03 && head[3] == 0x04) || (head[2] == 0x05 && head[3] == 0x06));
    }
}
