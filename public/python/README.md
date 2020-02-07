
# INLABS

### Instruções de utilização do script em Python:

**Passo 1:** Faça o download do arquivo inlabs-auto-download-xml.py ou inlabs-auto-download-pdf.py;

**Passo 2:** Edite o arquivo alterando as informações de 'login=' (linha 4) e 'senha=' (linha 5);

**Exemplo:**

       email="joaosilva@email.com"
       senha="J0ao747$#"

 **Passo 3:** Altere se necessário as seções que deseja realizar download em 'tipo_dou' (linha 6 ou 7). Separe as seções por espaço;

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

 **Passo 4:** Execute o comando abaixo para realizar a execução do script;

      $ python -W ignore inlabs-auto-download-xml.py

 **E/OU**

       $ python -W ignore inlabs-auto-download-pdf.py

 **Sugestões** Adicione entradas no crontab para execuções periódicas de acordo com sua necessidade;
