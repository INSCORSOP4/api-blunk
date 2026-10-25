using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SoporteBlunk.Application.Common.Options;

namespace SoporteBlunk.Application.Conocimiento;

/// <summary>
/// Convierte los archivos subidos en notas listas para guardar. Es lógica pura: no lee disco ni toca la base de datos.
/// Cada archivo se procesa de forma aislada: uno defectuoso se reporta como error sin afectar a los demás.
/// </summary>
public sealed class ProcesadorNotas(ReglasOmisionService reglas, ConocimientoOptions opciones)
{
    // UTF-8 estricto: un archivo que no lo sea se rechaza en vez de guardarse con caracteres corruptos.
    private static readonly UTF8Encoding Utf8Estricto = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public InformeProcesamiento ProcesarLote(IReadOnlyList<ArchivoSubido> archivos)
    {
        var resultados = new List<ResultadoArchivo>(archivos.Count);
        var rutasVistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (archivo, indice) in archivos.Select((a, i) => (a, i)))
        {
            if (indice >= opciones.MaxArchivos)
            {
                resultados.Add(new ResultadoArchivo(archivo.Ruta, EstadoArchivo.Error,
                    $"Se excedió el máximo de {opciones.MaxArchivos} archivos por subida.", null));
                continue;
            }

            var resultado = ProcesarArchivo(archivo);
            if (resultado.Nota is { } nota && !rutasVistas.Add(nota.RutaRelativa))
                resultado = new ResultadoArchivo(archivo.Ruta, EstadoArchivo.Error, "Ruta duplicada dentro de la misma subida.", null);
            resultados.Add(resultado);
        }

        return new InformeProcesamiento(
            resultados.Count,
            resultados.Count(r => r.Estado == EstadoArchivo.Procesado),
            resultados.Count(r => r.Estado == EstadoArchivo.Omitido),
            resultados.Count(r => r.Estado == EstadoArchivo.Error),
            resultados.Count(r => r.Nota is { SecretosEnmascarados: > 0 }),
            resultados.Sum(r => r.Nota?.SecretosEnmascarados ?? 0),
            resultados);
    }

    public ResultadoArchivo ProcesarArchivo(ArchivoSubido archivo)
    {
        var problema = RutaNota.TryNormalizar(archivo.Ruta, out var ruta);
        if (problema == RutaNota.Problema.NoEsMarkdown)
            return new ResultadoArchivo(MostrarRuta(archivo.Ruta), EstadoArchivo.Omitido, "No es un archivo .md.", null);
        if (problema != RutaNota.Problema.Ninguno)
            return new ResultadoArchivo(MostrarRuta(archivo.Ruta), EstadoArchivo.Error, RutaNota.Describir(problema), null);

        if (reglas.MotivoDeOmision(ruta) is { } motivo)
            return new ResultadoArchivo(ruta, EstadoArchivo.Omitido, motivo, null);

        if (archivo.Contenido.Length == 0)
            return new ResultadoArchivo(ruta, EstadoArchivo.Omitido, "El archivo está vacío.", null);
        if (archivo.Contenido.Length > opciones.MaxBytesPorArchivo)
            return new ResultadoArchivo(ruta, EstadoArchivo.Error,
                $"El archivo supera el máximo de {opciones.MaxBytesPorArchivo / 1024} KB.", null);

        try
        {
            return new ResultadoArchivo(ruta, EstadoArchivo.Procesado, null, ConstruirNota(ruta, archivo));
        }
        catch (DecoderFallbackException)
        {
            return new ResultadoArchivo(ruta, EstadoArchivo.Error, "El archivo no está codificado en UTF-8.", null);
        }
        catch (RegexMatchTimeoutException)
        {
            // Si el análisis de secretos no termina, la nota NO se guarda: mejor perderla que publicar un secreto.
            return new ResultadoArchivo(ruta, EstadoArchivo.Error, "No se pudo revisar el archivo de forma segura (tiempo agotado).", null);
        }
    }

    private NotaProcesada ConstruirNota(string ruta, ArchivoSubido archivo)
    {
        var hash = SHA256.HashData(archivo.Contenido);
        var advertencias = new List<string>();

        var texto = Utf8Estricto.GetString(archivo.Contenido)
            .TrimStart('﻿')
            .Replace("\r\n", "\n").Replace('\r', '\n').Replace("\0", "")
            .Normalize(NormalizationForm.FormC);

        // 1) Secretos primero, sobre el texto completo (incluido el frontmatter): de aquí en adelante nada los contiene.
        var enmascarado = EnmascaradorSecretos.Enmascarar(texto);

        // 2) Estructura de la nota.
        var separado = MarkdownNota.SepararFrontmatter(enmascarado.Texto, opciones.MaxBytesFrontmatter);
        if (separado.Advertencia is not null) advertencias.Add(separado.Advertencia);

        var cuerpo = MarkdownNota.QuitarComentarios(separado.Cuerpo).Trim(); // los comentarios de Obsidian son privados
        var lineas = MarkdownNota.Lineas(cuerpo);
        var carpeta = RutaNota.Carpeta(ruta);

        var titulo = Recortar(
            MarkdownNota.Texto(separado.Datos, "title", "titulo", "título")
            ?? MarkdownNota.TituloDeEncabezado(lineas)
            ?? RutaNota.NombreSinExtension(ruta), 300);

        var etiquetas = MarkdownNota.Lista(separado.Datos, true, "tags", "tag")
            .Select(MarkdownNota.NormalizarEtiqueta).OfType<string>()
            .Concat(MarkdownNota.EtiquetasEnLineas(lineas))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(opciones.MaxEtiquetas).ToList();

        var alias = MarkdownNota.Lista(separado.Datos, false, "aliases", "alias")
            .Select(a => Recortar(a, 300)).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();

        // Se ajustan a las columnas de kb.Enlace; un destino imposible de guardar se descarta.
        var enlaces = MarkdownNota.EnlacesEnLineas(lineas, carpeta)
            .Where(e => e.Destino.Length <= 450)
            .Select(e => e with { Seccion = e.Seccion is null ? null : Recortar(e.Seccion, 300), TextoVisible = e.TextoVisible is null ? null : Recortar(e.TextoVisible, 300) })
            .Take(opciones.MaxEnlaces).ToList();

        return new NotaProcesada(
            RutaRelativa: ruta,
            Titulo: titulo,
            Carpeta: carpeta,
            Resumen: MarkdownNota.Resumen(lineas),
            Estado: MarkdownNota.Texto(separado.Datos, "status", "estado") is { } e ? Recortar(e, 50) : null,
            Alias: alias,
            Etiquetas: etiquetas,
            Enlaces: enlaces,
            ContenidoMd: cuerpo,
            ContenidoTexto: MarkdownNota.AplanarMarkdown(cuerpo),
            HashOrigen: hash,
            SecretosEnmascarados: enmascarado.Cantidad,
            LineasConSecretos: enmascarado.Lineas,
            ModificadoEnOrigen: archivo.ModificadoEn,
            Advertencias: advertencias);
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];

    // La ruta se devuelve al navegador en el informe: se limita y se limpia de caracteres de control.
    private static string MostrarRuta(string? ruta) =>
        string.Concat((ruta ?? "").Take(200).Select(c => char.IsControl(c) ? '?' : c));
}
