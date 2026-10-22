using System.Diagnostics;
using System.IO;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Cadenza.UI.Tests.Infrastructure;

/// <summary>
/// Drives a running Cadenza window through Windows UI Automation. In headless mode the window is moved
/// off-screen and shown without activation, so it never covers or steals focus from what you're doing.
/// Element lookups wait (up to <see cref="UiTestSettings.Timeout"/>) because WinUI builds pages lazily.
/// </summary>
public sealed class CadenzaApp : IDisposable
{
    private const string PageTitleId = "PageTitle";
    private const string ConnectionLabelId = "ConnectionLabel";
    private const string Disconnected = "Spotify disconnected";
    private const int OffScreenGap = 200;
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(150);

    private readonly Process _process;
    private readonly UiTestSettings _settings;

    private CadenzaApp(Process process, IntPtr window, UiTestSettings settings)
    {
        _process = process;
        _settings = settings;
        Window = window;
        Root = AutomationElement.FromHandle(window);
    }

    /// <summary>The main window's handle.</summary>
    public IntPtr Window { get; }

    /// <summary>The UI Automation element for the main window.</summary>
    public AutomationElement Root { get; }

    /// <summary>Starts the app and waits until the main window and its page title exist.</summary>
    /// <exception cref="AssertInconclusiveException">The app hasn't been built.</exception>
    public static CadenzaApp Launch(UiTestSettings settings)
    {
        if (!File.Exists(settings.AppPath))
            Assert.Inconclusive($"Build the app first (powershell -File .\\build.ps1 -UiOnly). Not found: {settings.AppPath}");

        var process = Process.Start(new ProcessStartInfo(settings.AppPath) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Cadenza didn't start.");
        var window = WaitFor(() =>
        {
            process.Refresh();
            return process.MainWindowHandle;
        }, handle => handle != IntPtr.Zero, settings.Timeout, "the Cadenza window");

        if (settings.Headless)
            MoveOffScreen(window);

        var app = new CadenzaApp(process, window, settings);
        app.ById(PageTitleId);

        return app;
    }

    /// <summary>Whether Spotify is signed in (the sidebar status isn't "Spotify disconnected").</summary>
    public bool IsSignedIn => !string.Equals(ById(ConnectionLabelId).Current.Name, Disconnected, StringComparison.Ordinal);

    /// <summary>The page heading ("Your library", "For you", …).</summary>
    public string PageTitle => ById(PageTitleId).Current.Name;

    /// <summary>Finds an element by its automation ID (the XAML <c>x:Name</c>), waiting for it to appear.</summary>
    public AutomationElement ById(string automationId) =>
        Find(new PropertyCondition(AutomationElement.AutomationIdProperty, automationId), $"element '{automationId}'");

    /// <summary>Finds an element by its accessible name, optionally of a given control type.</summary>
    public AutomationElement ByName(string name, ControlType? type = null)
    {
        Condition condition = new PropertyCondition(AutomationElement.NameProperty, name);
        if (type is not null)
            condition = new AndCondition(condition, new PropertyCondition(AutomationElement.ControlTypeProperty, type));

        return Find(condition, $"'{name}'");
    }

    /// <summary>Whether an element with this automation ID exists right now (no waiting).</summary>
    public bool Exists(string automationId) =>
        Root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)) is not null;

    /// <summary>Clicks a button-like element (invoke, select or toggle, whichever it supports).</summary>
    public static void Click(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
            ((InvokePattern)invoke).Invoke();
        else if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
            ((SelectionItemPattern)select).Select();
        else if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
            ((TogglePattern)toggle).Toggle();
        else
            throw new InvalidOperationException($"'{element.Current.Name}' can't be clicked through UI Automation.");
    }

    /// <summary>Opens a page from the sidebar and waits for its heading.</summary>
    public void Navigate(string navigationName, string expectedTitle)
    {
        Click(ByName(navigationName, ControlType.Button));
        WaitUntil(() => PageTitle == expectedTitle, $"the '{expectedTitle}' page");
    }

    /// <summary>Replaces a text box's text (raises its text-changed handlers, like typing).</summary>
    public void SetText(string automationId, string text) =>
        ((ValuePattern)ById(automationId).GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);

    /// <summary>The list items currently realised in a list.</summary>
    public IReadOnlyList<AutomationElement> ListItems(string listAutomationId) =>
        ById(listAutomationId).FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
            .Cast<AutomationElement>()
            .ToArray();

    /// <summary>Every element's accessible name under <paramref name="scope"/> (default: the whole window).</summary>
    public IReadOnlyList<string> Names(AutomationElement? scope = null) =>
        (scope ?? Root).FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Select(element => element.Current.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToArray();

    /// <summary>Waits until <paramref name="condition"/> holds, failing the test with <paramref name="what"/> otherwise.</summary>
    public void WaitUntil(Func<bool> condition, string what) =>
        WaitFor(() => SafeCheck(condition), ok => ok, _settings.Timeout, what);

    /// <summary>Saves a picture of the window (works off-screen).</summary>
    public void Screenshot(string path)
    {
        if (!NativeMethods.GetWindowRect(Window, out var rect))
            return;

        using var bitmap = new Bitmap(Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var context = graphics.GetHdc();
            NativeMethods.PrintWindow(Window, context, NativeMethods.RenderFullContent);
            graphics.ReleaseHdc(context);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    public void Dispose()
    {
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);
        _process.Dispose();
    }

    private AutomationElement Find(Condition condition, string what) =>
        WaitFor(() => Root.FindFirst(TreeScope.Descendants, condition), element => element is not null, _settings.Timeout, what)!;

    private static bool SafeCheck(Func<bool> condition)
    {
        try
        {
            return condition();
        }
        catch (Exception error) when (error is ElementNotAvailableException or COMException or AssertFailedException)
        {
            // Elements come and go while WinUI re-renders; try again.
            return false;
        }
    }

    private static T WaitFor<T>(Func<T> probe, Func<T, bool> done, TimeSpan timeout, string what)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            T value;
            try
            {
                value = probe();
            }
            catch (Exception error) when (error is ElementNotAvailableException or COMException)
            {
                value = default!;
            }

            if (done(value))
                return value;
            if (watch.Elapsed > timeout)
                Assert.Fail($"Timed out after {timeout.TotalSeconds:0} s waiting for {what}.");

            Thread.Sleep(_pollInterval);
        }
    }

    private static void MoveOffScreen(IntPtr window)
    {
        NativeMethods.GetWindowRect(window, out var rect);
        var width = Math.Max(rect.Right - rect.Left, 1200);
        var height = Math.Max(rect.Bottom - rect.Top, 800);
        var placement = new NativeMethods.WindowPlacement
        {
            Length = Marshal.SizeOf<NativeMethods.WindowPlacement>(),
            ShowCommand = NativeMethods.ShowNoActivate,
            MinX = -1,
            MinY = -1,
            MaxX = -1,
            MaxY = -1,
            Normal = new NativeMethods.Rect { Left = -width - OffScreenGap, Top = OffScreenGap, Right = -OffScreenGap, Bottom = OffScreenGap + height },
        };
        NativeMethods.SetWindowPlacement(window, ref placement);
    }
}
