
# INLABS

### Instruções de utilização do script em Bash:

**Passo 1:** Faça o download do arquivo inlabs-auto-download-xml.sh ou inlabs-auto-download-pdf.sh;

**Passo 2:** Edite o arquivo alterando as informações de 'email=' (linha 9) e 'senha=' (linha 10);

**Exemplo:**

       email="joaosilva@email.com"
       senha="J0ao747$#"

 **Passo 3:** Altere se necessário as seções que deseja realizar download em 'tipo_dou' (linha 13);

 **Exemplo 1 XML:**

       tipo_dou="DO1 DO2 DO3"

 **Exemplo 2 XML:**

       tipo_dou="DO1E DO2E DO3E"

 **Exemplo 3 XML:**

      tipo_dou="DO1 DO1E DO2 DO2E"

 **Exemplo 1 PDF:**

       tipo_dou="do1 do2 do3"

 **Exemplo 2 PDF:**

       tipo_dou="do1 do2"

 **Exemplo 3 PDF:**

       tipo_dou="do3"

 **Passo 4:** Execute o comando abaixo para conceder permissão de execução no script;

      $ chmod +x inlabs-auto-download-xml.sh

 **E/OU**

       $ chmod +x inlabs-auto-download-pdf.sh

 **Passo 5:** Adicione uma entrada no crontab para execuções periódicas de acordo com sua necessidade;

       $ crontab -e

      01 10 * * * sh /home/user/inlabs-auto-download-xml.sh >> /tmp/inlabs-auto-download-xml.txt

 **E/OU**

    $ crontab -e


     01 10 * * * sh /home/user/inlabs-auto-download-pdf.sh >> /tmp/inlabs-auto-download-pdf.txt

 **Explicação da crontab**

        minuto hora dia mes ano                              comando
          01    10   *   *   *   sh /home/user/inlabs-auto-download-xml.sh >> /tmp/inlabs-auto-download-xml.txt
