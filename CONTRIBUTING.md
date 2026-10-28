# Cómo contribuir

Este proyecto es de software libre (MIT) y **solo acepta cambios mediante pull request**. No se aceptan empujes
directos a `main`: todo cambio pasa por una rama propia, un pull request y una revisión.

## Antes de abrir un pull request

1. **Abra primero un issue** si el cambio es grande (un endpoint nuevo, un cambio de comportamiento), para acordar el
   enfoque.
2. **Mantenga los tres lenguajes iguales.** Un cambio de comportamiento en PHP, C# o Rust debe aplicarse en los tres,
   con la misma prueba en cada uno. Un cambio solo de documentación o de un lenguaje debe decirlo en el pull request.
3. **Agregue pruebas** en `php/tests`, `dotnet/MercadoPublico.Tests` y `rust/tests`. Las pruebas normales corren contra
   un servidor local de mentira y no necesitan ticket.
4. **Si descubre algo de la API real** (un código de error, un límite, un campo), anótelo en [docs/API.md](docs/API.md)
   indicando cómo lo comprobó y en qué fecha. No documente como hecho lo que no probó.
5. **Nunca suba un ticket**, ni siquiera uno vencido, ni respuestas reales con datos que no sean públicos.

## Pruebas

```bash
php php/tests/run.php                                   # PHP 8.1+
dotnet run --project dotnet/MercadoPublico.Tests -f net8.0   # C# (también net10.0)
(cd rust && cargo test && cargo clippy --all-targets -- -D warnings && cargo fmt --check)
```

Las pruebas **vivas** consultan la API real (solo lectura) y requieren su propio ticket:

```bash
export MERCADOPUBLICO_TICKET=...
php php/tests/vivo.php
dotnet run --project dotnet/MercadoPublico.Tests -f net10.0 -- --vivo
(cd rust && cargo test --test integracion -- --ignored --nocapture)
```

Córralas una tras otra, no a la vez: la API responde 429 si se la consulta demasiado rápido.

## Estilo

- Sin dependencias externas en PHP y C#. En Rust, solo las ya declaradas salvo que el cambio lo justifique.
- Mensajes de commit claros, en español, en tiempo presente («Agrega…», «Corrige…»).
- Los comentarios explican el **porqué** (sobre todo lo aprendido de la API real), no repiten el código.

## Licencia de las contribuciones

Al enviar un pull request usted acepta que su contribución se publica bajo la licencia MIT del proyecto.
