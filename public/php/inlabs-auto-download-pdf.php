<?php
/**
 * INLABS - Download automático dos arquivos PDF do Diário Oficial da União.
 *
 * Requisitos: PHP 8.1+ com a extensão cURL habilitada.
 * Uso:        php inlabs-auto-download-pdf.php [AAAA-MM-DD]
 *             (sem data, baixa a edição do dia atual)
 */

$login = "email@dominio.com";
$senha = "minha_senha";

// Tipos de Diários Oficiais da União permitidos: do1 do2 do3 (Contempla as edições extras)
$tipo_dou = "do1 do2 do3";

// Pasta onde os arquivos serão salvos (padrão: pasta atual)
$pasta_destino = __DIR__;

## Altere daqui para baixo por sua conta e risco ##

$url_login = "https://inlabs.in.gov.br/logar.php";
$url_download = "https://inlabs.in.gov.br/index.php";
$origem = "736372697074";
$tentativas = 3;

// Variáveis de ambiente, quando definidas, substituem os valores acima (útil no Docker e no cron)
$login = getenv("INLABS_EMAIL") ?: $login;
$senha = getenv("INLABS_SENHA") ?: $senha;
$tipo_dou = getenv("INLABS_SECOES_PDF") ?: $tipo_dou;
$pasta_destino = getenv("INLABS_DESTINO") ?: $pasta_destino;

// O DOU é publicado no horário de Brasília
date_default_timezone_set("America/Sao_Paulo");

if (!extension_loaded('curl')) {
    fwrite(STDERR, "A extensão cURL do PHP não está habilitada.\n");
    exit(1);
}

$data = $argv[1] ?? date('Y-m-d');
if (!preg_match('/^\d{4}-\d{2}-\d{2}$/', $data) || !strtotime($data)) {
    fwrite(STDERR, "Data inválida: $data (use o formato AAAA-MM-DD)\n");
    exit(1);
}
[$ano, $mes, $dia] = explode("-", $data);

if (!is_dir($pasta_destino) && !mkdir($pasta_destino, 0775, true)) {
    fwrite(STDERR, "Não foi possível criar a pasta de destino: $pasta_destino\n");
    exit(1);
}

$ch = curl_init();
curl_setopt_array($ch, [
    CURLOPT_COOKIEFILE => "", // Mantém os cookies da sessão em memória
    CURLOPT_FOLLOWLOCATION => true,
    CURLOPT_CONNECTTIMEOUT => 30,
    CURLOPT_TIMEOUT => 300,
    CURLOPT_HTTPHEADER => ["origem: $origem"],
]);

// LOGIN
curl_setopt_array($ch, [
    CURLOPT_URL => $url_login,
    CURLOPT_POST => true,
    CURLOPT_POSTFIELDS => http_build_query(["email" => $login, "password" => $senha]),
    CURLOPT_RETURNTRANSFER => true,
]);

for ($i = 1; $i <= $tentativas; $i++) {
    if (curl_exec($ch) !== false) {
        break;
    }
    fwrite(STDERR, "Falha de conexão ($i/$tentativas): " . curl_error($ch) . "\n");
    if ($i === $tentativas) {
        exit(1);
    }
    sleep(5);
}

$cookies = implode("\n", curl_getinfo($ch, CURLINFO_COOKIELIST));
if (!str_contains($cookies, "inlabs_session_cookie")) {
    fwrite(STDERR, "Falha ao obter cookie. Verifique suas credenciais\n");
    exit(37);
}
echo "Login realizado com sucesso\n";

// DOWNLOAD
curl_setopt($ch, CURLOPT_HTTPGET, true);

/**
 * Baixa um arquivo e retorna o código HTTP da resposta.
 * O arquivo só é mantido no disco quando a resposta é 200.
 */
function baixar($ch, string $url_download, string $data, string $nome_arquivo, string $pasta_destino): int
{
    $caminho = rtrim($pasta_destino, "/\\") . DIRECTORY_SEPARATOR . $nome_arquivo;
    $temporario = "$caminho.part";

    $arquivo = fopen($temporario, "wb");
    if ($arquivo === false) {
        fwrite(STDERR, "Não foi possível gravar em $temporario. Verifique as permissões.\n");
        exit(1);
    }
    curl_setopt_array($ch, [
        CURLOPT_URL => $url_download . "?" . http_build_query(["p" => $data, "dl" => $nome_arquivo]),
        CURLOPT_FILE => $arquivo,
    ]);
    $ok = curl_exec($ch);
    $status = curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
    fclose($arquivo);

    if ($ok && $status === 200) {
        rename($temporario, $caminho);
        echo "Arquivo $nome_arquivo salvo.\n";
        return 200;
    }

    unlink($temporario);
    if ($status !== 404) {
        echo "Erro ao baixar $nome_arquivo (HTTP $status) " . curl_error($ch) . "\n";
    }
    return $status;
}

foreach (preg_split('/\s+/', trim($tipo_dou)) as $dou_secao) {
    echo "Aguarde Download...\n";

    // Edição normal
    $nome_arquivo = "{$ano}_{$mes}_{$dia}_ASSINADO_{$dou_secao}.pdf";
    if (baixar($ch, $url_download, $data, $nome_arquivo, $pasta_destino) === 404) {
        echo "Arquivo não encontrado: $nome_arquivo\n";
    }

    // Edições extras (A, B, C...). Para na primeira que não existir.
    foreach (range('A', 'Z') as $seq) {
        $nome_extra = "{$ano}_{$mes}_{$dia}_ASSINADO_{$dou_secao}_extra_{$seq}.pdf";
        if (baixar($ch, $url_download, $data, $nome_extra, $pasta_destino) !== 200) {
            break;
        }
    }
}

curl_close($ch);
echo "Aplicação encerrada\n";
exit(0);
