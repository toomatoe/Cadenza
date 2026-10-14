namespace Cadenza.Desktop.Models;

/// <summary>How a notice is shown in the window's notice bar.</summary>
public enum NoticeLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>A message for the notice bar, raised by view models that don't know about WinUI.</summary>
public sealed class Notice
{
    public Notice(string text, NoticeLevel level = NoticeLevel.Info)
    {
        Text = text;
        Level = level;
    }

    public string Text { get; }

    public NoticeLevel Level { get; }
}
