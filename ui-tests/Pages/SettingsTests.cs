using System.Windows.Automation;
using Cadenza.UI.Tests.Infrastructure;

namespace Cadenza.UI.Tests.Pages;

/// <summary>Reads the Settings page without changing anything.</summary>
[TestClass]
public sealed class SettingsTests : UiTestBase
{
    private static readonly string[] _animals = ["Cat", "Fox", "Frog", "Bunny", "Chick", "Axolotl", "Panda"];

    [TestInitialize]
    public void OpenSettings() => App.Navigate("Settings", "Settings");

    [TestMethod]
    public void SettingsShowsTheProfileEditor()
    {
        App.WaitUntil(() => App.Names().Contains("Profile"), "the Profile card");
        Assert.IsNotNull(App.ById("SpeciesBox"));
    }

    [TestMethod]
    public void CompanionListIncludesEveryAnimal()
    {
        var species = App.ById("SpeciesBox");
        var expand = (ExpandCollapsePattern)species.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        expand.Expand();
        try
        {
            App.WaitUntil(() => species.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Count > 0,
                "the companion list to open");
            var animals = species.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>()
                .Select(item => item.Current.Name)
                .ToArray();

            CollectionAssert.IsSubsetOf(_animals, animals);
        }
        finally
        {
            expand.Collapse();
        }
    }
}
