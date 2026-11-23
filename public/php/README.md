
# INLABS

### Instruções de utilização do script em PHP:

**Requisitos:** PHP 8.1 ou superior com a extensão cURL habilitada (`php -m | grep curl`), ou o Docker (veja [Usando com Docker](#usando-com-docker)).

**Passo 1:** Faça o download do arquivo inlabs-auto-download-xml.php ou inlabs-auto-download-pdf.php;

**Passo 2:** Edite o arquivo alterando as informações de '$login =' (linha 10) e '$senha =' (linha 11);

**Exemplo:**

       $login = "joaosilva@email.com";
       $senha = "J0ao747$#";

 **Passo 3:** Altere se necessário as seções que deseja realizar download em '$tipo_dou' (linha 14). Separe as seções por espaço;

 **Exemplo 1 XML:**

       $tipo_dou = "DO1 DO2 DO3";

 **Exemplo 2 XML:**

       $tipo_dou = "DO1E DO2E DO3E";

 **Exemplo 3 XML:**

       $tipo_dou = "DO1 DO1E DO2 DO2E";

 **Exemplo 1 PDF:**

       $tipo_dou = "do1 do2 do3";

 **Exemplo 2 PDF:**

       $tipo_dou = "do1 do2";

 **Exemplo 3 PDF:**

       $tipo_dou = "do3";

 No script de PDF, as edições extras (`_extra_A`, `_extra_B`...) de cada seção são baixadas automaticamente.

 **Passo 4 (opcional):** Altere a pasta onde os arquivos serão salvos em '$pasta_destino' (linha 17). Por padrão, é a mesma pasta do script;

 **Passo 5:** Execute o comando abaixo para realizar a execução do script;

      $ php inlabs-auto-download-xml.php

 **E/OU**

      $ php inlabs-auto-download-pdf.php

 Para baixar a edição de outra data (o INLABS disponibiliza os últimos 4 meses), informe-a no formato AAAA-MM-DD:

      $ php inlabs-auto-download-xml.php 2026-11-20

 **Códigos de saída:** `0` sucesso, `1` erro de execução/conexão, `37` falha de autenticação (mesmo código do script em Python).

 **Sugestões** Adicione entradas no crontab para execuções periódicas de acordo com sua necessidade;

      $ crontab -e

      01 10 * * * php /home/user/inlabs-auto-download-xml.php >> /tmp/inlabs-auto-download-xml.txt

 No Windows, utilize o Agendador de Tarefas apontando para `php.exe` com o caminho do script como argumento.

### Variáveis de ambiente

Em vez de editar o arquivo, você pode informar as configurações por variáveis de ambiente. Quando definidas, elas substituem os valores escritos no script:

| Variável            | Exemplo                 | Substitui                         |
|---------------------|-------------------------|-----------------------------------|
| `INLABS_EMAIL`      | `joaosilva@email.com`   | `$login`                          |
| `INLABS_SENHA`      | `J0ao747$#`             | `$senha`                          |
| `INLABS_SECOES_XML` | `DO1 DO2 DO1E`          | `$tipo_dou` do script de XML      |
| `INLABS_SECOES_PDF` | `do1 do3`               | `$tipo_dou` do script de PDF      |
| `INLABS_DESTINO`    | `/home/user/dou`        | `$pasta_destino`                  |

### Usando com Docker

Não precisa ter o PHP instalado: basta ter o Docker.

 **Passo 1:** Gere a imagem a partir desta pasta;

      $ docker build -t inlabs-php .

 **Passo 2:** Crie um arquivo `inlabs.env` com suas credenciais. Assim a senha não fica no histórico do terminal;

      INLABS_EMAIL=joaosilva@email.com
      INLABS_SENHA=J0ao747$#

 **Passo 3:** Execute informando o tipo (`xml` ou `pdf`) e, se quiser, a data. Os arquivos são salvos na pasta montada em `/dados`;

      $ docker run --rm --env-file inlabs.env -v "$(pwd)/dou:/dados" inlabs-php xml

      $ docker run --rm --env-file inlabs.env -v "$(pwd)/dou:/dados" inlabs-php pdf 2026-11-20

      $ docker run --rm --env-file inlabs.env -e INLABS_SECOES_XML="DO1 DO1E" -v "$(pwd)/dou:/dados" inlabs-php xml

 O container roda com um usuário sem privilégios (UID 1000). No Linux, se a pasta de destino pertencer a outro usuário, acrescente `--user "$(id -u):$(id -g)"` ao comando.

 **Sugestão:** agende no crontab;

      01 10 * * * docker run --rm --env-file /home/user/inlabs.env -v /home/user/dou:/dados inlabs-php xml >> /tmp/inlabs-auto-download-xml.txt
