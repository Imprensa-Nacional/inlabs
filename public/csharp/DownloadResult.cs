namespace Inlabs.Download;

internal enum DownloadStatus
{
    /// <summary>Operação concluída (login efetuado ou arquivo salvo).</summary>
    Success,

    /// <summary>HTTP 404: arquivo não publicado para a data/seção. Não é erro.</summary>
    Missing,

    /// <summary>Sessão inválida ou acesso negado (login sem cookie, HTTP 401/403).</summary>
    AuthFailure,

    /// <summary>Falha de rede, HTTP inesperado, conteúdo incompatível ou disco.</summary>
    Failure,

    /// <summary>Cancelamento pelo usuário (Ctrl+C).</summary>
    Cancelled,
}

internal sealed record DownloadResult(DownloadStatus Status, string Message, string? SavedPath = null)
{
    public static DownloadResult Ok(string message, string? savedPath = null) => new(DownloadStatus.Success, message, savedPath);
    public static DownloadResult NotFound(string message) => new(DownloadStatus.Missing, message);
    public static DownloadResult Auth(string message) => new(DownloadStatus.AuthFailure, message);
    public static DownloadResult Fail(string message) => new(DownloadStatus.Failure, message);
    public static DownloadResult Cancel() => new(DownloadStatus.Cancelled, "Operação cancelada.");
}
