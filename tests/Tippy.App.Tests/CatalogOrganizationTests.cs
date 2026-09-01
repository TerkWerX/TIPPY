using Tippy.App.Services;

namespace Tippy.App.Tests;

public sealed class CatalogOrganizationTests
{
    [Fact]
    public void WindowsCatalogSeparatesShortcutsFromIndividualKeys()
    {
        var catalog = WindowsActionCatalog.Create();
        var shortcuts = catalog.Where(action => !action.IsDirectKey).ToArray();
        var keys = catalog.Where(action => action.IsDirectKey).ToArray();

        Assert.Equal(186, catalog.Count);
        Assert.Equal(61, shortcuts.Length);
        Assert.Equal(125, keys.Length);
        Assert.Equal(
            ["Editing & files", "Windows & system", "Virtual desktops", "Web browsers & tabs", "Media & volume", "Navigation & display"],
            shortcuts.Select(action => action.Category).Distinct());
        Assert.Equal(9, keys.Select(action => action.Category).Distinct().Count());
        Assert.All(keys, key => Assert.Contains(key.Category, key.SearchText));
    }

    [Fact]
    public void ApplicationCatalogUsesPurposeBasedCommandCategories()
    {
        var applications = ApplicationShortcutCatalog.Create();
        var shortcuts = applications.SelectMany(application => application.Shortcuts).ToArray();

        Assert.Equal(32, applications.Count);
        Assert.Equal(557, shortcuts.Length);
        Assert.DoesNotContain(shortcuts, shortcut => shortcut.Category == "Commands");
        Assert.True(shortcuts.Select(shortcut => shortcut.Category).Distinct().Count() >= 8);
        Assert.Contains(shortcuts, shortcut => shortcut.Category == "Live production");
        Assert.Contains(shortcuts, shortcut => shortcut.Category == "Playback & recording");
        Assert.Contains(shortcuts, shortcut => shortcut.Category == "Files & projects");
        Assert.Contains(shortcuts, shortcut => shortcut.Category == "View & navigation");
    }
}
