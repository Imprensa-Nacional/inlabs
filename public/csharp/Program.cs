using System.Globalization;
using System.Text;

namespace Inlabs.Download;

internal static class Program
{
    public const int ExitOk = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;
    public const int ExitAuth = 37;
    public const int ExitCancelled = 130;

    public const string EmailVariable = "INLABS_EMAIL";
    public const string PasswordVariable = "INLABS_PASSWORD";

    private static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // Sem console associado: segue com a codificação padrão.
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            return await RunAsync(
                args,
                Environment.GetEnvironmentVariable,
                Console.Out,
                Console.Error,
                createClient: null,
                today: null,
                cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>Fluxo completo da execução. Parâmetros opcionais existem para permitir verificações offline.</summary>
    internal static async Task<int> RunAsync(
        string[] args,
        Func<string, string?> getEnv,
        TextWriter output,
        TextWriter error,
        Func<InlabsClient>? createClient,
        Func<DateOnly>? today,
        CancellationToken ct)
    {
        ParseResult parsed = DownloadOptions.Parse(args);
        if (parsed.ShowHelp)
        {
            output.WriteLine(HelpText);
            return ExitOk;
        }

        if (parsed.Options is null)
        {
            error.WriteLine($"Erro: {parsed.Error}");
            error.WriteLine("Use --help para ver as opções disponíveis.");
            return ExitUsage;
        }

        DownloadOptions options = parsed.Options;

        string? email = getEnv(EmailVariable);
        string? password = getEnv(PasswordVariable);
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(email))
        {
            missing.Add(EmailVariable);
        }

        if (string.IsNullOrEmpty(password))
        {
            missing.Add(PasswordVariable);
        }

        if (missing.Count > 0)
        {
            error.WriteLine($"Erro: defina a(s) variável(is) de ambiente: {string.Join(", ", missing)}.");
            return ExitUsage;
        }

        void Info(string message) => output.WriteLine($"[{Timestamp()}] {message}");
        void Problem(string message) => error.WriteLine($"[{Timestamp()}] {message}");

        try
        {
            // Data local capturada uma única vez, antes do login.
            DateOnly date = today is null ? DateOnly.FromDateTime(DateTime.Now) : today();
            IReadOnlyList<DownloadCandidate> plan = DownloadPlan.Build(date, options.Format, options.Sections, options.PdfName);

            try
            {
                Directory.CreateDirectory(options.OutputDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Problem($"Não foi possível preparar o diretório de saída '{options.OutputDirectory}': {ex.Message}");
                return ExitFailure;
            }

            Info($"Início: formato {options.Format.ToString().ToUpperInvariant()}, data {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, "
                + $"seções {string.Join(' ', options.Sections)}, {plan.Count} solicitação(ões).");
            Info($"Diretório de saída: {options.OutputDirectory}");

            using InlabsClient client = createClient is null ? InlabsClient.CreateDefault() : createClient();

            Info("Autenticando no INLABS...");
            DownloadResult login = await client.LoginAsync(email!, password!, ct).ConfigureAwait(false);
            if (login.Status != DownloadStatus.Success)
            {
                return Finish(login, 0, 0, 0, plan.Count);
            }

            Info(login.Message);

            int saved = 0;
            int absent = 0;
            int processed = 0;

            foreach (DownloadCandidate candidate in plan)
            {
                DownloadResult result = await client.DownloadAsync(candidate, options.OutputDirectory, ct).ConfigureAwait(false);
                processed++;

                switch (result.Status)
                {
                    case DownloadStatus.Success:
                        saved++;
                        Info($"Baixado: {candidate.Description} -> {result.SavedPath} ({result.Message})");
                        break;
                    case DownloadStatus.Missing:
                        absent++;
                        Info($"Ausente: {candidate.Description} ({result.Message})");
                        break;
                    default:
                        Problem($"Falha em {candidate.Description}: {result.Message}");
                        return Finish(result, saved, absent, processed, plan.Count);
                }
            }

            return Finish(null, saved, absent, processed, plan.Count);

            int Finish(DownloadResult? stop, int savedCount, int absentCount, int processedCount, int total)
            {
                if (stop is { Status: DownloadStatus.Failure or DownloadStatus.AuthFailure } && processedCount == 0)
                {
                    Problem(stop.Message);
                }

                int failed = stop is null || stop.Status == DownloadStatus.Cancelled ? 0 : 1;
                if (stop is { Status: DownloadStatus.Cancelled })
                {
                    Problem("Execução cancelada pelo usuário.");
                }

                if (stop is not null)
                {
                    Info($"Execução interrompida após {processedCount} de {total} solicitação(ões).");
                }

                Info($"Resumo: {savedCount} arquivo(s) salvo(s), {absentCount} ausente(s), {failed} falha(s).");
                if (stop is null && savedCount == 0)
                {
                    Info("Nenhum arquivo disponível para a data e as seções solicitadas.");
                }

                return stop?.Status switch
                {
                    null or DownloadStatus.Success or DownloadStatus.Missing => ExitOk,
                    DownloadStatus.AuthFailure => ExitAuth,
                    DownloadStatus.Cancelled => ExitCancelled,
                    _ => ExitFailure,
                };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Problem("Execução cancelada pelo usuário.");
            return ExitCancelled;
        }
        catch (Exception ex)
        {
            Problem($"Erro inesperado ({ex.GetType().Name}): {ex.Message}");
            return ExitFailure;
        }
    }

    private static string Timestamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    internal const string HelpText = """
        Download de arquivos do INLABS (Imprensa Nacional) - versão C# / .NET 9

        Uso:
          Inlabs.Download [opções]

        Opções:
          --format xml|pdf        Formato dos arquivos. Padrão: xml (ZIPs de XML).
          --sections "<lista>"    Seções separadas por espaço, em um único argumento.
                                    xml: DO1 DO2 DO3 DO1E DO2E DO3E (padrão: todas)
                                    pdf: do1 do2 do3 (padrão: todas)
          --output <diretório>    Diretório de saída (criado se necessário). Padrão: diretório atual.
          --pdf-name remote|python
                                  Somente com --format pdf. Nome dos arquivos salvos.
                                    remote: nome remoto completo (padrão)
                                    python: estilo do exemplo Python (aaaa-MM-dd-secao.pdf)
          --help                  Exibe esta ajuda.

        Variáveis de ambiente obrigatórias para o download:
          INLABS_EMAIL            E-mail (login) da conta INLABS.
          INLABS_PASSWORD         Senha da conta INLABS.

        Códigos de saída:
          0    Concluído (inclui o caso em que todos os arquivos estão ausentes - HTTP 404).
          1    Falha operacional (rede, HTTP inesperado, conteúdo incompatível, disco).
          2    Argumentos ou credenciais ausentes/inválidos.
          37   Sessão inválida ou acesso negado (sem cookie de sessão, HTTP 401/403).
          130  Cancelado pelo usuário (Ctrl+C).
        """;
}
