<?php
// Servidor de mentira que imita las APIs de Mercado Público. El "ticket" elige el escenario.
$uri = parse_url($_SERVER['REQUEST_URI']);
$path = $uri['path'];
parse_str($uri['query'] ?? '', $q);
$h = array_change_key_case(getallheaders(), CASE_LOWER);
header('Content-Type: application/json');

function contador(string $clave): int
{
    $f = sys_get_temp_dir() . '/mpmock_' . md5($clave);
    $n = (int) @file_get_contents($f);
    file_put_contents($f, (string) ($n + 1));

    return $n;
}

if (str_starts_with($path, '/v2')) {
    $ticket = $h['ticket'] ?? '';
    if ($ticket === '' || ($h['user-agent'] ?? '') === '') {
        http_response_code(403);
        echo '{"message":"Forbidden"}';
        return;
    }
    if (str_starts_with($ticket, 'lento')) {
        usleep(150_000);
    }
    if (preg_match('#^/v2/compra-agil/(.+)$#', $path, $m)) {
        if ($m[1] === 'MALO') {
            http_response_code(400);
            echo '{"success":"NOK","payload":null,"errors":[{"codigo":"PARAMETRO_RUTA_INVALIDO","mensaje":"Parámetro de ruta \'codigo\' inválido."}]}';
            return;
        }
        echo json_encode(['success' => 'OK', 'payload' => ['codigo' => $m[1], 'institucion' => ['organismo_comprador' => 'MUNI X', 'rut' => '69.041.300-0', 'unidad_compra' => 'SALUD', 'nombre_region' => 'Región de Coquimbo'], 'proveedores_cotizando' => [['razon_social' => 'PROV UNO', 'rut' => '1-9']]]]);
        return;
    }
    $region = (int) ($q['region'] ?? 0);
    $pagina = (int) ($q['numero_pagina'] ?? 1);
    if (str_starts_with($ticket, 'fallapag') && $region === 3 && $pagina === 2) {
        http_response_code(500);
        echo '{"message":"Internal"}';
        return;
    }
    $items = [['codigo' => "R$region-P$pagina-A", 'q' => $q['q'] ?? null, 'raw' => $_SERVER['QUERY_STRING'] ?? '', 'nombre' => 'x']];
    // Una Compra Ágil repetida en dos regiones: debe deduplicarse.
    if ($pagina === 1 && ($region === 1 || $region === 2)) {
        $items[] = ['codigo' => 'REPETIDA', 'nombre' => 'y'];
    }
    echo json_encode(['success' => 'OK', 'payload' => ['items' => $items, 'paginacion' => ['total_paginas' => 2, 'numero_pagina' => $pagina, 'tamano_pagina' => 50, 'total_resultados' => 100]]]);
    return;
}

$ticket = $q['ticket'] ?? '';
if (str_starts_with($ticket, 'malo')) {
    http_response_code(203);
    echo '{"Codigo":203,"Mensaje":"Ticket no válido."}';
    return;
}
if (str_starts_with($ticket, 'simult') && contador($ticket . $path) < 2) {
    echo '{"Codigo":10500,"Mensaje":"Hemos detectado que existen peticiones simultáneas"}';
    return;
}
if (str_starts_with($ticket, 'r429') && contador($ticket . $path) < 1) {
    http_response_code(429);
    echo 'Too Many Requests';
    return;
}
if (str_ends_with($path, '/Empresas/BuscarComprador')) {
    echo '{"Cantidad":2,"listaEmpresas":[{"CodigoEmpresa":"1","NombreEmpresa":"A"},{"CodigoEmpresa":"2","NombreEmpresa":"B"}]}';
    return;
}
if (str_ends_with($path, '/Empresas/BuscarProveedor')) {
    if (($q['rutempresaproveedor'] ?? '') === '0-0') {
        http_response_code(500);
        echo '{"Codigo":10200,"Mensaje":"No hay resultados de empresas."}';
        return;
    }
    echo '{"Cantidad":1,"listaEmpresas":[{"CodigoEmpresa":"25638","NombreEmpresa":"LATAM"}]}';
    return;
}
if (str_ends_with($path, '/ordenesdecompra.json')) {
    echo json_encode(['Cantidad' => 3, 'Listado' => [['Codigo' => '1-1-SE26'], ['Codigo' => '2-2-CM26'], ['Codigo' => '3-3-CM25'], ['Codigo' => '4-4-CM2']], '_q' => $q]);
    return;
}
echo json_encode(['Cantidad' => 1, 'Listado' => [['CodigoExterno' => '1-L1', 'q' => $q]]]);
