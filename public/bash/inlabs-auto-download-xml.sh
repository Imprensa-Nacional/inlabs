#!/bin/bash
######################################################################
## INLABS 							    ##
## Script desenvolvido em bash para download automático de arquivos ##
## Autor: https://github.com/Iakim 				    ##
## A simplicidade é o último grau de sofisticação 		    ##
######################################################################

email="mail@mail.com"
senha="123456"

## Tipos de Diários Oficiais da União permitidos: DO1 DO2 DO3 DO1E DO2E DO3E ##
tipo_dou="DO1 DO2 DO3 DO1E DO2E DO3E"

## Altere daqui para baixo por sua conta e risco ##
dia=`date +%d`
mes=`date +%m`
ano=`date +%Y`

## LOGIN ##
login="curl --cookie-jar cookies.iakim 'http://inlabs.in.gov.br/logar.php' -H 'origem: 736372697074' --data 'email=$email&password=$senha' --compressed"
echo $login > login.sh
sh login.sh
rm -rf login.sh

if test -f "cookies.iakim"; then
	valida_login=`cat cookies.iakim | grep inlabs_session_cookie | wc -l`
	if [ $valida_login -eq 1 ]
	then
		echo "Login Realizado com Sucesso"
	else
	        echo "Falha de Autenticação"
		rm -rf cookies.iakim
		exit 0
	fi
else
	echo "Não foi possível localizar os cookies, verifique as permissões e sua conectividade"
	exit 0
fi

## DOWNLOAD ##
for secao in $tipo_dou;
do
	download="curl --silent -fL -b cookies.iakim 'http://inlabs.in.gov.br/index.php?p=$ano-$mes-$dia&dl=$ano-$mes-$dia-$secao.zip' -H 'origem: 736372697074' --output $ano-$mes-$dia-$secao.zip"
	echo $download > $ano-$mes-$dia-$secao.sh
	sh $ano-$mes-$dia-$secao.sh
	rm -rf $ano-$mes-$dia-$secao.sh
done

rm -rf cookies.iakim
exit 0