package br.gov.inlabs;

import java.io.FileOutputStream;
import java.io.IOException;
import java.net.URI;
import java.net.http.*;
import java.time.LocalDate;
import java.time.format.DateTimeFormatter;
import java.util.List;

public class InlabsDownloaderPDF {
    private static final String LOGIN_URL = "https://inlabs.in.gov.br/logar.php";
    private static final String DOWNLOAD_URL = "https://inlabs.in.gov.br/index.php?p=";
    private static final List<String> TIPOS_DOU = List.of("do1", "do2", "do3");

    private final String email;
    private final String senha;
    private final HttpClient client;
    private String sessionCookie;

    public InlabsDownloaderPDF(String email, String senha) {
        this.email = email;
        this.senha = senha;
        this.client = HttpClient.newBuilder()
                .followRedirects(HttpClient.Redirect.ALWAYS)
                .build();
    }

    public void loginAndDownload() throws IOException, InterruptedException {
        var formData = "email=" + email + "&password=" + senha;

        var request = HttpRequest.newBuilder()
                .uri(URI.create(LOGIN_URL))
                .header("Content-Type", "application/x-www-form-urlencoded")
                .POST(HttpRequest.BodyPublishers.ofString(formData))
                .build();

        var response = client.send(request, HttpResponse.BodyHandlers.ofString());

        var cookieHeader = response.headers().firstValue("Set-Cookie");
        if (cookieHeader.isEmpty()) throw new RuntimeException("Login falhou, credenciais inválidas");

        sessionCookie = cookieHeader.get().split(";")[0].split("=")[1];
        baixarArquivos();
    }

    private void baixarArquivos() throws IOException, InterruptedException {
        var hoje = LocalDate.now();
        var formatter = DateTimeFormatter.ofPattern("yyyy-MM-dd");
        var dataCompleta = hoje.format(formatter);
        var ano = String.valueOf(hoje.getYear());
        var mes = String.format("%02d", hoje.getMonthValue());
        var dia = String.format("%02d", hoje.getDayOfMonth());

        for (var douSecao : TIPOS_DOU) {
            var nomeArquivo = String.format("%s_%s_%s_ASSINADO_%s.pdf", ano, mes, dia, douSecao);
            var url = DOWNLOAD_URL + dataCompleta + "&dl=" + nomeArquivo;

            var request = HttpRequest.newBuilder()
                    .uri(URI.create(url))
                    .header("Cookie", "inlabs_session_cookie=" + sessionCookie)
                    .header("origem", "736372697074")
                    .GET()
                    .build();

            var response = client.send(request, HttpResponse.BodyHandlers.ofByteArray());

            if (response.statusCode() == 200) {
                try (var fos = new FileOutputStream(dataCompleta + "-" + douSecao + ".pdf")) {
                    fos.write(response.body());
                    System.out.println("Arquivo salvo: " + nomeArquivo);
                }
            } else if (response.statusCode() == 404) {
                System.out.println("Não encontrado: " + nomeArquivo);
            }
        }
    }

    public static void main(String[] args) {
        if (args.length != 2) {
            System.out.println("Uso: java -jar inlabs-downloader-pdf.jar <email> <senha>");
            System.exit(1);
        }

        try {
            new InlabsDownloaderPDF(args[0], args[1]).loginAndDownload();
            System.out.println("Download finalizado");
            System.exit(0);
        } catch (Exception e) {
            System.err.println("Erro: " + e.getMessage());
            System.exit(1);
        }
    }
}