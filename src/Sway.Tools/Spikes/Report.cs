namespace Sway.Tools.Spikes;

/// <summary>Writes each line to the console and to a text file next to the program, so the user can paste it back.</summary>
internal sealed class Report : IDisposable
{
    private readonly StreamWriter _file;

    internal Report(string fileName)
    {
        Path = System.IO.Path.Combine(AppContext.BaseDirectory, fileName);
        _file = new StreamWriter(Path, append: false) { AutoFlush = true };
    }

    internal string Path { get; }

    internal void Line(string text = "")
    {
        Console.WriteLine(text);
        _file.WriteLine(text);
    }

    public void Dispose() => _file.Dispose();
}
