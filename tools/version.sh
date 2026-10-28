#!/usr/bin/env bash
# Mantiene UNA sola versión para los tres paquetes (NuGet, crates.io y Packagist).
#   tools/version.sh check [vX.Y.Z]   verifica que C#, Rust y el CHANGELOG coincidan (y con la etiqueta, si se da)
#   tools/version.sh set X.Y.Z         cambia la versión en C# y Rust
# PHP no lleva versión en composer.json: Packagist la toma de la etiqueta de git.
set -euo pipefail

raiz="$(cd "$(dirname "$0")/.." && pwd)"
csproj="$raiz/dotnet/MercadoPublico/MercadoPublico.csproj"
cargo="$raiz/rust/Cargo.toml"
changelog="$raiz/CHANGELOG.md"
semver='^[0-9]+\.[0-9]+\.[0-9]+$'

version_cs() { sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$csproj" | head -1; }
version_rs() { sed -n 's/^version = "\(.*\)"$/\1/p' "$cargo" | head -1; }

case "${1:-}" in
  check)
    cs="$(version_cs)"; rs="$(version_rs)"
    [[ "$cs" =~ $semver ]] || { echo "Versión de C# inválida: '$cs'" >&2; exit 1; }
    [[ "$cs" == "$rs" ]] || { echo "Las versiones no coinciden: C#=$cs, Rust=$rs" >&2; exit 1; }
    grep -qE "^## ${cs//./\\.}( |$)" "$changelog" || { echo "Falta la sección '## $cs' en CHANGELOG.md" >&2; exit 1; }
    if [[ -n "${2:-}" ]]; then
      [[ "${2#v}" == "$cs" ]] || { echo "La etiqueta '$2' no coincide con la versión $cs" >&2; exit 1; }
    fi
    echo "Versión $cs consistente (C#, Rust, CHANGELOG${2:+, etiqueta $2})."
    ;;
  set)
    nueva="${2:-}"
    [[ "$nueva" =~ $semver ]] || { echo "Uso: tools/version.sh set X.Y.Z" >&2; exit 2; }
    sed -i "s:<Version>.*</Version>:<Version>$nueva</Version>:" "$csproj"
    sed -i "0,/^version = \".*\"$/s//version = \"$nueva\"/" "$cargo"
    echo "Versión cambiada a $nueva en C# y Rust. Falta agregar la sección '## $nueva' al CHANGELOG.md."
    ;;
  *)
    echo "Uso: tools/version.sh check [vX.Y.Z] | set X.Y.Z" >&2
    exit 2
    ;;
esac
