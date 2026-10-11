namespace Cadenza.Tests.Infrastructure;

internal sealed class CheckRunner
{
    public int Passed { get; private set; }

    public void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Passed++;
    }

    public void Throws<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            Passed++;
            return;
        }
        throw new InvalidOperationException(message);
    }

    public async Task ThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            Passed++;
            return;
        }
        throw new InvalidOperationException(message);
    }
}
