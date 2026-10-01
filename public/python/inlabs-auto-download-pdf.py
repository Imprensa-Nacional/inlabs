from datetime import date
import requests
import argparse

parser = argparse.ArgumentParser()
parser.add_argument("--data",help="data (dd-mm-YYYY) para as consultas. Omissão ou data inválida sinaliza data de hoje")
data_arg = parser.parse_args()

#login =
#senha =

## Tipos de Diários Oficiais da União permitidos: do1 do2 do3 (Contempla as edições extras) ##
tipo_dou="do1 do2 do3"

url_login = "https://inlabs.in.gov.br/logar.php"
url_download = "https://inlabs.in.gov.br/index.php?p="

payload = {"email" : login, "password" : senha}
headers = {
    "Content-Type": "application/x-www-form-urlencoded",
    "Accept" : "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
    }
s = requests.Session()

def usarHoje():
    ano = date.today().strftime("%Y")
    mes = date.today().strftime("%m")
    dia = date.today().strftime("%d")
    data_completa = ano + "-" + mes + "-" + dia
    return ano, mes, dia, data_completa

def download():
    cookie = str()
    cookie = s.cookies.get('inlabs_session_cookie')

    if(not cookie):
        print("Falha ao obter cookie. Verifique suas credenciais")
        exit(37)

    data_manual = data_arg.data

    if(data_manual):
        print("Atenção: conforme testado até 11/03/2026, esta opção permitia apenas consulta de arquivos dos últimos quatro meses anteriores a hoje, "+date.today().strftime("%d/%m/%Y")+". Aconselha-se que o usuário verifique https://inlabs.in.gov.br/index.php?p= para visualizar os arquivos disponíveis.")
        ano = data_manual[6:]
        mes = data_manual[3:5]
        dia = data_manual[:2]
        data_completa = ano+"-"+mes+"-"+dia

        try:
            data_verif = date.fromisoformat(data_completa)
        except:
            print("Data inválida ou não fornecida. Recorrendo à data de hoje...")
            ano, mes, dia, data_completa = usarHoje()
    else:
        ano, mes, dia, data_completa = usarHoje()

    print("A data para consulta é "+data_completa+".")

    #Download inicial
    for dou_secao in tipo_dou.split(' '):
        print("Aguarde Download...")
        url_arquivo = url_download + data_completa + "&dl=" + ano + "_" + mes + "_" + dia + "_ASSINADO_" + dou_secao + ".pdf"
        cabecalho_arquivo = {'Cookie': 'inlabs_session_cookie=' + cookie, 'origem': '736372697074'}
        response_arquivo = s.request("GET", url_arquivo, headers = cabecalho_arquivo)
        if response_arquivo.status_code == 200:
            with open(data_completa + "-" + dou_secao + ".pdf", "wb") as f:
                f.write(response_arquivo.content)
                print("Arquivo %s salvo." % (ano + "_" + mes + "_" + dia + "_ASSINADO_" + dou_secao + ".pdf"))
                del response_arquivo
                del f
        elif response_arquivo.status_code == 404:
                print("Arquivo não encontrado: %s" % (ano + "_" + mes + "_" + dia + "_ASSINADO_" + dou_secao + ".pdf"))
    print("Aplicação encerrada")
    exit(0)

def login():
    try:
        response = s.request("POST", url_login, data=payload, headers=headers)
        download()
    except requests.exceptions.ConnectionError:
        login()

login()
