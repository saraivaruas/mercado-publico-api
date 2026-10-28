# Cómo publicar una versión

Hay **un solo repositorio y tres paquetes**, con la misma versión (SemVer):

| Lenguaje | Registro | Nombre | Cómo se publica |
|---|---|---|---|
| PHP | [Packagist](https://packagist.org) | `saraivaruas/mercado-publico` | Automático: Packagist lee las etiquetas de git por el webhook de GitHub |
| C# | [NuGet](https://www.nuget.org) | `MercadoPublico` | El flujo `Release` (secreto `NUGET_API_KEY`) |
| Rust | [crates.io](https://crates.io) | `mercado_publico` | El flujo `Release` (secreto `CARGO_REGISTRY_TOKEN`) |

## Qué es una versión mayor, menor o parche

- **Parche (1.0.1):** corrige un error o un detalle de la API real, sin cambiar lo que devuelve la biblioteca.
- **Menor (1.1.0):** agrega algo (un endpoint, un filtro, un campo en `Parte`) sin romper el código existente.
- **Mayor (2.0.0):** cambia nombres, firmas o la forma de los resultados. Debe hacerse igual en los tres lenguajes.

## Pasos

1. Cree una rama y cambie la versión en los tres lenguajes a la vez:
   ```bash
   tools/version.sh set 1.1.0
   ```
2. Agregue la sección `## 1.1.0 — AAAA-MM` al [CHANGELOG](CHANGELOG.md) con lo que cambió.
3. Abra un **pull request**. El CI debe pasar (`tools/version.sh check` también corre ahí).
4. Una vez fusionado a `main`, cree la etiqueta **sobre `main`** y súbala:
   ```bash
   git checkout main && git pull
   git tag -a v1.1.0 -m "v1.1.0"
   git push origin v1.1.0
   ```
5. El flujo **Release** verifica que la etiqueta coincida con las versiones, corre todas las pruebas, publica en NuGet y
   crates.io (si los secretos existen), y crea el *GitHub Release* con las notas del CHANGELOG y el `.nupkg`.
   Packagist detecta la etiqueta por sí solo.

> Una versión publicada en NuGet o crates.io **no se puede reemplazar ni borrar de verdad** (solo retirar). Si algo salió mal,
> publique un parche nuevo; no reescriba la etiqueta.

## Configuración inicial (una sola vez, la hace el dueño del repositorio)

1. **Packagist:** inicie sesión con GitHub, use «Submit» con `https://github.com/saraivaruas/mercado-publico-api` y acepte
   instalar el webhook. Desde entonces cada etiqueta nueva aparece sola.
2. **NuGet:** en nuget.org cree una *API key* limitada a publicar el paquete `MercadoPublico` y guárdela como secreto
   `NUGET_API_KEY` del repositorio (Settings → Secrets and variables → Actions).
3. **crates.io:** inicie sesión con GitHub, cree un token con permiso de publicar y guárdelo como secreto
   `CARGO_REGISTRY_TOKEN`. La **primera** publicación de un nombre exige un token con permiso `publish-new`.
4. **Opcional, vigilancia de la API:** guarde su ticket de Mercado Público como secreto `MERCADOPUBLICO_TICKET`; el flujo
   `API viva` consulta cada semana la API real (solo lectura, ~15 consultas) y falla si algo cambió.

Nunca guarde tokens ni tickets en el repositorio.
