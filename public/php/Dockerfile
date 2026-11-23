# INLABS - Download automático do Diário Oficial da União (scripts PHP)
#
# Build: docker build -t inlabs-php public/php
# Uso:   docker run --rm --env-file inlabs.env -v "$(pwd)/dou:/dados" inlabs-php xml [AAAA-MM-DD]

FROM php:8.3-cli-alpine

# A extensão cURL já vem habilitada na imagem oficial do PHP
RUN adduser -D -u 1000 inlabs \
    && mkdir /dados \
    && chown inlabs:inlabs /dados

WORKDIR /app
COPY --chmod=755 docker-entrypoint.sh /usr/local/bin/inlabs
COPY inlabs-auto-download-xml.php inlabs-auto-download-pdf.php ./

ENV INLABS_DESTINO=/dados
VOLUME /dados
USER inlabs

ENTRYPOINT ["inlabs"]
CMD ["xml"]
