# INLABS - Download automático em C# (.NET 9)

Aplicação de console em C# que baixa os arquivos do DOU disponibilizados no [INLABS](https://inlabs.in.gov.br), com a mesma finalidade dos exemplos oficiais em [Python](../python/README.md) e [Bash](../bash/README.md):

- **XML**: arquivos `.zip` das seções `DO1`, `DO2`, `DO3` e das edições extras `DO1E`, `DO2E`, `DO3E`. Os ZIPs **não são extraídos** nem interpretados.
- **PDF**: PDF assinado das seções `do1`, `do2`, `do3`, incluindo as edições extras (letras `A` a `Z`).

Usa somente a biblioteca padrão do .NET: não há pacotes NuGet na aplicação.

> O INLABS não publica uma API versionada. O login por formulário, o cookie de sessão, o cabeçalho `origem` e os nomes de arquivo foram reproduzidos a partir dos exemplos oficiais deste repositório e podem mudar sem aviso.

## Requisitos

- Conta válida no INLABS.
- [.NET 9 SDK](https://dotnet.microsoft.com/download) para compilar e usar `dotnet run`. Para executar uma versão já compilada (dependente de framework), basta o runtime .NET 9.

## Credenciais

O e-mail e a senha são lidos **apenas de variáveis de ambiente**. Não há opção de linha de comando para senha, nem leitura de arquivo `.env`.

PowerShell:

```powershell
$env:INLABS_EMAIL = "seu-email@exemplo.com"
$env:INLABS_PASSWORD = Read-Host "Senha do INLABS" -MaskInput
```

Bash:

```bash
export INLABS_EMAIL="seu-email@exemplo.com"
read -r -s -p "Senha do INLABS: " INLABS_PASSWORD && export INLABS_PASSWORD
```

Evite digitar a senha diretamente em comandos (ela ficaria no histórico do terminal) e nunca grave credenciais em arquivos versionados. Lembre-se de que as variáveis de ambiente também precisam ser protegidas no ambiente de execução.

## Uso

```text
dotnet run --project public/csharp/Inlabs.Download.csproj -- [opções]
```

| Opção | Descrição | Padrão |
| --- | --- | --- |
| `--format xml\|pdf` | Formato dos arquivos | `xml` |
| `--sections "<lista>"` | Seções separadas por espaço, em **um único argumento** | todas do formato |
| `--output <diretório>` | Diretório de saída (criado se não existir) | diretório atual |
| `--pdf-name remote\|python` | Nome dos PDFs salvos (somente com `--format pdf`) | `remote` |
| `--help` | Exibe a ajuda (não exige credenciais nem acessa a rede) | |

Seções aceitas: `DO1 DO2 DO3 DO1E DO2E DO3E` para XML e `do1 do2 do3` para PDF. A caixa é normalizada (por exemplo, `do1` em XML vira `DO1`); entradas repetidas são ignoradas e entradas inválidas, vazias ou desconhecidas interrompem a execução antes de qualquer acesso à rede.

Exemplos (todos com placeholders; as credenciais vêm do ambiente):

```bash
# Ajuda
dotnet run --project public/csharp/Inlabs.Download.csproj -- --help

# XML completo: DO1 DO2 DO3 DO1E DO2E DO3E
dotnet run --project public/csharp/Inlabs.Download.csproj -- --format xml

# Somente edições extras em XML
dotnet run --project public/csharp/Inlabs.Download.csproj -- --format xml --sections "DO1E DO2E DO3E"

# PDF da seção 1 (regular + extras A-Z), em ./downloads
dotnet run --project public/csharp/Inlabs.Download.csproj -- --format pdf --sections "do1" --output ./downloads

# PDF da seção 3 com nomes no estilo do exemplo Python
dotnet run --project public/csharp/Inlabs.Download.csproj -- --format pdf --sections "do3" --pdf-name python
```

Observação: `dotnet run` pode executar um *restore* implícito, mesmo a aplicação não tendo pacotes externos.

## Como funciona

1. Captura **uma única vez** a data **local** da máquina (a data não pode ser informada por parâmetro). Em servidores com outro fuso, configure o fuso do ambiente de execução.
2. Faz `POST https://inlabs.in.gov.br/logar.php` com os campos de formulário `email` e `password` (codificados corretamente, inclusive com caracteres especiais), cabeçalho `origem: 736372697074` e o `Accept` usado no exemplo Python.
3. Mantém o cookie `inlabs_session_cookie` em memória (`CookieContainer`) durante a execução; nada é gravado em disco. Se, após o login, não houver cookie de sessão aplicável, a execução termina com código 37.
4. Para cada arquivo, faz `GET https://inlabs.in.gov.br/index.php?p={aaaa-MM-dd}&dl={nome-remoto}` com o cabeçalho `origem`, seguindo até 10 redirecionamentos e com validação TLS normal.

Nomes remotos:

| Tipo | Nome remoto |
| --- | --- |
| XML regular e extra | `{aaaa-MM-dd}-{secao}.zip`, por exemplo `2026-01-09-DO1E.zip` |
| PDF regular | `{aaaa_MM_dd}_ASSINADO_{secao}.pdf` |
| PDF extra | `{aaaa_MM_dd}_ASSINADO_{secao}_extra_{letra}.pdf` |

Para PDF, cada seção solicita o regular e as 26 letras de extras, na ordem `A B C D E F G H I J K L M N O P Q R S T U V X W Y Z` (assim como no exemplo Bash), **sem** interromper a busca no primeiro 404: são 27 solicitações por seção (81 para as três). É normal que a maioria dos extras não exista.

### Nomes dos arquivos salvos

- XML: igual ao nome remoto.
- PDF com `--pdf-name remote` (padrão): nome remoto completo, **incluindo `.pdf` nos extras**. Isto difere do exemplo Bash, que salva os extras sem a extensão.
- PDF com `--pdf-name python`: regular `{aaaa-MM-dd}-{secao}.pdf` (como o exemplo Python) e extra `{aaaa-MM-dd}-{secao}_extra_{letra}.pdf` (o exemplo Python não baixa extras; este nome é uma extensão definida aqui).

### Gravação segura

- O corpo da resposta é gravado por *streaming* em um arquivo temporário (`.nome.<guid>.tmp`) no próprio diretório de destino.
- Um arquivo existente só é substituído depois que o download termina e passa nas verificações. Em caso de falha ou cancelamento, o arquivo anterior é mantido e o temporário é removido.
- Respostas HTML, vazias ou sem assinatura compatível (`%PDF-` para PDF; cabeçalho ZIP para XML) **não são salvas**. Isso não equivale a validar o ZIP inteiro nem a assinatura digital do PDF.
- Os bytes são salvos sem alteração (a descompressão HTTP `gzip`/`deflate`/`br` é transparente e não extrai os ZIPs).
- Cada operação HTTP (cabeçalhos e corpo) tem limite total de 300 segundos.
- Execuções simultâneas no mesmo diretório podem sobrescrever o mesmo arquivo; não agende execuções sobrepostas.

## Códigos de saída

| Código | Situação |
| --- | --- |
| `0` | Concluído, inclusive quando todos os arquivos estão ausentes (HTTP 404) |
| `1` | Falha operacional: DNS, conexão, TLS, tempo esgotado, redirecionamentos em excesso, HTTP 429/5xx ou inesperado, conteúdo incompatível, erro de disco |
| `2` | Argumentos ou credenciais ausentes/inválidos (nenhum acesso à rede) |
| `37` | Sessão inválida ou acesso negado: login sem cookie de sessão, HTTP 401/403 |
| `130` | Cancelado com Ctrl+C |

HTTP 404 em um download é registrado como "Ausente" e a execução continua. Qualquer outra falha encerra a execução (sem *retry* nem novo login automático); os arquivos já salvos permanecem. O resumo final informa quantos arquivos foram salvos, quantos estavam ausentes e quantas falhas ocorreram.

## Logs

Os logs são escritos em português na saída padrão (andamento) e na saída de erro (falhas), com data e hora. Eles **nunca** incluem senha, corpo do login, valor de cookie ou conteúdo HTML das respostas.

## Disponibilidade dos dados

Conforme o [README principal](../../README.md), o INLABS mantém os dados dos últimos quatro meses. Fins de semana, feriados e edições extras podem não ter arquivos; isso resulta em 404 e não indica defeito.

## Agendamento

A aplicação não possui agendador próprio. Use o agendador do sistema, com caminhos absolutos, diretório de trabalho e variáveis de ambiente definidos para o usuário que executará a tarefa.

Cron (Linux), com a aplicação publicada previamente (`dotnet publish public/csharp/Inlabs.Download.csproj -c Release -o /opt/inlabs`):

```cron
0 7 * * * . /etc/inlabs.env && cd /var/dados/inlabs && dotnet /opt/inlabs/Inlabs.Download.dll --format xml >> /var/log/inlabs.log 2>&1
```

(`/etc/inlabs.env` deve exportar `INLABS_EMAIL` e `INLABS_PASSWORD` e ter permissão restrita ao usuário.)

Agendador de Tarefas do Windows: crie uma tarefa que execute `dotnet.exe` com o argumento `C:\inlabs\Inlabs.Download.dll --format xml --output D:\inlabs`, defina o diretório "Iniciar em" e configure as variáveis de ambiente `INLABS_EMAIL` e `INLABS_PASSWORD` para o usuário da tarefa.

## Visual Studio Code

A pasta `public/csharp` já traz a configuração em `.vscode/`. Para usar:

1. Instale o [.NET SDK](https://dotnet.microsoft.com/download) e a extensão **C#** (`ms-dotnettools.csharp`), recomendada automaticamente pelo VS Code.
2. Abra **a pasta `public/csharp`** (`code public/csharp`), e não a raiz do repositório. Os caminhos de `launch.json` e `tasks.json` são relativos a ela.
3. Em **Executar e Depurar** (`Ctrl+Shift+D`), escolha uma configuração e pressione `F5`:

| Configuração | O que faz |
| --- | --- |
| Ajuda (--help) | Exibe a ajuda; não precisa de credenciais |
| Baixar XML | Pede e-mail, senha e seções; salva em `./downloads` |
| Baixar PDF | Pede e-mail, senha e seções; salva em `./downloads` |
| Verificações offline | Executa as verificações offline, sem credenciais e sem acesso ao INLABS |

O e-mail e a senha são solicitados a cada execução (a senha é digitada de forma oculta) e passados ao processo como variáveis de ambiente. Nenhuma credencial é gravada nos arquivos do projeto. A pasta `downloads/` está no `.gitignore`.

Pelo menu **Terminal > Executar Tarefa** (ou `Ctrl+Shift+B` para compilar) estão disponíveis as tarefas `build`, `build-checks` e `verificações offline`.

> As configurações apontam para `bin/Debug/net9.0`. Se você mudar o `TargetFramework` dos `.csproj`, atualize também os caminhos em `.vscode/launch.json`.

## Compilar e verificar

```bash
dotnet build public/csharp/Inlabs.Download.csproj
```

Verificações offline (não usam credenciais reais nem acessam o INLABS):

```bash
dotnet run --project public/csharp/tests/Inlabs.Download.Checks.csproj
```

O programa termina com código diferente de zero se alguma verificação falhar. Elas cobrem: planejamento de URLs e nomes (XML e PDF, extras, perfis de nomes), validação de argumentos, ausência de requisições HTTP em ajuda/erros de configuração, formulário de login e cabeçalhos, tratamento de status HTTP, rejeição de conteúdo incompatível, preservação de arquivos em falhas, timeout, cancelamento e, com o `HttpClientHandler` real contra um servidor local de *loopback* com cookies fictícios, cookies, redirecionamentos e descompressão. Essas verificações **não** substituem um teste real com uma conta INLABS.

## Solução de problemas

| Sintoma | O que verificar |
| --- | --- |
| Código `2` | Argumentos inválidos ou variáveis `INLABS_EMAIL`/`INLABS_PASSWORD` ausentes ou vazias. |
| Código `37` | Credenciais incorretas, conta sem acesso ou bloqueio (HTTP 403) pelo serviço; um 403 não prova, por si só, senha incorreta. |
| Código `1` com "Falha de comunicação" | Rede, DNS, proxy, TLS ou tempo esgotado. |
| Código `1` com HTTP 429/5xx | Limite de requisições ou indisponibilidade; tente mais tarde. Problemas do serviço devem ser tratados com a Central de Atendimento da Imprensa Nacional, conforme o README principal. |
| Código `1` com "Resposta HTML inesperada" ou "assinatura" | O serviço devolveu uma página no lugar do arquivo (por exemplo, sessão inválida). Nada foi salvo. |
| Código `1` com "Falha ao gravar em disco" | Permissão, espaço em disco ou caminho de saída. |
| Todos os itens "Ausente" | Data sem publicação ou fora da janela de disponibilidade. |

Defeitos desta implementação devem ser relatados por *issue* no repositório.
