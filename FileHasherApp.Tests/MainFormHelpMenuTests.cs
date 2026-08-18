using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Help menu and in-app help window tests. Each test method gets its own app
/// instance so menu/window state never leaks between tests. Content
/// assertions reference HelpContent directly (via the project reference), so
/// they stay in sync with the shipped topics without hardcoded copies.
/// </summary>
[Collection("Serial")]
public sealed class MainFormHelpMenuTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    public MainFormHelpMenuTests() => _fixture = new AppFixture();
    public void Dispose()          => _fixture.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Window OpenHelpWindow()
    {
        Win.FindFirstDescendant(cf => cf.ByName("Help")).AsMenuItem().Click();
        Win.FindFirstDescendant(cf => cf.ByAutomationId("MiHelpContents")).AsMenuItem().Click();

        var help = _fixture.WaitForTopLevelWindow("FileHasher Help", TimeSpan.FromSeconds(10));
        Assert.NotNull(help);
        return help!;
    }

    private static void SelectTopic(Window help, string title)
    {
        var list = help.FindFirstDescendant(cf => cf.ByAutomationId("HelpTopicsList"));
        Assert.NotNull(list);
        var item = list.FindFirstDescendant(cf => cf.ByName(title));
        Assert.NotNull(item);
        item.AsListBoxItem().ScrollIntoView().Select();
    }

    private static string ContentText(Window help)
    {
        var box = help.FindFirstDescendant(cf => cf.ByAutomationId("HelpContentBox"));
        Assert.NotNull(box);
        return box.AsTextBox().Text;
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void HelpMenu_ContainsContentsSupportAndPrivacyItems()
    {
        Win.FindFirstDescendant(cf => cf.ByName("Help")).AsMenuItem().Click();

        Assert.NotNull(Win.FindFirstDescendant(cf => cf.ByAutomationId("MiHelpContents")));
        Assert.NotNull(Win.FindFirstDescendant(cf => cf.ByAutomationId("MiSupportWebsite")));
        Assert.NotNull(Win.FindFirstDescendant(cf => cf.ByAutomationId("MiPrivacyPolicy")));
        // The About item must survive the menu expansion too.
        Assert.NotNull(Win.FindFirstDescendant(cf => cf.ByName("About FileHasher…")));
    }

    [Fact]
    public void HelpWindow_OpensAndListsEveryTopic()
    {
        var help = OpenHelpWindow();

        var list  = help.FindFirstDescendant(cf => cf.ByAutomationId("HelpTopicsList"));
        var items = list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
        Assert.Equal(HelpContent.Topics.Length, items.Length);

        // First topic is rendered on open.
        Assert.Contains(HelpContent.Topics[0].Title, ContentText(help));
    }

    [Fact]
    public void HelpWindow_TopicSelectionUpdatesContent()
    {
        var help = OpenHelpWindow();

        SelectTopic(help, "Verifying Sidecars");
        var text = ContentText(help);
        Assert.Contains("MISMATCH", text);
        Assert.Contains("NO SIDECAR", text);
    }

    [Fact]
    public void HelpWindow_SupportTopicShowsVersionStampedSubject()
    {
        var help = OpenHelpWindow();

        SelectTopic(help, "Support");
        Assert.Contains($"FileHasher-Windows-{HelpContent.AppVersion}", ContentText(help));

        // The mailto link itself carries the same version-stamped subject.
        Assert.NotNull(help.FindFirstDescendant(cf => cf.ByAutomationId("HelpEmailLink")));
        Assert.Contains($"subject=FileHasher-Windows-{HelpContent.AppVersion}",
                        HelpContent.SupportMailto);
    }

    [Fact]
    public void HelpWindow_ReopeningActivatesExistingInstance()
    {
        _ = OpenHelpWindow();
        _ = OpenHelpWindow();   // second invocation must not create a duplicate

        Thread.Sleep(500);      // give a hypothetical duplicate time to appear
        Assert.Equal(1, _fixture.CountTopLevelWindows("FileHasher Help"));
    }
}
