<?php
// Pruebas sin dependencias: levanta un servidor de mentira local y ejercita el cliente.
declare(strict_types=1);

spl_autoload_register(function (string $c): void {
    if (str_starts_with($c, 'MercadoPublico\\')) {
        require __DIR__ . '/../src/' . substr($c, 15) . '.php';
    }
});

use MercadoPublico\{Cliente, FechaChile, Opciones, Participantes, Parte};

$puerto = 18000 + random_int(0, 999);
$servidor = proc_open([PHP_BINARY, '-S', "127.0.0.1:$puerto", __DIR__ . '/mock.php'], [1 => ['file', '/dev/null', 'w'], 2 => ['file', '/dev/null', 'w']], $pipes);
register_shutdown_function(fn () => proc_terminate($servidor));
for ($i = 0; $i < 50 && !@fsockopen('127.0.0.1', $puerto); $i++) {
    usleep(100_000);
}

$ok = 0;
$fallas = [];
function prueba(string $nombre, callable $f): void
{
    global $ok, $fallas;
    try {
        $f();
        $ok++;
        echo "  ✔ $nombre\n";
    } catch (Throwable $e) {
        $fallas[] = $nombre;
        echo "  ✘ $nombre — " . $e->getMessage() . "\n";
    }
}
function igual(mixed $esperado, mixed $real, string $msg = ''): void
{
    if ($esperado !== $real) {
        throw new RuntimeException(($msg ? "$msg: " : '') . 'esperado ' . json_encode($esperado) . ', real ' . json_encode($real));
    }
}

function cliente(string $ticket): Cliente
{
    global $puerto;

    return new Cliente(new Opciones(
        $ticket . bin2hex(random_bytes(3)),
        "http://127.0.0.1:$puerto/v1/publico",
        "http://127.0.0.1:$puerto/v2/compra-agil",
        timeoutSegundos: 10,
        maxIntentos: 3,
        esperaReintentoMs: 1,
        intervaloMinimoMs: 0,
    ));
}

echo "Mercado Público — pruebas PHP\n";

prueba('fecha en formato ddmmaaaa del calendario de Chile', function () {
    igual('05032026', FechaChile::formato(new DateTimeImmutable('2026-03-05')));
    igual('America/Santiago', FechaChile::hoy()->getTimezone()->getName());
});
prueba('licitaciones por fecha manda ticket y fecha', function () {
    $r = cliente('ok')->licitaciones(['fecha' => new DateTimeImmutable('2026-03-05'), 'estado' => 'adjudicada', 'codigoOrganismo' => 7314]);
    igual(true, $r->exito);
    igual('05032026', $r->items[0]['q']['fecha']);
    igual('adjudicada', $r->items[0]['q']['estado']);
    igual('7314', $r->items[0]['q']['CodigoOrganismo']);
});
prueba('reintenta "peticiones simultáneas" y termina bien', function () {
    $r = cliente('simult')->licitacion('1-L1');
    igual(true, $r->exito, 'éxito tras reintentos');
    igual('1-L1', $r->primero()['CodigoExterno']);
});
prueba('reintenta HTTP 429', function () {
    igual(true, cliente('r429')->licitacionesActivas()->exito);
});
prueba('ticket inválido no se reintenta y devuelve el mensaje oficial', function () {
    $r = cliente('malo')->licitacionesActivas();
    igual(false, $r->exito);
    igual(203, $r->codigoError);
    igual('Ticket no válido.', $r->mensaje);
});
prueba('sin filtros válidos falla localmente', function () {
    igual(false, cliente('ok')->licitaciones(['cualquiera' => 'x'])->exito);
});
prueba('convenio marco filtra solo los códigos -CM<año>', function () {
    $r = cliente('ok')->ordenesConvenioMarco('05032026');
    igual(['2-2-CM26', '3-3-CM25'], array_column($r->items, 'Codigo'));
    igual(true, Cliente::esConvenioMarco('1002584-358-CM26'));
    igual(false, Cliente::esConvenioMarco('1-1-SE26'));
});
prueba('compradores y proveedores', function () {
    igual(2, count(cliente('ok')->compradores()->items));
    igual('LATAM', cliente('ok')->proveedores('89.862.200-2')->primero()['NombreEmpresa']);
});
prueba('proveedor sin resultados es éxito con lista vacía', function () {
    $r = cliente('ok')->proveedores('0-0');
    igual(true, $r->exito);
    igual([], $r->items);
});
prueba('Compra Ágil: envía ticket por cabecera y user-agent (si no, 403)', function () {
    $r = cliente('ok')->compraAgil('3754-146-COT26');
    igual(true, $r->exito);
    igual('3754-146-COT26', $r->primero()['codigo']);
});
prueba('Compra Ágil: código inválido devuelve el mensaje de la API', function () {
    $r = cliente('ok')->compraAgil('MALO');
    igual(false, $r->exito);
    igual(400, $r->http);
});
prueba('Compra Ágil: recorre las 16 regiones y deduplica', function () {
    $r = cliente('ok')->compraAgilPublicadas();
    igual(true, $r->exito);
    igual(false, $r->truncado);
    igual(16 * 2 + 1, count($r->items), '16 regiones × 2 páginas + 1 repetida única');
    igual(1, count(array_filter($r->items, fn ($i) => $i['codigo'] === 'REPETIDA')));
});
prueba('Compra Ágil: palabra clave viaja con %20 y sin recorrer regiones', function () {
    $r = cliente('ok')->compraAgilPublicadas(['palabraClave' => 'servicio aseo']);
    igual('servicio aseo', $r->items[0]['q']);
    igual(true, str_contains($r->items[0]['raw'], 'q=servicio%20aseo'), 'espacio codificado como %20, no +');
    igual(2, count($r->items), 'una sola consulta paginada (2 páginas), sin recorrer regiones');
});
prueba('Compra Ágil: página que falla deja el listado marcado como truncado', function () {
    $r = cliente('fallapag')->compraAgilPublicadas(['regiones' => [3]]);
    igual(true, $r->exito);
    igual(true, $r->truncado);
    igual(1, count($r->items));
});
prueba('Compra Ágil: el tope de tiempo trunca y conserva lo acumulado', function () {
    $r = cliente('lento')->compraAgilPublicadas(['maxSegundos' => 1]);
    igual(true, $r->truncado);
    igual(true, count($r->items) > 0 && count($r->items) < 33);
});
prueba('participantes: licitación adjudicada', function () {
    $p = Participantes::de(['CodigoExterno' => 'X', 'Comprador' => ['NombreOrganismo' => 'HOSPITAL', 'RutUnidad' => '61.602.148-6', 'CodigoOrganismo' => '7314', 'RegionUnidad' => 'O´Higgins'], 'Items' => ['Listado' => [
        ['Adjudicacion' => ['RutProveedor' => '78.723.570-0', 'NombreProveedor' => 'BARRON SPA']],
        ['Adjudicacion' => ['RutProveedor' => '78.723.570-0', 'NombreProveedor' => 'BARRON SPA']],
        ['Correlativo' => 3],
    ]]]);
    igual('HOSPITAL', $p['compradores'][0]->nombre);
    igual(1, count($p['vendedores']), 'proveedor repetido una sola vez');
    igual(Parte::VENDEDOR, $p['vendedores'][0]->rol);
});
prueba('participantes: orden de compra y Compra Ágil', function () {
    $oc = Participantes::de(['Codigo' => '1-CM26', 'Comprador' => ['NombreOrganismo' => 'DEP'], 'Proveedor' => ['Nombre' => 'LATAM', 'RutSucursal' => '89.862.200-2']]);
    igual('LATAM', $oc['vendedores'][0]->nombre);
    igual('orden_compra', $oc['compradores'][0]->fuente);
    $ca = Participantes::de(cliente('ok')->compraAgil('X-1-COT26')->primero());
    igual('MUNI X', $ca['compradores'][0]->nombre);
    igual('PROV UNO', $ca['vendedores'][0]->nombre);
});
prueba('el ticket vacío se rechaza', function () {
    try {
        new Opciones('  ');
        throw new RuntimeException('debió lanzar');
    } catch (InvalidArgumentException) {
    }
});

echo "\n$ok pruebas correctas" . ($fallas ? ', ' . count($fallas) . " con falla\n" : "\n");
exit($fallas ? 1 : 0);
