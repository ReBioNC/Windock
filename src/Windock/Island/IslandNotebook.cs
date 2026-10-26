using System.IO;
using Windock.Core.Abstractions;
using Windock.Core.Island;
using Windock.Core.Logging;

namespace Windock.Island;

/// The island's tasks, notes and shelf, kept in Windock's data folder (island-tasks.json, island-notes.json,
/// island-shelf.json).
/// Edits are saved half a second after the last change, and at once on Flush.
internal sealed class IslandNotebook(string directory, IDelayScheduler scheduler, ILog log)
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly string _tasksPath = Path.Combine(directory, "island-tasks.json");
    private readonly string _notesPath = Path.Combine(directory, "island-notes.json");
    private readonly string _shelfPath = Path.Combine(directory, "island-shelf.json");
    private IDisposable? _tasksSave, _notesSave;

    public List<IslandTask> Tasks { get; private set; } = new();
    public List<string> Notes { get; private set; } = new() { "" };
    public List<string> ShelfItems { get; private set; } = new();

    public void Load()
    {
        Tasks = TaskList.Prune(TaskList.Parse(Read(_tasksPath) ?? "[]"), DateTime.Now).ToList();
        Notes = NoteBook.Parse(Read(_notesPath) ?? "[]");
        ShelfItems = Shelf.Prune(Shelf.Parse(Read(_shelfPath) ?? "[]"), p => File.Exists(p) || Directory.Exists(p));
    }

    public void TasksChanged()
    {
        _tasksSave?.Dispose();
        _tasksSave = scheduler.Schedule(SaveDelay, SaveTasks);
    }

    public void NotesChanged()
    {
        _notesSave?.Dispose();
        _notesSave = scheduler.Schedule(SaveDelay, SaveNotes);
    }

    public void ShelfChanged(List<string> items)
    {
        ShelfItems = items;
        Write(_shelfPath, Shelf.Serialize(items));  // rare and small: saved at once
    }

    public void Flush()
    {
        if (_tasksSave is not null) SaveTasks();
        if (_notesSave is not null) SaveNotes();
    }

    private void SaveTasks()
    {
        _tasksSave?.Dispose();
        _tasksSave = null;
        Write(_tasksPath, TaskList.Serialize(Tasks));
    }

    private void SaveNotes()
    {
        _notesSave?.Dispose();
        _notesSave = null;
        Write(_notesPath, NoteBook.Serialize(Notes));
    }

    private string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"cannot read {path}", ex);
            return null;
        }
    }

    private void Write(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"cannot save {path}", ex);
        }
    }
}
