// Verificações offline do downloader INLABS. Sem pacotes, sem credenciais reais, sem acesso ao INLABS.
// Execução: dotnet run --project public/csharp/tests/Inlabs.Download.Checks.csproj
// Código de saída: 0 = todas passaram; 1 = alguma falhou.

using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Inlabs.Download;

namespace Inlabs.Download.Checks;

internal static class ChecksRunner
{
    private static int _passed;
    private static int _failed;
    private static int _skipped;

    private static async Task<int> Main()
    {
        Console.OutputEncoding = new UTF8Encoding(false);

        // Plano e nomes
        Check("XML padrão: 6 candidatos na ordem esperada", XmlDefaultPlan);
        Check("PDF padrão: 81 candidatos, X antes de W", PdfDefaultPlan);
        Check("Data fixa 2026-01-09: URLs e nomes", FixedDateNames);
        Check("Perfis de nomes remote/python", NameProfiles);

        // Entradas
        Check("Opções: padrões", OptionsDefaults);
        Check("Opções: seções (caixa, espaços, duplicatas)", OptionsSections);
        Check("Opções: entradas inválidas", OptionsInvalid);

        // Fluxo com transporte falso
        await CheckAsync("Help e erros de configuração: nenhuma requisição HTTP", NoHttpOnConfigErrors);
        await CheckAsync("Formulário de login, cabeçalhos e ausência de segredos no log", LoginFormAndHeaders);
        await CheckAsync("Status: 200 salva, 404 continua, todos 404 => 0", StatusSaveAndMissing);
        await CheckAsync("PDF: 404 do regular não impede extra existente", PdfExtraWithoutRegular);
        await CheckAsync("Status: 401/403/429/5xx/204/206 e falhas de login", StatusFailures);
        await CheckAsync("Conteúdo: HTML, vazio e assinatura inválida não substituem arquivo", ContentRejected);
        await CheckAsync("Conteúdo: ZIP vazio válido (PK 05 06) é aceito e preservado", EmptyZipAccepted);
        await CheckAsync("Arquivos: bytes preservados, sobrescrita e destino com espaços", FilesSaved);
        await CheckAsync("Arquivos: falha na cópia preserva arquivo anterior e remove temporário", CopyFailure);
        await CheckAsync("Arquivos: falha de disco preserva destino e remove temporário", DiskFailureCase);
        await CheckAsync("Timeout cobre o corpo da resposta", TimeoutCoversBody);
        await CheckAsync("Cancelamento => 130 sem temporários", CancellationCase);

        // Handler real contra servidor de loopback
        await LoopbackChecksAsync();

        Console.WriteLine();
        Console.WriteLine($"Resultado: {_passed} aprovada(s), {_failed} falha(s), {_skipped} ignorada(s).");
        return _failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- infraestrutura

    private static void Check(string name, Action body) =>
        CheckAsync(name, () => { body(); return Task.CompletedTask; }).GetAwaiter().GetResult();

    private static async Task CheckAsync(string name, Func<Task> body)
    {
        try
        {
            await body();
            _passed++;
            Console.WriteLine($"[OK]    {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"[FALHA] {name}\n        {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Skip(string name, string reason)
    {
        _skipped++;
        Console.WriteLine($"[IGNORADA] {name} ({reason})");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string what) =>
        Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"{what}: esperado '{expected}', obtido '{actual}'.");

    private static void SequenceEqual(IEnumerable<string> expected, IEnumerable<string> actual, string what) =>
        Assert(expected.SequenceEqual(actual), $"{what}: esperado [{string.Join(", ", expected)}], obtido [{string.Join(", ", actual)}].");

    private const string FakeEmail = "usuario+teste@exemplo.invalid";
    private const string FakePassword = "S3nh@&+%=ü ç/\\\"fict";
    private const string FakeCookieValue = "valor-ficticio-de-sessao";
    private static readonly DateOnly FixedDate = new(2026, 1, 9);

    private static readonly byte[] ZipBytes = [.. "PK\x03\x04"u8.ToArray(), .. Enumerable.Range(0, 3000).Select(i => (byte)(i % 251))];
    private static readonly byte[] PdfBytes = [.. "%PDF-1.7\n"u8.ToArray(), .. Enumerable.Range(0, 3000).Select(i => (byte)(i % 241))];

    private static string NewTempDir(string name = "saida")
    {
        string root = Path.Combine(Path.GetTempPath(), "inlabs-checks-" + Guid.NewGuid().ToString("N"));
        string dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed record Captured(HttpMethod Method, Uri Uri, string? Origem, string? Accept, string? ContentType, string? Body);

    private sealed class FakeHandler(Func<Captured, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = new Captured(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("origem", out var o) ? string.Join(",", o) : null,
                request.Headers.TryGetValues("Accept", out var a) ? string.Join(",", a) : null,
                request.Content?.Headers.ContentType?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(captured);
            return await respond(captured, cancellationToken);
        }
    }

    private static HttpResponseMessage Resp(HttpStatusCode code, byte[]? body = null, string? mediaType = null)
    {
        var r = new HttpResponseMessage(code) { Content = new ByteArrayContent(body ?? []) };
        if (mediaType is not null)
        {
            r.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        }

        return r;
    }

    private sealed record Run(int Code, string Out, string Err, FakeHandler Handler)
    {
        public string AllText => Out + Err;
        public IEnumerable<Captured> Downloads => Handler.Requests.Where(r => r.Method == HttpMethod.Get);
    }

    /// <summary>Executa Program.RunAsync com transporte falso. O login sempre grava um cookie no container.</summary>
    private static async Task<Run> RunFake(
        string[] args,
        Func<Captured, CancellationToken, Task<HttpResponseMessage>> download,
        Func<Captured, CancellationToken, Task<HttpResponseMessage>>? login = null,
        bool setCookie = true,
        Action<CookieContainer>? onLogin = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        string? email = FakeEmail,
        string? password = FakePassword)
    {
        var cookies = new CookieContainer();
        var handler = new FakeHandler(async (req, token) =>
        {
            if (req.Method == HttpMethod.Post)
            {
                if (login is not null)
                {
                    return await login(req, token);
                }

                // Permite ao teste manipular o MESMO container entregue ao InlabsClient.
                onLogin?.Invoke(cookies);

                if (setCookie)
                {
                    cookies.Add(InlabsClient.ProductionBaseUri, new Cookie(InlabsClient.SessionCookieName, FakeCookieValue, "/"));
                }

                return Resp(HttpStatusCode.OK, "<html>ok</html>"u8.ToArray(), "text/html");
            }

            return await download(req, token);
        });

        var env = new Dictionary<string, string?>
        {
            [Program.EmailVariable] = email,
            [Program.PasswordVariable] = password,
        };
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = await Program.RunAsync(
            args,
            name => env.GetValueOrDefault(name),
            stdout,
            stderr,
            () => new InlabsClient(handler, cookies, null, timeout),
            () => FixedDate,
            ct);
        return new Run(code, stdout.ToString(), stderr.ToString(), handler);
    }

    private static Task<HttpResponseMessage> Task_(HttpResponseMessage r) => Task.FromResult(r);

    // ---------------------------------------------------------------- plano e nomes

    private static void XmlDefaultPlan()
    {
        var plan = DownloadPlan.Build(FixedDate, OutputFormat.Xml, DownloadOptions.XmlSections);
        Equal(6, plan.Count, "quantidade");
        SequenceEqual(
            ["2026-01-09-DO1.zip", "2026-01-09-DO2.zip", "2026-01-09-DO3.zip", "2026-01-09-DO1E.zip", "2026-01-09-DO2E.zip", "2026-01-09-DO3E.zip"],
            plan.Select(c => c.RemoteName),
            "nomes");
        SequenceEqual(plan.Select(c => c.RemoteName), plan.Select(c => c.LocalName), "nome local XML = remoto");
    }

    private static void PdfDefaultPlan()
    {
        var plan = DownloadPlan.Build(FixedDate, OutputFormat.Pdf, DownloadOptions.PdfSections);
        Equal(81, plan.Count, "quantidade");
        Equal("A B C D E F G H I J K L M N O P Q R S T U V X W Y Z", string.Join(' ', DownloadPlan.ExtraLetters.ToCharArray()), "letras");
        Equal("2026_01_09_ASSINADO_do1.pdf", plan[0].RemoteName, "primeiro");
        Equal(null, plan[0].ExtraLetter, "primeiro é regular");
        Equal("2026_01_09_ASSINADO_do1_extra_A.pdf", plan[1].RemoteName, "segundo");
        Equal('V', plan[22].ExtraLetter, "posição de V");
        Equal('X', plan[23].ExtraLetter, "posição de X (antes de W)");
        Equal('W', plan[24].ExtraLetter, "posição de W");
        Equal("2026_01_09_ASSINADO_do2.pdf", plan[27].RemoteName, "início da seção 2");
        Equal("2026_01_09_ASSINADO_do3_extra_Z.pdf", plan[80].RemoteName, "último");
        Assert(plan.Select(c => c.RemoteName).Distinct().Count() == 81, "nomes devem ser únicos");
    }

    private static void FixedDateNames()
    {
        var xml = DownloadPlan.Build(FixedDate, OutputFormat.Xml, ["DO1E"])[0];
        Equal("2026-01-09-DO1E.zip", xml.RemoteName, "ZIP");
        Equal("https://inlabs.in.gov.br/index.php?p=2026-01-09&dl=2026-01-09-DO1E.zip", xml.GetUri(InlabsClient.ProductionBaseUri).ToString(), "URL XML");

        var pdf = DownloadPlan.Build(FixedDate, OutputFormat.Pdf, ["do1"]);
        Equal("https://inlabs.in.gov.br/index.php?p=2026-01-09&dl=2026_01_09_ASSINADO_do1.pdf", pdf[0].GetUri(InlabsClient.ProductionBaseUri).ToString(), "URL PDF");
        Equal("https://inlabs.in.gov.br/index.php?p=2026-01-09&dl=2026_01_09_ASSINADO_do1_extra_A.pdf", pdf[1].GetUri(InlabsClient.ProductionBaseUri).ToString(), "URL extra");
    }

    private static void NameProfiles()
    {
        var remote = DownloadPlan.Build(FixedDate, OutputFormat.Pdf, ["do1"], PdfNameProfile.Remote);
        Equal("2026_01_09_ASSINADO_do1.pdf", remote[0].LocalName, "regular remote");
        Equal("2026_01_09_ASSINADO_do1_extra_B.pdf", remote[2].LocalName, "extra remote (com .pdf)");

        var py = DownloadPlan.Build(FixedDate, OutputFormat.Pdf, ["do1"], PdfNameProfile.Python);
        Equal("2026-01-09-do1.pdf", py[0].LocalName, "regular python");
        Equal("2026-01-09-do1_extra_B.pdf", py[2].LocalName, "extra python");
        Equal("2026_01_09_ASSINADO_do1_extra_B.pdf", py[2].RemoteName, "nome remoto não muda");
    }

    // ---------------------------------------------------------------- entradas

    private static void OptionsDefaults()
    {
        var r = DownloadOptions.Parse([]);
        Assert(r.Options is not null, "esperava opções");
        Equal(OutputFormat.Xml, r.Options!.Format, "formato");
        SequenceEqual(DownloadOptions.XmlSections, r.Options.Sections, "seções");
        Equal(PdfNameProfile.Remote, r.Options.PdfName, "perfil");
        Equal(Directory.GetCurrentDirectory(), r.Options.OutputDirectory, "saída");

        var pdf = DownloadOptions.Parse(["--format", "PDF"]);
        SequenceEqual(DownloadOptions.PdfSections, pdf.Options!.Sections, "seções PDF");

        var eq = DownloadOptions.Parse(["--format=pdf", "--pdf-name=Python", "--output=./x y"]);
        Equal(PdfNameProfile.Python, eq.Options!.PdfName, "perfil com '='");
        Assert(eq.Options.OutputDirectory.EndsWith("x y", StringComparison.Ordinal) && Path.IsPathRooted(eq.Options.OutputDirectory), "saída absoluta com espaço");
    }

    private static void OptionsSections()
    {
        SequenceEqual(["DO1", "DO2"], DownloadOptions.Parse(["--sections", " do1   DO2 do1 "]).Options!.Sections, "XML normaliza p/ maiúsculas");
        SequenceEqual(["DO1E", "DO3E"], DownloadOptions.Parse(["--sections", "DO1E\tDO3E"]).Options!.Sections, "extras XML");
        SequenceEqual(["do2", "do1"], DownloadOptions.Parse(["--format", "pdf", "--sections", "DO2 do1 DO2"]).Options!.Sections, "PDF normaliza p/ minúsculas, ordem preservada");
    }

    private static void OptionsInvalid()
    {
        string[][] invalid =
        [
            ["--sections", ""],
            ["--sections", "   "],
            ["--sections", "DO4"],
            ["--format", "pdf", "--sections", "do1e"],
            ["--format", "docx"],
            ["--foo"],
            ["--format", "xml", "--format", "pdf"],
            ["--format"],
            ["--format", "--sections", "DO1"],
            ["--sections"],
            ["--pdf-name", "python"],
            ["--format", "pdf", "--pdf-name", "outro"],
            ["--output", ""],
            ["posicional"],
            ["--sections", "DO1", "DO2"],
        ];
        foreach (string[] args in invalid)
        {
            var r = DownloadOptions.Parse(args);
            Assert(r.Options is null && !r.ShowHelp && r.Error is not null, $"deveria rejeitar: {string.Join(' ', args.Select(a => $"'{a}'"))}");
        }

        var help = DownloadOptions.Parse(["--foo", "--help"]);
        Assert(help.ShowHelp && help.Options is null && help.Error is null, "--help tem precedência");
    }

    // ---------------------------------------------------------------- fluxo (transporte falso)

    private static async Task NoHttpOnConfigErrors()
    {
        string dir = NewTempDir();
        try
        {
            foreach (var (args, email, password, expected) in new (string[], string?, string?, int)[]
            {
                (["--help"], null, null, 0),
                (["--format", "bmp"], FakeEmail, FakePassword, 2),
                ([], null, FakePassword, 2),
                ([], "   ", FakePassword, 2),
                ([], FakeEmail, null, 2),
                ([], FakeEmail, "", 2),
            })
            {
                int clients = 0;
                var env = new Dictionary<string, string?> { [Program.EmailVariable] = email, [Program.PasswordVariable] = password };
                var o = new StringWriter();
                var e = new StringWriter();
                int code = await Program.RunAsync(
                    args,
                    n => env.GetValueOrDefault(n),
                    o,
                    e,
                    () =>
                    {
                        clients++;
                        throw new InvalidOperationException("cliente HTTP não deveria ser criado");
                    },
                    () => FixedDate,
                    CancellationToken.None);
                Equal(expected, code, $"código para [{string.Join(' ', args)}]");
                Equal(0, clients, "clientes criados");
            }
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static async Task LoginFormAndHeaders()
    {
        string dir = NewTempDir();
        try
        {
            var run = await RunFake(
                ["--sections", "DO1E", "--output", dir],
                (_, _) => Task_(Resp(HttpStatusCode.OK, ZipBytes, "application/zip")));
            Equal(0, run.Code, "código");

            var login = run.Handler.Requests[0];
            Equal("https://inlabs.in.gov.br/logar.php", login.Uri.ToString(), "URL de login");
            Equal("736372697074", login.Origem, "origem no login");
            // O HttpClient pode reformatar espaços após vírgulas/pontos e vírgulas; o conteúdo deve ser o mesmo.
            Equal("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8", login.Accept?.Replace(" ", ""), "Accept no login");
            Assert(login.ContentType!.StartsWith("application/x-www-form-urlencoded", StringComparison.Ordinal), "Content-Type do formulário");

            var fields = login.Body!.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
            Equal(2, fields.Count, "campos");
            Equal(FakeEmail, fields["email"], "email decodificado");
            Equal(FakePassword, fields["password"], "senha decodificada");

            var get = run.Downloads.Single();
            Equal("736372697074", get.Origem, "origem no download");
            Equal("https://inlabs.in.gov.br/index.php?p=2026-01-09&dl=2026-01-09-DO1E.zip", get.Uri.ToString(), "URL de download");

            foreach (string secret in new[] { FakePassword, FakeEmail, FakeCookieValue, "S3nh@" })
            {
                Assert(!run.AllText.Contains(secret, StringComparison.Ordinal), $"segredo vazou no log: {secret}");
            }
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static async Task StatusSaveAndMissing()
    {
        string dir = NewTempDir();
        try
        {
            // DO1 e DO3E existem; o resto é 404.
            var run = await RunFake(
                ["--output", dir],
                (req, _) => Task_(req.Uri.Query.Contains("2026-01-09-DO1.zip") || req.Uri.Query.Contains("2026-01-09-DO3E.zip")
                    ? Resp(HttpStatusCode.OK, ZipBytes, "application/octet-stream")
                    : Resp(HttpStatusCode.NotFound, "não encontrado"u8.ToArray(), "text/html")));
            Equal(0, run.Code, "código");
            Equal(6, run.Downloads.Count(), "todas as seções solicitadas");
            SequenceEqual(["2026-01-09-DO1.zip", "2026-01-09-DO3E.zip"], Directory.GetFiles(dir).Select(Path.GetFileName)!.Order()!, "arquivos");
            Assert(File.ReadAllBytes(Path.Combine(dir, "2026-01-09-DO1.zip")).SequenceEqual(ZipBytes), "bytes idênticos");
            Assert(run.Out.Contains("2 arquivo(s) salvo(s), 4 ausente(s), 0 falha(s)", StringComparison.Ordinal), "resumo");
        }
        finally
        {
            Cleanup(dir);
        }

        // Todos ausentes
        dir = NewTempDir();
        try
        {
            var run = await RunFake(["--output", dir], (_, _) => Task_(Resp(HttpStatusCode.NotFound)));
            Equal(0, run.Code, "todos 404 => 0");
            Equal(0, Directory.GetFileSystemEntries(dir).Length, "nenhum arquivo criado");
            Assert(run.Out.Contains("Nenhum arquivo disponível", StringComparison.Ordinal), "mensagem de nenhum arquivo");
            Assert(!run.Out.Contains("Baixado", StringComparison.Ordinal), "não deve afirmar download");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static async Task PdfExtraWithoutRegular()
    {
        string dir = NewTempDir();
        try
        {
            var run = await RunFake(
                ["--format", "pdf", "--sections", "do1", "--output", dir],
                (req, _) => Task_(req.Uri.Query.EndsWith("_extra_C.pdf", StringComparison.Ordinal)
                    ? Resp(HttpStatusCode.OK, PdfBytes, "application/pdf")
                    : Resp(HttpStatusCode.NotFound)));
            Equal(0, run.Code, "código");
            Equal(27, run.Downloads.Count(), "regular + 26 extras");
            string[] files = Directory.GetFiles(dir).Select(Path.GetFileName).ToArray()!;
            SequenceEqual(["2026_01_09_ASSINADO_do1_extra_C.pdf"], files, "arquivo salvo com .pdf");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static async Task StatusFailures()
    {
        string dir = NewTempDir();
        try
        {
            foreach (var (status, expected) in new[]
            {
                (HttpStatusCode.Unauthorized, 37),
                (HttpStatusCode.Forbidden, 37),
                ((HttpStatusCode)429, 1),
                (HttpStatusCode.InternalServerError, 1),
                (HttpStatusCode.BadGateway, 1),
                (HttpStatusCode.NoContent, 1),
                (HttpStatusCode.PartialContent, 1),
                (HttpStatusCode.Found, 1),
                (HttpStatusCode.BadRequest, 1),
            })
            {
                var run = await RunFake(["--output", dir], (_, _) => Task_(Resp(status, ZipBytes, "application/zip")));
                Equal(expected, run.Code, $"código para HTTP {(int)status}");
                Equal(1, run.Downloads.Count(), $"deve parar no primeiro download (HTTP {(int)status})");
                Equal(0, Directory.GetFileSystemEntries(dir).Length, $"nada gravado (HTTP {(int)status})");
            }

            // Login
            var noCookie = await RunFake(["--output", dir], (_, _) => Task_(Resp(HttpStatusCode.OK)), setCookie: false);
            Equal(37, noCookie.Code, "login sem cookie");
            Equal(0, noCookie.Downloads.Count(), "sem downloads após login sem cookie");

            foreach (var (status, expected) in new[] { (HttpStatusCode.Forbidden, 37), (HttpStatusCode.Unauthorized, 37), (HttpStatusCode.InternalServerError, 1), (HttpStatusCode.NotFound, 1) })
            {
                var run = await RunFake(
                    ["--output", dir],
                    (_, _) => Task_(Resp(HttpStatusCode.OK, ZipBytes)),
                    login: (_, _) => Task_(Resp(status)));
                Equal(expected, run.Code, $"login HTTP {(int)status}");
                Equal(0, run.Downloads.Count(), "sem downloads após falha de login");
            }

            // Cookie presente, mas vazio, não vale como sessão. O cookie é adicionado ao container
            // realmente usado pelo InlabsClient (via onLogin) e o teste confirma que ele está lá.
            int presentInContainer = -1;
            var emptyCookie = await RunFake(
                ["--output", dir],
                (_, _) => Task_(Resp(HttpStatusCode.OK, ZipBytes)),
                setCookie: false,
                onLogin: container =>
                {
                    container.Add(InlabsClient.ProductionBaseUri, new Cookie(InlabsClient.SessionCookieName, "", "/"));
                    presentInContainer = container.GetCookies(new Uri(InlabsClient.ProductionBaseUri, "index.php")).Count;
                });
            Equal(1, presentInContainer, "o cookie vazio deve existir no container do cliente");
            Equal(37, emptyCookie.Code, "cookie vazio");
            Equal(0, emptyCookie.Downloads.Count(), "sem downloads com cookie vazio");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static async Task ContentRejected()
    {
        var cases = new (string Name, string[] Args, Func<HttpResponseMessage> Response)[]
        {
            ("HTML com MIME text/html", ["--sections", "DO1"], () => Resp(HttpStatusCode.OK, ZipBytes, "text/html")),
            ("XHTML", ["--sections", "DO1"], () => Resp(HttpStatusCode.OK, ZipBytes, "application/xhtml+xml")),
            ("HTML com MIME enganoso", ["--sections", "DO1"], () => Resp(HttpStatusCode.OK, "<!DOCTYPE html><html>login</html>"u8.ToArray(), "application/octet-stream")),
            ("corpo vazio", ["--sections", "DO1"], () => Resp(HttpStatusCode.OK, [], "application/zip")),
            ("curto demais", ["--sections", "DO1"], () => Resp(HttpStatusCode.OK, "PK"u8.ToArray(), "application/zip")),
            ("ZIP com assinatura inválida", ["--sections", "DO1"], () => Resp(HttpStatusCode.OK, "NAO-E-UM-ZIP"u8.ToArray(), "application/zip")),
            ("PDF com assinatura de ZIP", ["--format", "pdf", "--sections", "do1"], () => Resp(HttpStatusCode.OK, ZipBytes, "application/pdf")),
        };

        foreach (var c in cases)
        {
            string dir = NewTempDir();
            try
            {
                string target = Path.Combine(dir, c.Args.Contains("pdf") ? "2026_01_09_ASSINADO_do1.pdf" : "2026-01-09-DO1.zip");
                File.WriteAllText(target, "CONTEUDO-ANTIGO");

                var run = await RunFake([.. c.Args, "--output", dir], (_, _) => Task_(c.Response()));
                Equal(1, run.Code, $"{c.Name}: código");
                Equal(1, run.Downloads.Count(), $"{c.Name}: encerra na resposta incompatível");
                Equal("CONTEUDO-ANTIGO", File.ReadAllText(target), $"{c.Name}: arquivo anterior");
                Equal(1, Directory.GetFileSystemEntries(dir).Length, $"{c.Name}: sem temporários");
            }
            finally
            {
                Cleanup(dir);
            }
        }
    }

    private static async Task EmptyZipAccepted()
    {
        byte[] emptyZip;
        using (var ms = new MemoryStream())
        {
            using (new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
            }

            emptyZip = ms.ToArray();
        }

        Assert(emptyZip.Length > 0 && emptyZip[2] == 0x05 && emptyZip[3] == 0x06, "o ZIP vazio gerado deve começar com PK 05 06");

        string dir = NewTempDir();
        try
        {
            var run = await RunFake(
                ["--sections", "DO1", "--output", dir],
                (_, _) => Task_(Resp(HttpStatusCode.OK, emptyZip, "application/zip")));
            Equal(0, run.Code, "código");
            string target = Path.Combine(dir, "2026-01-09-DO1.zip");
            Assert(File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(emptyZip), "bytes do ZIP vazio preservados");
            Equal(1, Directory.GetFileSystemEntries(dir).Length, "sem temporários");
            Assert(run.Out.Contains("1 arquivo(s) salvo(s), 0 ausente(s), 0 falha(s)", StringComparison.Ordinal), "resumo");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static async Task FilesSaved()
    {
        string dir = NewTempDir("pasta com espaços");
        try
        {
            string target = Path.Combine(dir, "2026-01-09-DO2.zip");
            File.WriteAllText(target, "ANTIGO");

            var run = await RunFake(
                ["--sections", "DO2", "--output", dir],
                (_, _) => Task_(Resp(HttpStatusCode.OK, ZipBytes, "application/zip")));
            Equal(0, run.Code, "código");
            Assert(File.ReadAllBytes(target).SequenceEqual(ZipBytes), "arquivo sobrescrito com bytes idênticos");
            Equal(1, Directory.GetFileSystemEntries(dir).Length, "sem temporários");
            Assert(run.Out.Contains(target, StringComparison.Ordinal), "log traz o caminho real gravado");

            // Diretório inexistente é criado
            string nested = Path.Combine(dir, "a", "b c");
            var run2 = await RunFake(
                ["--format", "pdf", "--sections", "do3", "--pdf-name", "python", "--output", nested],
                (req, _) => Task_(req.Uri.Query.EndsWith("ASSINADO_do3.pdf", StringComparison.Ordinal)
                    ? Resp(HttpStatusCode.OK, PdfBytes, "application/pdf")
                    : Resp(HttpStatusCode.NotFound)));
            Equal(0, run2.Code, "código (pdf)");
            Assert(File.Exists(Path.Combine(nested, "2026-01-09-do3.pdf")), "perfil python: nome efetivamente gravado");
            Assert(run2.Out.Contains("2026-01-09-do3.pdf", StringComparison.Ordinal), "log mostra o nome gravado");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private sealed class FailingStream(byte[] head, Exception error) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= head.Length)
            {
                throw error;
            }

            int n = Math.Min(buffer.Length, Math.Min(head.Length - _position, 512));
            head.AsMemory(_position, n).CopyTo(buffer);
            _position += n;
            return ValueTask.FromResult(n);
        }
    }

    private sealed class HangingStream(byte[] head) : Stream
    {
        private bool _sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                head.AsMemory(0, Math.Min(head.Length, buffer.Length)).CopyTo(buffer);
                return Math.Min(head.Length, buffer.Length);
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private static async Task CopyFailure()
    {
        string dir = NewTempDir();
        try
        {
            string target = Path.Combine(dir, "2026-01-09-DO1.zip");
            File.WriteAllText(target, "ANTIGO");
            var run = await RunFake(
                ["--sections", "DO1 DO2", "--output", dir],
                (_, _) => Task_(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new FailingStream(ZipBytes, new IOException("conexão interrompida")))
                }));
            Equal(1, run.Code, "código");
            Equal(1, run.Downloads.Count(), "não prossegue após a falha");
            Equal("ANTIGO", File.ReadAllText(target), "arquivo anterior preservado");
            Equal(1, Directory.GetFileSystemEntries(dir).Length, "temporário removido");
            Assert(run.Err.Contains("comunicação", StringComparison.Ordinal), "mensagem de falha de comunicação");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static async Task DiskFailureCase()
    {
        string dir = NewTempDir();
        try
        {
            // Um diretório com o nome do destino impede a substituição.
            string blocker = Path.Combine(dir, "2026-01-09-DO1.zip");
            Directory.CreateDirectory(blocker);
            File.WriteAllText(Path.Combine(blocker, "marca.txt"), "x");

            var run = await RunFake(["--sections", "DO1", "--output", dir], (_, _) => Task_(Resp(HttpStatusCode.OK, ZipBytes, "application/zip")));
            Equal(1, run.Code, "código");
            Assert(run.Err.Contains("disco", StringComparison.Ordinal), "mensagem de disco");
            Assert(File.Exists(Path.Combine(blocker, "marca.txt")), "destino preservado");
            Equal(1, Directory.GetFileSystemEntries(dir).Length, "temporário removido");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static async Task TimeoutCoversBody()
    {
        string dir = NewTempDir();
        try
        {
            string target = Path.Combine(dir, "2026-01-09-DO1.zip");
            File.WriteAllText(target, "ANTIGO");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var run = await RunFake(
                ["--sections", "DO1 DO2", "--output", dir],
                (_, _) => Task_(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream(ZipBytes)) }),
                timeout: TimeSpan.FromMilliseconds(500));
            Assert(watch.Elapsed < TimeSpan.FromSeconds(20), "deveria terminar logo após o timeout");
            Equal(1, run.Code, "código");
            Equal(1, run.Downloads.Count(), "não prossegue após timeout");
            Assert(run.Err.Contains("Tempo limite", StringComparison.Ordinal), "mensagem de timeout");
            Equal("ANTIGO", File.ReadAllText(target), "arquivo anterior preservado");
            Equal(1, Directory.GetFileSystemEntries(dir).Length, "temporário removido");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static async Task CancellationCase()
    {
        string dir = NewTempDir();
        try
        {
            using var cts = new CancellationTokenSource();
            var run = await RunFake(
                ["--sections", "DO1 DO2", "--output", dir],
                (_, _) =>
                {
                    cts.CancelAfter(300);
                    return Task_(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream(ZipBytes)) });
                },
                ct: cts.Token);
            Equal(130, run.Code, "código");
            Equal(1, run.Downloads.Count(), "sem novas requisições");
            Equal(0, Directory.GetFileSystemEntries(dir).Length, "sem arquivos/temporários");

            // Cancelamento durante o login
            using var cts2 = new CancellationTokenSource(300);
            var run2 = await RunFake(
                ["--output", dir],
                (_, _) => Task_(Resp(HttpStatusCode.OK)),
                login: async (_, token) =>
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return Resp(HttpStatusCode.OK);
                },
                ct: cts2.Token);
            Equal(130, run2.Code, "código (login)");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // ---------------------------------------------------------------- handler real + servidor de loopback

    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        public List<string> Log { get; } = [];
        public Uri BaseUri { get; }

        public LoopbackServer(Func<HttpListenerContext, Task> handle)
        {
            int port;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            BaseUri = new Uri($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add(BaseUri.ToString());
            _listener.Start();
            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = await _listener.GetContextAsync();
                    }
                    catch (Exception) when (_stop.IsCancellationRequested || !_listener.IsListening)
                    {
                        return;
                    }

                    try
                    {
                        lock (Log)
                        {
                            Log.Add($"{ctx.Request.HttpMethod} {ctx.Request.Url!.PathAndQuery} cookie={(ctx.Request.Headers["Cookie"] is { Length: > 0 } ? "sim" : "não")} origem={ctx.Request.Headers["origem"]}");
                        }

                        await handle(ctx);
                    }
                    catch (Exception)
                    {
                        ctx.Response.StatusCode = 500;
                    }
                    finally
                    {
                        ctx.Response.Close();
                    }
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Close();
            await _loop;
        }
    }

    private static bool HasSessionCookie(HttpListenerContext ctx) =>
        (ctx.Request.Headers["Cookie"] ?? "").Contains($"{InlabsClient.SessionCookieName}=loopback-ficticio", StringComparison.Ordinal);

    private static void Redirect(HttpListenerContext ctx, string location)
    {
        ctx.Response.StatusCode = 302;
        ctx.Response.RedirectLocation = location;
    }

    private static void Bytes(HttpListenerContext ctx, byte[] body, string type = "application/octet-stream", bool gzip = false)
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = type;
        if (gzip)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                gz.Write(body);
            }

            ctx.Response.AddHeader("Content-Encoding", "gzip");
            body = ms.ToArray();
        }

        ctx.Response.ContentLength64 = body.Length;
        ctx.Response.OutputStream.Write(body);
    }

    /// <summary>Servidor que se comporta como o INLABS: login com Set-Cookie + redirect; download exige cookie e origem.</summary>
    private static Func<HttpListenerContext, Task> InlabsLike(
        string cookieAttributes,
        bool redirectOnDownload = true,
        bool gzip = false,
        string cookieValue = "loopback-ficticio") => ctx =>
    {
        string path = ctx.Request.Url!.AbsolutePath;
        if (path == "/logar.php" && ctx.Request.HttpMethod == "POST")
        {
            if (cookieAttributes.Length > 0)
            {
                ctx.Response.AppendHeader("Set-Cookie", $"{InlabsClient.SessionCookieName}={cookieValue}; {cookieAttributes}");
            }

            Redirect(ctx, "/home.php");
        }
        else if (path == "/home.php")
        {
            Bytes(ctx, "<html>inicio</html>"u8.ToArray(), "text/html");
        }
        else if (path == "/index.php")
        {
            if (!HasSessionCookie(ctx) || ctx.Request.Headers["origem"] != InlabsClient.OriginHeaderValue)
            {
                ctx.Response.StatusCode = 403;
            }
            else if (redirectOnDownload)
            {
                Redirect(ctx, "/arquivos/real.bin");
            }
            else
            {
                Bytes(ctx, ZipBytes, gzip: gzip);
            }
        }
        else if (path == "/arquivos/real.bin")
        {
            if (!HasSessionCookie(ctx))
            {
                ctx.Response.StatusCode = 403;
            }
            else
            {
                Bytes(ctx, ZipBytes, gzip: gzip);
            }
        }
        else
        {
            ctx.Response.StatusCode = 404;
        }

        return Task.CompletedTask;
    };

    private static async Task<(int Code, string Text, LoopbackServer Server)> RunLoopback(
        Func<HttpListenerContext, Task> handler,
        string dir,
        string sections = "DO1",
        TimeSpan? timeout = null)
    {
        var server = new LoopbackServer(handler);
        var env = new Dictionary<string, string?> { [Program.EmailVariable] = FakeEmail, [Program.PasswordVariable] = FakePassword };
        var o = new StringWriter();
        var e = new StringWriter();
        int code = await Program.RunAsync(
            ["--sections", sections, "--output", dir],
            n => env.GetValueOrDefault(n),
            o,
            e,
            () => InlabsClient.CreateDefault(server.BaseUri, timeout),
            () => FixedDate,
            CancellationToken.None);
        return (code, o.ToString() + e.ToString(), server);
    }

    private static async Task LoopbackChecksAsync()
    {
        const string group = "Handler real (loopback)";
        try
        {
            await using var probe = new LoopbackServer(_ => Task.CompletedTask);
        }
        catch (Exception ex) when (ex is HttpListenerException or SocketException or PlatformNotSupportedException)
        {
            Skip(group, $"HttpListener indisponível neste ambiente: {ex.Message}");
            return;
        }

        await CheckAsync("Loopback: cookie + redirects de login e de download (handler real)", async () =>
        {
            string dir = NewTempDir();
            try
            {
                var (code, text, server) = await RunLoopback(InlabsLike("Path=/"), dir);
                await using (server)
                {
                    Equal(0, code, "código");
                    Assert(File.ReadAllBytes(Path.Combine(dir, "2026-01-09-DO1.zip")).SequenceEqual(ZipBytes), "bytes idênticos");
                    Assert(server.Log.Any(l => l.StartsWith("GET /arquivos/real.bin", StringComparison.Ordinal) && l.Contains("cookie=sim", StringComparison.Ordinal)), "cookie reenviado após o redirect");
                    Assert(server.Log.Where(l => l.StartsWith("GET /index.php", StringComparison.Ordinal)).All(l => l.Contains("origem=736372697074", StringComparison.Ordinal)), "cabeçalho origem no download");
                    Assert(!text.Contains("loopback-ficticio", StringComparison.Ordinal), "valor do cookie fora do log");
                }
            }
            finally
            {
                Cleanup(dir);
            }
        });

        await CheckAsync("Loopback: descompressão HTTP preserva os bytes do ZIP", async () =>
        {
            string dir = NewTempDir();
            try
            {
                var (code, _, server) = await RunLoopback(InlabsLike("Path=/", redirectOnDownload: false, gzip: true), dir);
                await using (server)
                {
                    Equal(0, code, "código");
                    Assert(File.ReadAllBytes(Path.Combine(dir, "2026-01-09-DO1.zip")).SequenceEqual(ZipBytes), "bytes descomprimidos idênticos");
                }
            }
            finally
            {
                Cleanup(dir);
            }
        });

        var cookieCases = new (string Name, string Attributes, string Value)[]
        {
            ("sem Set-Cookie", "", "loopback-ficticio"),
            ("cookie com caminho incompatível", "Path=/outro", "loopback-ficticio"),
            ("cookie expirado", "Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT", "loopback-ficticio"),
            ("cookie com Max-Age=0", "Path=/; Max-Age=0", "loopback-ficticio"),
            ("cookie com valor vazio", "Path=/", ""),
        };
        foreach (var (name, attributes, value) in cookieCases)
        {
            await CheckAsync($"Loopback: {name} => 37 e nenhum download", async () =>
            {
                string dir = NewTempDir();
                try
                {
                    var (code, _, server) = await RunLoopback(InlabsLike(attributes, cookieValue: value), dir);
                    await using (server)
                    {
                        Equal(37, code, "código");
                        Assert(!server.Log.Any(l => l.Contains("/index.php", StringComparison.Ordinal)), "não deve tentar downloads");
                        Equal(0, Directory.GetFileSystemEntries(dir).Length, "nada gravado");
                    }
                }
                finally
                {
                    Cleanup(dir);
                }
            });
        }

        await CheckAsync("Loopback: redirecionamento infinito => 1 (limite de 10)", async () =>
        {
            string dir = NewTempDir();
            try
            {
                int hops = 0;
                var (code, text, server) = await RunLoopback(
                    ctx =>
                    {
                        if (ctx.Request.Url!.AbsolutePath == "/logar.php")
                        {
                            ctx.Response.AppendHeader("Set-Cookie", $"{InlabsClient.SessionCookieName}=loopback-ficticio; Path=/");
                            ctx.Response.StatusCode = 200;
                        }
                        else
                        {
                            Interlocked.Increment(ref hops);
                            Redirect(ctx, "/index.php?p=2026-01-09&dl=loop&n=" + hops);
                        }

                        return Task.CompletedTask;
                    },
                    dir);
                await using (server)
                {
                    Equal(1, code, "código");
                    Assert(hops is >= 10 and <= 12, $"número de saltos limitado (obtido {hops})");
                    Assert(text.Contains("redirecionamento", StringComparison.OrdinalIgnoreCase), "mensagem de redirecionamento");
                    Equal(0, Directory.GetFileSystemEntries(dir).Length, "nada gravado");
                }
            }
            finally
            {
                Cleanup(dir);
            }
        });

        await CheckAsync("Loopback: sem recursão nem retry em 503", async () =>
        {
            string dir = NewTempDir();
            try
            {
                var (code, _, server) = await RunLoopback(
                    ctx =>
                    {
                        if (ctx.Request.Url!.AbsolutePath == "/logar.php")
                        {
                            ctx.Response.AppendHeader("Set-Cookie", $"{InlabsClient.SessionCookieName}=loopback-ficticio; Path=/");
                            ctx.Response.StatusCode = 200;
                        }
                        else
                        {
                            ctx.Response.StatusCode = 503;
                        }

                        return Task.CompletedTask;
                    },
                    dir,
                    "DO1 DO2");
                await using (server)
                {
                    Equal(1, code, "código");
                    Equal(1, server.Log.Count(l => l.StartsWith("POST /logar.php", StringComparison.Ordinal)), "um único login");
                    Equal(1, server.Log.Count(l => l.StartsWith("GET /index.php", StringComparison.Ordinal)), "um único download");
                }
            }
            finally
            {
                Cleanup(dir);
            }
        });
    }
}
