<?php

declare(strict_types=1);

namespace MercadoPublico;

/**
 * Extrae "quién compra y quién vende" del DETALLE de una licitación, orden de
 * compra (incluye Convenio Marco) o Compra Ágil. Solo el detalle trae estos
 * datos: los listados por fecha traen únicamente código, nombre y estado.
 *
 * Una licitación solo informa vendedores cuando ya está adjudicada; una Compra
 * Ágil, los proveedores que han cotizado.
 */
final class Participantes
{
    /**
     * Detecta el tipo de detalle por su forma.
     *
     * @param array<string, mixed> $detalle
     * @return array{compradores: Parte[], vendedores: Parte[]}
     */
    public static function de(array $detalle): array
    {
        if (isset($detalle['CodigoExterno'])) {
            return self::deLicitacion($detalle);
        }
        if (isset($detalle['institucion'])) {
            return self::deCompraAgil($detalle);
        }
        if (isset($detalle['Comprador']) || isset($detalle['Proveedor'])) {
            return self::deOrdenCompra($detalle);
        }

        return ['compradores' => [], 'vendedores' => []];
    }

    /**
     * @param array<string, mixed> $d
     * @return array{compradores: Parte[], vendedores: Parte[]}
     */
    public static function deLicitacion(array $d): array
    {
        $c = is_array($d['Comprador'] ?? null) ? $d['Comprador'] : [];
        $compradores = $c === [] ? [] : [new Parte(
            Parte::COMPRADOR,
            self::texto($c['NombreOrganismo'] ?? null),
            self::texto($c['RutUnidad'] ?? null),
            self::texto($c['CodigoOrganismo'] ?? null),
            self::texto($c['NombreUnidad'] ?? null),
            self::texto($c['RegionUnidad'] ?? null),
            self::texto($c['ComunaUnidad'] ?? null),
            'licitacion',
        )];

        $vendedores = [];
        foreach ($d['Items']['Listado'] ?? [] as $item) {
            $a = is_array($item['Adjudicacion'] ?? null) ? $item['Adjudicacion'] : null;
            $rut = self::texto($a['RutProveedor'] ?? null);
            if ($a === null || $rut === null || isset($vendedores[$rut])) {
                continue;
            }
            $vendedores[$rut] = new Parte(Parte::VENDEDOR, self::texto($a['NombreProveedor'] ?? null), $rut, null, null, null, null, 'licitacion');
        }

        return ['compradores' => $compradores, 'vendedores' => array_values($vendedores)];
    }

    /**
     * @param array<string, mixed> $d
     * @return array{compradores: Parte[], vendedores: Parte[]}
     */
    public static function deOrdenCompra(array $d): array
    {
        $c = is_array($d['Comprador'] ?? null) ? $d['Comprador'] : [];
        $p = is_array($d['Proveedor'] ?? null) ? $d['Proveedor'] : [];

        return [
            'compradores' => $c === [] ? [] : [new Parte(
                Parte::COMPRADOR,
                self::texto($c['NombreOrganismo'] ?? null),
                self::texto($c['RutUnidad'] ?? null),
                self::texto($c['CodigoOrganismo'] ?? null),
                self::texto($c['NombreUnidad'] ?? null),
                self::texto($c['RegionUnidad'] ?? null),
                self::texto($c['ComunaUnidad'] ?? null),
                'orden_compra',
            )],
            'vendedores' => $p === [] ? [] : [new Parte(
                Parte::VENDEDOR,
                self::texto($p['Nombre'] ?? null),
                self::texto($p['RutSucursal'] ?? null),
                self::texto($p['Codigo'] ?? null),
                self::texto($p['NombreSucursal'] ?? null),
                self::texto($p['Region'] ?? null),
                self::texto($p['Comuna'] ?? null),
                'orden_compra',
            )],
        ];
    }

    /**
     * @param array<string, mixed> $d
     * @return array{compradores: Parte[], vendedores: Parte[]}
     */
    public static function deCompraAgil(array $d): array
    {
        $i = is_array($d['institucion'] ?? null) ? $d['institucion'] : [];
        $compradores = $i === [] ? [] : [new Parte(
            Parte::COMPRADOR,
            self::texto($i['organismo_comprador'] ?? null),
            self::texto($i['rut'] ?? null),
            null,
            self::texto($i['unidad_compra'] ?? null),
            self::texto($i['nombre_region'] ?? null),
            null,
            'compra_agil',
        )];

        $vendedores = [];
        foreach ($d['proveedores_cotizando'] ?? [] as $p) {
            if (!is_array($p)) {
                continue;
            }
            $nombre = self::texto($p['razon_social'] ?? $p['nombre'] ?? $p['nombre_proveedor'] ?? null);
            $rut = self::texto($p['rut'] ?? $p['rut_proveedor'] ?? null);
            if ($nombre === null && $rut === null) {
                continue;
            }
            $vendedores[] = new Parte(Parte::VENDEDOR, $nombre, $rut, null, null, null, null, 'compra_agil');
        }

        return ['compradores' => $compradores, 'vendedores' => $vendedores];
    }

    private static function texto(mixed $v): ?string
    {
        if ($v === null || is_array($v) || is_object($v)) {
            return null;
        }
        $t = trim((string) $v);

        return $t === '' ? null : $t;
    }
}
