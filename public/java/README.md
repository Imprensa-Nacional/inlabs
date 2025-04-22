# INLABS Downloader - Versão Java

Este projeto contém scripts em Java para automatizar o download dos arquivos do Diário Oficial da União (DOU) em formato PDF e XML.

## Requisitos

- Java 11 ou superior
- Maven 3.6 ou superior

## Instalação

1. Clone o repositório
2. Navegue até o diretório `public/java`
3. Execute o comando Maven para construir os executáveis:

```bash
mvn clean package
```

## Configuração

Antes de executar os scripts, você precisa configurar suas credenciais do INLABS. Existem duas maneiras:

### 1. Via Argumentos de Linha de Comando (Recomendado)

Execute os scripts passando seu email e senha como argumentos:

```bash
java -jar target/inlabs-downloader-pdf.jar seu.email@dominio.com sua_senha
```

### 2. Via Código Fonte

Se preferir, você pode editar diretamente os arquivos fonte:

1. Abra o arquivo `src/main/java/br/gov/inlabs/InlabsDownloaderPDF.java`
2. Localize a linha no método `main`:
```java
String email = args[0];
String senha = args[1];
```
3. Substitua por:
```java
String email = "seu.email@dominio.com";
String senha = "sua_senha";
```

Repita o mesmo processo para o arquivo `InlabsDownloaderXML.java` se desejar baixar arquivos XML.

## Uso

### Download de PDFs

Para baixar os arquivos em PDF, execute:

```bash
java -jar target/inlabs-downloader-pdf.jar seu.email@dominio.com sua_senha
```

### Download de XMLs

Para baixar os arquivos em XML, execute:

```bash
java -jar target/inlabs-downloader-xml.jar seu.email@dominio.com sua_senha
```

## Funcionalidades

- Download automático dos arquivos do DOU
- Suporte para as seções DO1, DO2 e DO3
- Tratamento de erros e feedback ao usuário
- Nomes de arquivos padronizados com a data

## Exemplo de Saída

```
Aguarde Download...
Arquivo 2024_04_21_ASSINADO_do1.pdf salvo.
Arquivo 2024_04_21_ASSINADO_do2.pdf salvo.
Arquivo 2024_04_21_ASSINADO_do3.pdf salvo.
Aplicação encerrada
```

## Contribuindo

Sinta-se à vontade para contribuir com melhorias no código. Algumas sugestões:

- Adicionar suporte para datas específicas
- Implementar download paralelo
- Adicionar validação de arquivos
- Melhorar o tratamento de erros

## Licença

Este projeto está sob a mesma licença do projeto INLABS original. 