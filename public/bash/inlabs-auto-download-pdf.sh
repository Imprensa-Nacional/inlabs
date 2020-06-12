#!/bin/bash
######################################################################
## INLABS 							    ##
## Script desenvolvido em bash para download automático de arquivos ##
## Autor: https://github.com/Iakim 				    ##
## A simplicidade é o último grau de sofisticação 		    ##
######################################################################

email="mail@mail.com"
senha="123456"

## Tipos de Diários Oficiais da União permitidos: do1 do2 do3 (Contempla as edições extras) ##
tipo_dou="do1 do2 do3"

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
	echo "curl --silent -fL -b cookies.iakim 'http://inlabs.in.gov.br/index.php?p=$ano-$mes-$dia&dl="$ano"_"$mes"_"$dia"_ASSINADO_"$secao".pdf' -H 'origem: 736372697074' --output "$ano"_"$mes"_"$dia"_ASSINADO_"$secao".pdf"
	download="curl --silent -fL -b cookies.iakim 'http://inlabs.in.gov.br/index.php?p=$ano-$mes-$dia&dl="$ano"_"$mes"_"$dia"_ASSINADO_"$secao".pdf' -H 'origem: 736372697074' --output "$ano"_"$mes"_"$dia"_ASSINADO_"$secao".pdf"
        echo $download > $ano-$mes-$dia-$secao.sh
        sh $ano-$mes-$dia-$secao.sh
        rm -rf $ano-$mes-$dia-$secao.sh
	
	for seq in A B C D E F G H I J K L M N O P Q R S T U V X W Y Z
	do
		echo "curl --silent -fL -b cookies.iakim 'http://inlabs.in.gov.br/index.php?p=$ano-$mes-$dia&dl="$ano"_"$mes"_"$dia"_ASSINADO_"$secao"_extra_"$seq".pdf' -H 'origem: 736372697074' --output "$ano"_"$mes"_"$dia"_ASSINADO_"$secao"_extra_"$seq""
		download="curl --silent -fL -b cookies.iakim 'http://inlabs.in.gov.br/index.php?p=$ano-$mes-$dia&dl="$ano"_"$mes"_"$dia"_ASSINADO_"$secao"_extra_"$seq".pdf' -H 'origem: 736372697074' --output "$ano"_"$mes"_"$dia"_ASSINADO_"$secao"_extra_"$seq""
		echo $download > $ano-$mes-$dia-$secao.sh
		sh $ano-$mes-$dia-$secao.sh
		rm -rf $ano-$mes-$dia-$secao.sh
	done
done

rm -rf cookies.iakim
exit 0
