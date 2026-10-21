using System.Text.Json;
using BkIncidentes.Domain;

namespace BkIncidentes.Data;

/// <summary>
/// Almacén persistente. Es una interfaz a propósito: aquí se usa un archivo JSON (cero dependencias,
/// «dotnet run» funciona sin restaurar paquetes) y puede sustituirse por SQLite/EF Core sin tocar
/// controladores ni vistas.
/// </summary>
public interface IDataStore
{
    /// <summary>Ejecuta una lectura bajo candado. El delegado debe devolver copias o valores, no referencias vivas.</summary>
    T Read<T>(Func<DataSet, T> read);

    /// <summary>Ejecuta una escritura bajo candado y persiste el resultado de forma atómica.</summary>
    T Write<T>(Func<DataSet, T> write);
}

public sealed class JsonFileDataStore : IDataStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private DataSet _data;

    public JsonFileDataStore(string path, Action<DataSet>? seed = null)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path))
        {
            _data = JsonSerializer.Deserialize<DataSet>(File.ReadAllText(path), Options) ?? new DataSet();
        }
        else
        {
            _data = new DataSet();
            seed?.Invoke(_data);
            Persist();
        }
    }

    public string Path_ => _path;

    public T Read<T>(Func<DataSet, T> read)
    {
        lock (_gate) return read(_data);
    }

    public T Write<T>(Func<DataSet, T> write)
    {
        lock (_gate)
        {
            // Se trabaja sobre el documento vivo; si falla a la mitad, se recarga desde disco
            // para no dejar la memoria en un estado distinto del archivo.
            try
            {
                var result = write(_data);
                Persist();
                return result;
            }
            catch
            {
                _data = File.Exists(_path)
                    ? JsonSerializer.Deserialize<DataSet>(File.ReadAllText(_path), Options) ?? new DataSet()
                    : new DataSet();
                throw;
            }
        }
    }

    /// <summary>Escritura atómica: archivo temporal + reemplazo, para no corromper datos si el proceso se interrumpe.</summary>
    private void Persist()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, Options));
        File.Move(tmp, _path, overwrite: true);
    }
}
