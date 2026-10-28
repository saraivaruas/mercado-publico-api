<?php

declare(strict_types=1);

namespace MercadoPublico;

use DateTimeInterface;

/**
 * Cliente de las APIs públicas de Mercado Público (ChileCompra).
 *
 * Mercado Público es la plataforma de compras de la Dirección ChileCompra:
 * este cliente cubre sus dos APIs con el mismo ticket.
 *   - v1 (api.mercadopublico.cl): licitaciones, órdenes de compra (incluye
 *     Convenio Marco), compradores y proveedores.
 *   - v2 (api2.mercadopublico.cl): Compra Ágil.
 *
 * Reglas aprendidas contra la API real (ver docs/API.md):
 *   - La API pide ~1 consulta por segundo; si no, responde "peticiones
 *     simultáneas" (código 10500) o HTTP 429. Este cliente espacia las
 *     consultas y reintenta esos errores transitorios.
 *   - Un día se consulta con fecha ddmmaaaa del calendario de CHILE.
 *   - Compra Ágil solo deja paginar 10.000 resultados por consulta, y hay
 *     más publicadas: por eso se recorre región por región.
 */
final class Cliente
{
    private const REGIONES = 16;
    private const TAMANO_PAGINA = 50;

    private readonly Transporte $transporte;

    public function __construct(private readonly Opciones $opciones, ?Transporte $transporte = null)
    {
        $this->transporte = $transporte ?? new Transporte($opciones);
    }

    // ───────────────────────── Licitaciones ─────────────────────────

    /**
     * @param array{fecha?: DateTimeInterface|string, estado?: string, codigo?: string, codigoOrganismo?: string|int, codigoProveedor?: string|int} $filtros
     *   `estado`: activas, publicada, cerrada, desierta, adjudicada, revocada, suspendida o todos.
     *   Con `fecha` el listado trae código, nombre, estado y fecha de cierre; el DETALLE (comprador,
     *   adjudicación, ítems) solo viene al consultar por `codigo`.
     */
    public function licitaciones(array $filtros): Resultado
    {
        return $this->v1('licitaciones.json', $filtros);
    }

    /** Detalle completo de una licitación (ej. "1001-18-LE26"). */
    public function licitacion(string $codigo): Resultado
    {
        return $this->v1('licitaciones.json', ['codigo' => $codigo]);
    }

    /** Todas las licitaciones activas hoy (unas 5.000). */
    public function licitacionesActivas(): Resultado
    {
        return $this->v1('licitaciones.json', ['estado' => 'activas']);
    }

    // ───────────── Órdenes de compra y Convenio Marco ─────────────

    /**
     * @param array{fecha?: DateTimeInterface|string, estado?: string, codigo?: string, codigoOrganismo?: string|int, codigoProveedor?: string|int} $filtros
     *   `estado`: enviadaproveedor, aceptada, cancelada, recepcionconforme, etc. El listado de un día
     *   trae ~18.000 órdenes con solo código, nombre y estado.
     */
    public function ordenesCompra(array $filtros): Resultado
    {
        return $this->v1('ordenesdecompra.json', $filtros);
    }

    /** Detalle completo de una orden de compra: comprador, proveedor, ítems y montos. */
    public function ordenCompra(string $codigo): Resultado
    {
        return $this->v1('ordenesdecompra.json', ['codigo' => $codigo]);
    }

    /**
     * Órdenes de compra de Convenio Marco de un día (código con sufijo "-CM<año>"):
     * unas 2.000 de las ~18.000 del día. Son compras ya hechas a precios pactados.
     *
     * @param array{estado?: string, codigoOrganismo?: string|int, codigoProveedor?: string|int} $filtros
     */
    public function ordenesConvenioMarco(DateTimeInterface|string $fecha, array $filtros = []): Resultado
    {
        $res = $this->ordenesCompra(['fecha' => $fecha] + $filtros);
        if (!$res->exito) {
            return $res;
        }
        $cm = array_values(array_filter($res->items, fn ($o) => self::esConvenioMarco((string) ($o['Codigo'] ?? ''))));

        return Resultado::ok($cm, count($cm), false, $res->mensaje, $res->http);
    }

    public static function esConvenioMarco(string $codigoOrden): bool
    {
        return preg_match('/-CM\d{2}$/', $codigoOrden) === 1;
    }

    // ───────────────── Quién compra / quién vende ─────────────────

    /** Los ~900 organismos compradores del Estado (CodigoEmpresa, NombreEmpresa). */
    public function compradores(): Resultado
    {
        return $this->v1('Empresas/BuscarComprador', [], 'listaEmpresas');
    }

    /** Busca un proveedor por RUT (ej. "89.862.200-2"). Sin coincidencia: éxito con lista vacía. */
    public function proveedores(string $rut): Resultado
    {
        return $this->v1('Empresas/BuscarProveedor', ['rutempresaproveedor' => $rut], 'listaEmpresas');
    }

    // ─────────────────────── Compra Ágil (v2) ───────────────────────

    /** Detalle de una Compra Ágil (ej. "3754-146-COT26"): institución, productos, proveedores cotizando. */
    public function compraAgil(string $codigo): Resultado
    {
        [$res, $crudo] = $this->ejecutar($this->opciones->urlBaseV2 . '/' . rawurlencode($codigo), 'v2');
        if (!$res->exito) {
            return $res;
        }

        return Resultado::ok(is_array($crudo['payload'] ?? null) ? [$crudo['payload']] : [], null, false, $res->mensaje, $res->http);
    }

    /**
     * Compras Ágiles publicadas (recibiendo cotizaciones).
     *
     * Sin `palabraClave` ni `regiones` recorre las 16 regiones y deduplica, porque una consulta
     * nacional solo pagina hasta 10.000 resultados. El filtro `palabraClave` de la API (`q`) es
     * poco fiable a nivel nacional (responde 504); filtre localmente si puede.
     *
     * @param array{palabraClave?: string, regiones?: int[], maxSegundos?: int} $opciones
     *   `maxSegundos`: tope del recorrido; al agotarse devuelve lo acumulado con `truncado = true`.
     */
    public function compraAgilPublicadas(array $opciones = []): Resultado
    {
        $palabra = trim((string) ($opciones['palabraClave'] ?? ''));
        $regiones = array_values(array_unique(array_map('intval', $opciones['regiones'] ?? [])));
        $limite = isset($opciones['maxSegundos']) ? microtime(true) + (int) $opciones['maxSegundos'] : null;

        if ($palabra !== '' || $regiones !== []) {
            return $this->recorrerPublicadas($palabra, $regiones, $limite);
        }

        $porCodigo = [];
        $truncado = false;
        $alguna = false;
        $ultimoError = null;
        for ($region = 1; $region <= self::REGIONES; $region++) {
            if ($limite !== null && microtime(true) > $limite) {
                $truncado = true;
                break;
            }
            $res = $this->recorrerPublicadas('', [$region], $limite);
            if (!$res->exito) {
                $truncado = true;
                $ultimoError = $res;
                continue;
            }
            $alguna = true;
            $truncado = $truncado || $res->truncado;
            foreach ($res->items as $item) {
                $porCodigo[isset($item['codigo']) ? (string) $item['codigo'] : 'sin-codigo-' . count($porCodigo)] = $item;
            }
        }

        if (!$alguna) {
            return $ultimoError ?? Resultado::error('Se agotó el tiempo antes de consultar la primera región.');
        }
        $items = array_values($porCodigo);

        return Resultado::ok($items, count($items), $truncado, $truncado ? 'Listado parcial: alguna región falló o se agotó el tiempo.' : 'Conexión exitosa.');
    }

    // ─────────────────────────── Internos ───────────────────────────

    /** @param int[] $regiones */
    private function recorrerPublicadas(string $palabra, array $regiones, ?float $limite): Resultado
    {
        $items = [];
        $total = null;
        $pagina = 1;
        $paginas = 1;

        do {
            if ($limite !== null && microtime(true) > $limite) {
                return Resultado::ok($items, $total, true, "Listado parcial: se agotó el tiempo en la página $pagina de $paginas.");
            }
            $query = ['estado' => 'publicada', 'tamano_pagina' => self::TAMANO_PAGINA, 'numero_pagina' => $pagina];
            if ($palabra !== '') {
                $query['q'] = $palabra;
            }
            if ($regiones !== []) {
                $query['region'] = implode(',', $regiones);
            }
            [$res, $crudo] = $this->ejecutar($this->opciones->urlBaseV2 . '?' . http_build_query($query, '', '&', PHP_QUERY_RFC3986), 'v2');
            if (!$res->exito) {
                if ($items !== []) {
                    return Resultado::ok($items, $total, true, "Listado parcial: falló la página $pagina de $paginas ({$res->mensaje}).");
                }

                return $res;
            }
            $items = array_merge($items, $crudo['payload']['items'] ?? []);
            $paginas = (int) ($crudo['payload']['paginacion']['total_paginas'] ?? 1);
            $total = isset($crudo['payload']['paginacion']['total_resultados']) ? (int) $crudo['payload']['paginacion']['total_resultados'] : $total;
            $pagina++;
        } while ($pagina <= $paginas);

        return Resultado::ok($items, $total);
    }

    /** @param array<string, mixed> $filtros */
    private function v1(string $ruta, array $filtros, string $clave = 'Listado'): Resultado
    {
        $query = ['ticket' => $this->opciones->ticket];
        $mapa = ['codigo' => 'codigo', 'estado' => 'estado', 'codigoOrganismo' => 'CodigoOrganismo', 'codigoProveedor' => 'CodigoProveedor', 'rutempresaproveedor' => 'rutempresaproveedor'];
        $hayFiltro = false;
        foreach ($filtros as $nombre => $valor) {
            if ($nombre === 'fecha') {
                $query['fecha'] = $valor instanceof DateTimeInterface ? FechaChile::formato($valor) : (string) $valor;
                $hayFiltro = true;
            } elseif (isset($mapa[$nombre]) && $valor !== null && $valor !== '') {
                $query[$mapa[$nombre]] = (string) $valor;
                $hayFiltro = true;
            }
        }
        if ($filtros !== [] && !$hayFiltro) {
            return Resultado::error('Indique al menos un filtro válido: fecha, estado o codigo.');
        }
        $url = rtrim($this->opciones->urlBaseV1, '/') . '/' . $ruta . '?' . http_build_query($query, '', '&', PHP_QUERY_RFC3986);

        [$res, $crudo] = $this->ejecutar($url, 'v1', $clave);
        unset($crudo);

        return $res;
    }

    /**
     * Ejecuta con reintentos ante errores transitorios.
     *
     * @return array{0: Resultado, 1: ?array<string, mixed>}
     */
    private function ejecutar(string $url, string $version, string $clave = 'Listado'): array
    {
        $cabeceras = $version === 'v2' ? ['ticket: ' . $this->opciones->ticket] : [];
        for ($intento = 1; ; $intento++) {
            $r = $this->transporte->get($url, $cabeceras);
            [$res, $crudo, $transitorio] = $version === 'v2' ? $this->interpretarV2($r) : $this->interpretarV1($r, $clave);
            if (!$transitorio || $intento >= $this->opciones->maxIntentos) {
                return [$res, $crudo];
            }
            $this->transporte->dormirMs($intento * $this->opciones->esperaReintentoMs);
        }
    }

    /**
     * @param array{http: int, cuerpo: string, errorRed: string} $r
     * @return array{0: Resultado, 1: ?array<string, mixed>, 2: bool}
     */
    private function interpretarV1(array $r, string $clave): array
    {
        if ($r['cuerpo'] === '') {
            return [Resultado::error('No fue posible contactar a Mercado Público. Verifique la conectividad.', null, $r['http'] ?: null), null, true];
        }
        $datos = json_decode($r['cuerpo'], true);
        if (!is_array($datos)) {
            $transitorio = $r['http'] === 429 || $r['http'] >= 500;

            return [Resultado::error('Respuesta inesperada de Mercado Público (HTTP ' . $r['http'] . ').', null, $r['http']), null, $transitorio];
        }
        if (array_key_exists($clave, $datos)) {
            $items = is_array($datos[$clave]) ? array_values($datos[$clave]) : [];

            return [Resultado::ok($items, isset($datos['Cantidad']) ? (int) $datos['Cantidad'] : null, false, 'Conexión exitosa.', $r['http']), $datos, false];
        }

        $codigo = isset($datos['Codigo']) ? (int) $datos['Codigo'] : null;
        $mensaje = is_string($datos['Mensaje'] ?? null) ? $datos['Mensaje'] : 'Mercado Público rechazó la solicitud.';
        // 10200 = "No hay resultados": la consulta fue válida, simplemente no hay datos.
        if ($codigo === 10200) {
            return [Resultado::ok([], 0, false, $mensaje, $r['http']), $datos, false];
        }
        $transitorio = $codigo === 10500 || $r['http'] === 429 || stripos($mensaje, 'simult') !== false;

        return [Resultado::error($mensaje, $codigo, $r['http']), $datos, $transitorio];
    }

    /**
     * @param array{http: int, cuerpo: string, errorRed: string} $r
     * @return array{0: Resultado, 1: ?array<string, mixed>, 2: bool}
     */
    private function interpretarV2(array $r): array
    {
        if ($r['cuerpo'] === '') {
            return [Resultado::error('No fue posible contactar la API de Compra Ágil.', null, $r['http'] ?: null), null, true];
        }
        $datos = json_decode($r['cuerpo'], true);
        $transitorio = $r['http'] === 408 || $r['http'] === 429 || $r['http'] >= 500;
        if (!is_array($datos)) {
            return [Resultado::error('Respuesta inesperada de la API de Compra Ágil (HTTP ' . $r['http'] . ').', null, $r['http']), null, $transitorio];
        }
        if (($datos['success'] ?? null) === 'OK') {
            $items = $datos['payload']['items'] ?? [];

            return [Resultado::ok(is_array($items) ? $items : [], null, false, 'Conexión exitosa.', $r['http']), $datos, false];
        }
        $mensaje = $datos['errors'][0]['mensaje'] ?? $datos['message'] ?? 'La API de Compra Ágil rechazó la solicitud.';
        $codigo = $r['http'] ?: null;

        return [Resultado::error(is_string($mensaje) ? $mensaje : 'La API de Compra Ágil rechazó la solicitud.', $codigo, $r['http']), $datos, $transitorio];
    }
}
