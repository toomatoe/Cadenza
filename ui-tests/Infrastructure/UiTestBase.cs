using System.IO;

namespace Cadenza.UI.Tests.Infrastructure;

/// <summary>
/// Starts one Cadenza window per test class (launching is the slow part), closes it afterwards, and
/// attaches a screenshot to any test that fails.
/// </summary>
public abstract class UiTestBase
{
    private const string NotSignedIn =
        "Cadenza isn't signed in to Spotify on this machine. Open the app once and connect Spotify in Settings, " +
        "or provide tokens in CI (see ui-tests/README.md).";

    /// <summary>The app shared by the tests in the current class.</summary>
    protected static CadenzaApp App { get; private set; } = null!;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static void StartApp(TestContext context) => App = CadenzaApp.Launch(UiTestSettings.From(context));

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static void CloseApp() => App?.Dispose();

    [TestCleanup]
    public void CaptureFailure()
    {
        if (TestContext.CurrentTestOutcome == UnitTestOutcome.Passed || App is null)
            return;

        var path = Path.Combine(TestContext.TestRunResultsDirectory ?? Path.GetTempPath(), $"{TestContext.TestName}.png");
        App.Screenshot(path);
        TestContext.AddResultFile(path);
    }

    /// <summary>Skips (Inconclusive) when the test needs Spotify data and the app isn't signed in.</summary>
    protected static void RequireSignIn()
    {
        if (!App.IsSignedIn)
            Assert.Inconclusive(NotSignedIn);
    }
}
