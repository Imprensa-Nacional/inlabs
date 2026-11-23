#!/bin/sh
# Uso: inlabs <xml|pdf> [AAAA-MM-DD]
set -e

tipo="${1:-xml}"
[ $# -gt 0 ] && shift

case "$tipo" in
    xml|pdf)
        exec php "/app/inlabs-auto-download-$tipo.php" "$@"
        ;;
    *)
        echo "Uso: inlabs <xml|pdf> [AAAA-MM-DD]" >&2
        exit 1
        ;;
esac
