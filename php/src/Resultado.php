<?php

declare(strict_types=1);

namespace MercadoPublico;

/**
 * Resultado de una consulta. Las fallas de la API NO lanzan excepciones: se
 * devuelven con `exito = false` y el mensaje oficial de Mercado Público.
 * Los elementos de `items` se entregan tal cual los devuelve la API (arreglos
 * asociativos), porque su forma no está garantizada.
 */
final class Resultado
{
    /** @param array<int, array<string, mixed>> $items */
    public function __construct(
        public readonly bool $exito,
        public readonly string $mensaje,
        public readonly array $items = [],
        /** Código de error de la API (ej. 203 ticket inválido, 10500 peticiones simultáneas) o de la capa HTTP. */
        public readonly ?int $codigoError = null,
        public readonly ?int $http = null,
        /** true si el listado es PARCIAL (una página falló o se agotó el tiempo): faltan registros. */
        public readonly bool $truncado = false,
        /** Total declarado por la API, si lo informa. */
        public readonly ?int $total = null,
    ) {
    }

    /** @param array<int, array<string, mixed>> $items */
    public static function ok(array $items, ?int $total = null, bool $truncado = false, string $mensaje = 'Conexión exitosa.', ?int $http = 200): self
    {
        return new self(true, $mensaje, $items, null, $http, $truncado, $total);
    }

    public static function error(string $mensaje, ?int $codigoError = null, ?int $http = null): self
    {
        return new self(false, $mensaje, [], $codigoError, $http);
    }

    /** Primer elemento (útil para consultas por código). */
    public function primero(): ?array
    {
        return $this->items[0] ?? null;
    }
}
