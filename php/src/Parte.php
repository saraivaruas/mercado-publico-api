<?php

declare(strict_types=1);

namespace MercadoPublico;

/** Una parte de una compra: quien compra (organismo) o quien vende (proveedor). */
final class Parte
{
    public const COMPRADOR = 'comprador';
    public const VENDEDOR = 'vendedor';

    public function __construct(
        public readonly string $rol,
        public readonly ?string $nombre,
        public readonly ?string $rut,
        /** Código interno de Mercado Público del organismo o proveedor, si viene. */
        public readonly ?string $codigo = null,
        /** Unidad de compra (comprador) o sucursal (vendedor). */
        public readonly ?string $unidad = null,
        public readonly ?string $region = null,
        public readonly ?string $comuna = null,
        /** 'licitacion', 'orden_compra' o 'compra_agil'. */
        public readonly string $fuente = '',
    ) {
    }
}
