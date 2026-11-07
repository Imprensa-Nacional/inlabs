namespace Inlabs.Download;

internal enum OutputFormat
{
    Xml,
    Pdf,
}

internal enum PdfNameProfile
{
    /// <summary>Nome remoto completo (padrão).</summary>
    Remote,

    /// <summary>Nomes locais no estilo do exemplo Python.</summary>
    Python,
}

internal sealed record ParseResult(DownloadOptions? Options, bool ShowHelp, string? Error);

/// <summary>Opções de linha de comando já validadas. Não contém credenciais.</summary>
internal sealed class DownloadOptions
{
    public static readonly string[] XmlSections = ["DO1", "DO2", "DO3", "DO1E", "DO2E", "DO3E"];
    public static readonly string[] PdfSections = ["do1", "do2", "do3"];

    public required OutputFormat Format { get; init; }
    public required IReadOnlyList<string> Sections { get; init; }
    public required string OutputDirectory { get; init; }
    public required PdfNameProfile PdfName { get; init; }

    private static readonly string[] KnownOptions = ["format", "sections", "output", "pdf-name"];

    public static ParseResult Parse(string[] args)
    {
        if (args.Contains("--help", StringComparer.Ordinal))
        {
            return new ParseResult(null, true, null);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                return Fail($"Argumento inesperado: '{arg}'.");
            }

            string name = arg[2..];
            string? value = null;
            int eq = name.IndexOf('=');
            if (eq >= 0)
            {
                value = name[(eq + 1)..];
                name = name[..eq];
            }

            if (Array.IndexOf(KnownOptions, name) < 0)
            {
                return Fail($"Opção desconhecida: '--{name}'.");
            }

            if (values.ContainsKey(name))
            {
                return Fail($"A opção '--{name}' foi informada mais de uma vez.");
            }

            if (value is null)
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return Fail($"A opção '--{name}' exige um valor.");
                }

                value = args[++i];
            }

            values[name] = value;
        }

        var format = OutputFormat.Xml;
        if (values.TryGetValue("format", out string? formatText))
        {
            switch (formatText.ToLowerInvariant())
            {
                case "xml": format = OutputFormat.Xml; break;
                case "pdf": format = OutputFormat.Pdf; break;
                default: return Fail($"Formato inválido: '{formatText}'. Use 'xml' ou 'pdf'.");
            }
        }

        var profile = PdfNameProfile.Remote;
        if (values.TryGetValue("pdf-name", out string? profileText))
        {
            if (format != OutputFormat.Pdf)
            {
                return Fail("A opção '--pdf-name' só pode ser usada com '--format pdf'.");
            }

            switch (profileText.ToLowerInvariant())
            {
                case "remote": profile = PdfNameProfile.Remote; break;
                case "python": profile = PdfNameProfile.Python; break;
                default: return Fail($"Perfil de nomes inválido: '{profileText}'. Use 'remote' ou 'python'.");
            }
        }

        string[] allowed = format == OutputFormat.Xml ? XmlSections : PdfSections;
        IReadOnlyList<string> sections = allowed;
        if (values.TryGetValue("sections", out string? sectionsText))
        {
            var chosen = new List<string>();
            string[] tokens = sectionsText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return Fail("A lista de '--sections' está vazia.");
            }

            foreach (string token in tokens)
            {
                string normalized = format == OutputFormat.Xml ? token.ToUpperInvariant() : token.ToLowerInvariant();
                if (Array.IndexOf(allowed, normalized) < 0)
                {
                    return Fail($"Seção inválida para {format.ToString().ToUpperInvariant()}: '{token}'. Valores aceitos: {string.Join(' ', allowed)}.");
                }

                if (!chosen.Contains(normalized))
                {
                    chosen.Add(normalized);
                }
            }

            sections = chosen;
        }

        string outputDirectory;
        try
        {
            if (values.TryGetValue("output", out string? outputText))
            {
                if (string.IsNullOrWhiteSpace(outputText))
                {
                    return Fail("O valor de '--output' está vazio.");
                }

                outputDirectory = Path.GetFullPath(outputText);
            }
            else
            {
                outputDirectory = Directory.GetCurrentDirectory();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Fail($"Diretório de saída inválido: {ex.Message}");
        }

        return new ParseResult(
            new DownloadOptions
            {
                Format = format,
                Sections = sections,
                OutputDirectory = outputDirectory,
                PdfName = profile,
            },
            false,
            null);
    }

    private static ParseResult Fail(string message) => new(null, false, message);
}
