namespace OmniNode.Middleware;

internal sealed class RoutineRegistry
{
    private readonly IRoutineStore _routineStore;

    public object SyncRoot { get; } = new();
    public string StorePath { get; }

    public Dictionary<string, RoutineDefinition> RoutinesById { get; } = new(StringComparer.OrdinalIgnoreCase);

    public RoutineRegistry(IRoutineStore routineStore)
    {
        _routineStore = routineStore;
        StorePath = routineStore.StorePath;
    }

    public void Load()
    {
        lock (SyncRoot)
        {
            RoutinesById.Clear();
            var recoveredRunningCount = 0;
            try
            {
                foreach (var item in _routineStore.Load())
                {
                    if (string.IsNullOrWhiteSpace(item.Id))
                    {
                        continue;
                    }

                    if (item.Running)
                    {
                        item.Running = false;
                        recoveredRunningCount += 1;
                    }

                    RoutinesById[item.Id] = item;
                }

                if (recoveredRunningCount > 0)
                {
                    SaveLocked();
                    Console.Error.WriteLine($"[routine] recovered {recoveredRunningCount} stale running flag(s) on load");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[routine] state load failed: {ex.Message}");
            }
        }
    }

    public T ReadAll<T>(Func<IEnumerable<RoutineDefinition>, T> read)
    {
        lock (SyncRoot)
        {
            return read(RoutinesById.Values);
        }
    }

    public T Read<T>(string routineId, Func<RoutineDefinition?, T> read)
    {
        lock (SyncRoot)
        {
            RoutinesById.TryGetValue(routineId, out var routine);
            return read(routine);
        }
    }

    public void Mutate(Action<Dictionary<string, RoutineDefinition>> write)
    {
        lock (SyncRoot)
        {
            write(RoutinesById);
            SaveLocked();
        }
    }

    public T Mutate<T>(Func<Dictionary<string, RoutineDefinition>, T> write)
    {
        lock (SyncRoot)
        {
            var result = write(RoutinesById);
            SaveLocked();
            return result;
        }
    }

    public T MutateIfChanged<T>(Func<Dictionary<string, RoutineDefinition>, (T Result, bool Changed)> write)
    {
        lock (SyncRoot)
        {
            var (result, changed) = write(RoutinesById);
            if (changed)
            {
                SaveLocked();
            }

            return result;
        }
    }

    public void SaveLocked()
    {
        try
        {
            _routineStore.Save(RoutinesById.Values.ToArray());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[routine] state save failed: {ex.Message}");
        }
    }
}
