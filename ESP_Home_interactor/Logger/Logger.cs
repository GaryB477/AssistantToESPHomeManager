namespace ESP_Home_Interactor.helper;

public class Logger(bool DebugEnabled = false)
{
    public bool DebugEnabled { get; set; }

    private readonly string _prefixIncoming = "←";
    private readonly string _prefixOutgoing= "→";
    private readonly string _prefixWarning = "⚠";
    private readonly string _prefixSuccess = "✓";
    private readonly string _prefixError = "✗";
    private readonly string _prefixSeparator = "━━━";

    public void LogOutgoing(string message)
    {
        Log($"{_prefixOutgoing} {message}");
    }
    public void LogIncoming(string message)
    {
        Log($"{_prefixIncoming} {message}");
    }
    public void LogWarning(string message)
    {
        Log($"{_prefixWarning} {message}");
    }
    public void LogSuccess(string message)
    {
        Log($"{_prefixSuccess} {message}");
    }
    public void LogError(string message)
    {
        Log($"{_prefixError} {message}");
    }
    public void LogSeparator(string message)
    {
        Log($"{_prefixSeparator} {message} {_prefixSeparator}");
    }
    public void Log(string message)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
    }
    public void LogEmpty()
    {
        Console.WriteLine();
    }
}