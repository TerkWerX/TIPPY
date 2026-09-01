namespace Tippy.App.Models;

public sealed record CatalogCategoryFilter(string Name, int Count)
{
    public string CountLabel => $"{Count} {(Count == 1 ? "item" : "items")}";
}
