<?php

declare(strict_types=1);

namespace MercadoPublico;

/** Configuración del cliente. Todos los tiempos en milisegundos salvo `timeoutSegundos`. */
final class Opciones
{
    public function __construct(
        /** Ticket de la API (se pide gratis en api.mercadopublico.cl). Nunca lo deje escrito en el código. */
        public readonly string $ticket,
        /** Base de la API v1 (licitaciones, órdenes de compra, empresas). */
        public readonly string $urlBaseV1 = 'https://api.mercadopublico.cl/servicios/v1/publico',
        /** Base de la API v2 (Compra Ágil). */
        public readonly string $urlBaseV2 = 'https://api2.mercadopublico.cl/v2/compra-agil',
        public readonly int $timeoutSegundos = 45,
        /** Intentos totales por consulta ante errores transitorios (peticiones simultáneas, 429, 5xx, red). */
        public readonly int $maxIntentos = 4,
        /** Espera base entre reintentos; el intento N espera N veces este valor. */
        public readonly int $esperaReintentoMs = 2000,
        /** Separación mínima entre dos consultas (cortesía hacia la API; sin ella responde "peticiones simultáneas"/429). */
        public readonly int $intervaloMinimoMs = 1000,
        /** Sin User-Agent el WAF de la API v2 responde 403. */
        public readonly string $userAgent = 'Mozilla/5.0 (compatible; mercado-publico-api/1.0; +https://github.com/saraivaruas/mercado-publico-api)',
    ) {
        if (trim($ticket) === '') {
            throw new \InvalidArgumentException('El ticket no puede estar vacío.');
        }
        if ($maxIntentos < 1) {
            throw new \InvalidArgumentException('maxIntentos debe ser al menos 1.');
        }
    }
}
