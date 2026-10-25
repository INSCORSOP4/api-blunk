using System.IO.Compression;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Common.Options;

namespace SoporteBlunk.Application.Conocimiento;

/// <summary>
/// Extrae los archivos de un .zip subido, con topes contra bombas de compresión: máximo de entradas,
/// lectura acotada por archivo (se lee como mucho un byte más que el límite) y un presupuesto total de bytes descomprimidos.
/// No escribe nada en disco; los nombres se validan después con <see cref="RutaNota"/>.
/// </summary>
public static class LectorZip
{
    /// <summary>Presupuesto compartido entre todos los zips de una misma subida.</summary>
    public sealed class Presupuesto(long bytesRestantes)
    {
        public long BytesRestantes { get; private set; } = bytesRestantes;

        public void Consumir(long bytes) => BytesRestantes -= bytes;
    }

    public static IReadOnlyList<ArchivoSubido> Leer(Stream zip, ConocimientoOptions opciones, Presupuesto presupuesto, string nombreZip)
    {
        var archivos = new List<ArchivoSubido>();
        try
        {
            using var arch = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            if (arch.Entries.Count > opciones.MaxEntradasZip)
                throw new SubidaInvalidaException($"El zip '{Corto(nombreZip)}' tiene más de {opciones.MaxEntradasZip} entradas.");

            foreach (var entrada in arch.Entries)
            {
                // Carpetas (nombre vacío) y archivos que claramente no son notas no se descomprimen.
                if (string.IsNullOrEmpty(entrada.Name)) continue;
                var ruta = entrada.FullName;
                if (!ruta.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    archivos.Add(new ArchivoSubido(ruta, [], null)); // el procesador lo informará como omitido
                    continue;
                }

                var tope = (long)opciones.MaxBytesPorArchivo + 1; // un byte de más basta para saber que se pasó
                if (presupuesto.BytesRestantes <= 0)
                    throw new SubidaInvalidaException("El contenido descomprimido supera el tamaño total permitido.");

                var limite = (int)Math.Min(tope, presupuesto.BytesRestantes);
                using var origen = entrada.Open();
                var contenido = LeerAcotado(origen, limite);
                // Si el tope fue el presupuesto (y no el límite por archivo) y se agotó, el archivo quedaría truncado: se rechaza.
                if (contenido.Length == limite && limite < tope)
                    throw new SubidaInvalidaException("El contenido descomprimido supera el tamaño total permitido.");
                presupuesto.Consumir(contenido.Length);
                archivos.Add(new ArchivoSubido(ruta, contenido, entrada.LastWriteTime));
            }
        }
        catch (InvalidDataException)
        {
            throw new SubidaInvalidaException($"El archivo '{Corto(nombreZip)}' no es un .zip válido.");
        }
        catch (NotSupportedException)
        {
            throw new SubidaInvalidaException($"El zip '{Corto(nombreZip)}' usa un formato no soportado (¿cifrado?).");
        }
        return archivos;
    }

    /// <summary>Lee como máximo <paramref name="maximo"/> bytes.</summary>
    public static byte[] LeerAcotado(Stream s, int maximo)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81_920];
        var restante = maximo;
        int leidos;
        while (restante > 0 && (leidos = s.Read(buffer, 0, Math.Min(buffer.Length, restante))) > 0)
        {
            ms.Write(buffer, 0, leidos);
            restante -= leidos;
        }
        return ms.ToArray();
    }

    private static string Corto(string nombre) => nombre.Length <= 80 ? nombre : nombre[..80] + "…";
}
