using System.Globalization;

namespace Inlabs.Download;

/// <summary>Um arquivo a ser solicitado ao INLABS.</summary>
internal sealed record DownloadCandidate(
    OutputFormat Format,
    string Section,
    char? ExtraLetter,
    DateOnly Date,
    string RemoteName,
    string LocalName)
{
    /// <summary>Query string da URL de download: <c>p=yyyy-MM-dd&amp;dl=nome-remoto</c>.</summary>
    public string Query =>
        $"p={Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&dl={Uri.EscapeDataString(RemoteName)}";

    public Uri GetUri(Uri baseUri) => new(baseUri, "index.php?" + Query);

    public string Description =>
        Format == OutputFormat.Xml
            ? $"XML {Section}"
            : ExtraLetter is null ? $"PDF {Section}" : $"PDF {Section} extra {ExtraLetter}";
}

/// <summary>Geração pura (sem I/O) da lista ordenada de downloads.</summary>
internal static class DownloadPlan
{
    /// <summary>Letras dos extras, na ordem literal do exemplo Bash (X antes de W).</summary>
    public const string ExtraLetters = "ABCDEFGHIJKLMNOPQRSTUVXWYZ";

    public static IReadOnlyList<DownloadCandidate> Build(
        DateOnly date,
        OutputFormat format,
        IEnumerable<string> sections,
        PdfNameProfile profile = PdfNameProfile.Remote)
    {
        var inv = CultureInfo.InvariantCulture;
        string dashed = date.ToString("yyyy-MM-dd", inv);
        string underscored = date.ToString("yyyy_MM_dd", inv);
        var list = new List<DownloadCandidate>();

        foreach (string section in sections)
        {
            if (format == OutputFormat.Xml)
            {
                string name = $"{dashed}-{section}.zip";
                list.Add(new DownloadCandidate(format, section, null, date, name, name));
                continue;
            }

            string regular = $"{underscored}_ASSINADO_{section}.pdf";
            string regularLocal = profile == PdfNameProfile.Python ? $"{dashed}-{section}.pdf" : regular;
            list.Add(new DownloadCandidate(format, section, null, date, regular, regularLocal));

            foreach (char letter in ExtraLetters)
            {
                string extra = $"{underscored}_ASSINADO_{section}_extra_{letter}.pdf";
                string extraLocal = profile == PdfNameProfile.Python ? $"{dashed}-{section}_extra_{letter}.pdf" : extra;
                list.Add(new DownloadCandidate(format, section, letter, date, extra, extraLocal));
            }
        }

        return list;
    }
}
