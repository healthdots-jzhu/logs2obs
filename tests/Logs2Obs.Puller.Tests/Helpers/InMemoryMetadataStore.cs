namespace Logs2Obs.Puller.Tests.Helpers;

using System.Runtime.CompilerServices;

public sealed class InMemoryMetadataStore : IMetadataStore
{
    private readonly Dictionary<string, Dictionary<string, object>> _tables = new(StringComparer.Ordinal);

    public Task<T?> GetAsync<T>(string table, string key, CancellationToken ct = default)
    {
        if (_tables.TryGetValue(table, out var tableEntries)
            && tableEntries.TryGetValue(key, out var value))
        {
            return Task.FromResult((T?)value);
        }

        return Task.FromResult<T?>(default);
    }

    public Task PutAsync<T>(string table, T entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var key = GetEntityKey(entity);
        if (!_tables.TryGetValue(table, out var tableEntries))
        {
            tableEntries = new Dictionary<string, object>(StringComparer.Ordinal);
            _tables[table] = tableEntries;
        }

        tableEntries[key] = entity;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string table, string key, CancellationToken ct = default)
    {
        if (_tables.TryGetValue(table, out var tableEntries))
        {
            tableEntries.Remove(key);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<T> QueryAsync<T>(
        string table,
        Func<T, bool> filter,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_tables.TryGetValue(table, out var tableEntries))
        {
            foreach (var value in tableEntries.Values.OfType<T>())
            {
                if (filter(value))
                {
                    yield return value;
                    await Task.Yield();
                }
            }
        }
    }

    public bool TryGet(string table, string key, out object? value)
    {
        value = null;
        if (_tables.TryGetValue(table, out var tableEntries)
            && tableEntries.TryGetValue(key, out var stored))
        {
            value = stored;
            return true;
        }

        return false;
    }

    public static T GetPropertyValue<T>(object record, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(record);
        var property = record.GetType().GetProperty(propertyName);
        if (property is null)
        {
            throw new InvalidOperationException($"Property {propertyName} not found on {record.GetType().Name}.");
        }

        return (T)property.GetValue(record)!;
    }

    private static string GetEntityKey<T>(T entity)
    {
        var keyProperty = entity?.GetType().GetProperty("Key");
        if (keyProperty == null)
        {
            throw new InvalidOperationException("Entity does not expose a Key property.");
        }

        var key = keyProperty.GetValue(entity) as string;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Entity Key cannot be null or whitespace.");
        }

        return key;
    }
}
