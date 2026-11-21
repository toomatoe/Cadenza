namespace Cadenza.Core.Exceptions;

public sealed class AppUpdateException : Exception
{
    public AppUpdateException() { }
    public AppUpdateException(string message) : base(message) { }
    public AppUpdateException(string message, Exception innerException) : base(message, innerException) { }
}
