namespace EndJinx.Logging;

public interface ILogger
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class ConsoleLogger : ILogger
{
    public void Info(string message)
    {
        Console.WriteLine(message);
    }

    public void Error(string message, Exception? exception = null)
    {
        Console.Error.WriteLine(exception is null ? message : $"{message}: {exception}");
    }
}
