using System.Text;
using PassDpi.Core;

namespace PassDpi.Service;

/// <summary>
/// Size-capped rotating log file (passdpi.log, .1, .2 at 512 KB each) plus an in-memory
/// ring buffer the UI can fetch. Raises <see cref="LineWritten"/> so the pipe can stream lines live.
/// </summary>
public sealed class FileLog : ILog
{
    private const long MaxBytes = 512 * 1024;
    private const int Keep = 2;
    private const int RingSize = 300;

    private readonly object _gate = new();
    private readonly Queue<string> _ring = new();
    private readonly string _dir;
    private StreamWriter? _writer;
    private bool _closed;

    public event Action<string>? LineWritten;

    public FileLog(string dir) { _dir = dir; }

    private string FilePath => Path.Combine(_dir, "passdpi.log");

    public void Write(LogLevel level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level.ToString().ToUpperInvariant(),-5} {message}";
        lock (_gate)
        {
            _ring.Enqueue(line);
            while (_ring.Count > RingSize) _ring.Dequeue();
            if (!_closed)
            {
                try
                {
                    EnsureWriter();
                    _writer!.WriteLine(line);
                    _writer.Flush();
                    if (_writer.BaseStream.Length > MaxBytes) Rotate();
                }
                catch { /* logging must never take the service down */ }
            }
        }
        try { LineWritten?.Invoke(line); } catch { }
    }

    public List<string> Snapshot() { lock (_gate) return _ring.ToList(); }

    private void EnsureWriter()
    {
        if (_writer != null) return;
        Directory.CreateDirectory(_dir);
        var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(fs, new UTF8Encoding(false));
    }

    private void Rotate()
    {
        _writer?.Dispose();
        _writer = null;
        for (var i = Keep; i >= 1; i--)
        {
            var src = i == 1 ? FilePath : Path.Combine(_dir, $"passdpi.{i - 1}.log");
            var dst = Path.Combine(_dir, $"passdpi.{i}.log");
            if (File.Exists(src)) File.Move(src, dst, overwrite: true);
        }
    }

    /// <summary>Releases the file (used before uninstall deletes the folder). In-memory logging continues.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
