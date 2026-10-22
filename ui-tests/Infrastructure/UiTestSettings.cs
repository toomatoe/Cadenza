using System.Globalization;
using System.IO;

namespace Cadenza.UI.Tests.Infrastructure;

/// <summary>Run settings for the UI tests (from <c>headless.runsettings</c> or the command line).</summary>
public sealed class UiTestSettings
{
    private const string HeadlessKey = "Headless";
    private const string AppPathKey = "AppPath";
    private const string TimeoutKey = "TimeoutSeconds";
    private const string ReleaseBuild = @"desktop\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Cadenza.Desktop.exe";
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);

    public UiTestSettings(bool headless, string appPath, TimeSpan timeout)
    {
        Headless = headless;
        AppPath = appPath;
        Timeout = timeout;
    }

    /// <summary>Run the app off-screen without taking focus (the default).</summary>
    public bool Headless { get; }

    /// <summary>The Cadenza.Desktop.exe under test.</summary>
    public string AppPath { get; }

    /// <summary>How long to wait for the window and for pages to settle.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Reads the settings, defaulting to headless and the Release build in <c>desktop/bin</c>.</summary>
    public static UiTestSettings From(TestContext context)
    {
        var headless = !string.Equals(Value(context, HeadlessKey), "false", StringComparison.OrdinalIgnoreCase);
        var appPath = Value(context, AppPathKey) is { Length: > 0 } path ? path : Path.Combine(RepositoryRoot(), ReleaseBuild);
        var timeout = double.TryParse(Value(context, TimeoutKey), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : _defaultTimeout;

        return new UiTestSettings(headless, appPath, timeout);
    }

    private static string Value(TestContext context, string key) =>
        context.Properties.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";

    /// <summary>The folder holding Cadenza.slnx, found by walking up from the test assembly.</summary>
    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Cadenza.slnx")))
                return folder.FullName;
        }

        throw new InvalidOperationException("Couldn't find the repository root (Cadenza.slnx) above the test assembly.");
    }
}
