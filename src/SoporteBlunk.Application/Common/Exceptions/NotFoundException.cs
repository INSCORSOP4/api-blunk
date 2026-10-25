namespace SoporteBlunk.Application.Common.Exceptions;

public sealed class NotFoundException(string recurso, object clave)
    : Exception($"No se encontró {recurso} con identificador '{clave}'.");
