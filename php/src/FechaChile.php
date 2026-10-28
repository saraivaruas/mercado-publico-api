<?php

declare(strict_types=1);

namespace MercadoPublico;

use DateTimeImmutable;
use DateTimeInterface;
use DateTimeZone;

/**
 * La API entiende "hoy" como el día calendario de Chile. Un servidor en UTC
 * pasa a "mañana" por la tarde/noche de Chile y la API rechaza esa fecha
 * ("La fecha es mayor a la actual"), por eso nunca use la hora del servidor.
 */
final class FechaChile
{
    public const ZONA = 'America/Santiago';

    public static function hoy(): DateTimeImmutable
    {
        return new DateTimeImmutable('now', new DateTimeZone(self::ZONA));
    }

    /** Formato que exige la API: ddmmaaaa. */
    public static function formato(DateTimeInterface $fecha): string
    {
        return $fecha->format('dmY');
    }
}
