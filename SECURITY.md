# Seguridad

## Reportar una vulnerabilidad

No abra un issue público. Use «Report a vulnerability» en la pestaña **Security** del repositorio (reporte privado de
GitHub). Se responde en un plazo razonable; este es un proyecto de software libre mantenido sin garantía de soporte.

## Qué cuidar al usar la biblioteca

- **El ticket es una credencial.** Léalo de una variable de entorno o de su gestor de secretos; nunca lo escriba en el
  código ni lo suba al repositorio. La API v1 lo recibe como parámetro de la URL (así lo exige Mercado Público), por lo
  que no registre las URL completas en sus logs.
- La biblioteca nunca escribe ni registra el ticket.
- Los datos que devuelve la API son públicos, pero tratar RUT y nombres de personas naturales sigue sujeto a la
  Ley 19.628 sobre protección de la vida privada.
