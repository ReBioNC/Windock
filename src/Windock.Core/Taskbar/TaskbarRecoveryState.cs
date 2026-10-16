using System.Text.Json;

namespace Windock.Core.Taskbar;

public sealed record TaskbarRecoveryState(bool TaskbarModified, int OriginalAbState, int Pid);

/// state.json: what Windock changed on the taskbar, written before the change so a crash can be undone.
public sealed class RecoveryStateStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _path = Path.Combine(directory, "state.json");

    public TaskbarRecoveryState? Read()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<TaskbarRecoveryState>(File.ReadAllText(_path), Options) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public void Write(TaskbarRecoveryState state)
    {
        Directory.CreateDirectory(directory);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, Options));
        File.Move(tmp, _path, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
