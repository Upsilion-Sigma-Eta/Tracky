using Tracky.Core.Search;

namespace Tracky.App.ViewModels;

public sealed class SavedIssueSearchViewModel(SavedIssueSearch savedSearch) : ViewModelBase
{
    public SavedIssueSearch SavedSearch { get; } = savedSearch;

    public string Name => SavedSearch.IsPinned ? $"{SavedSearch.Name} (Pinned)" : SavedSearch.Name;

    public string QueryText => string.IsNullOrWhiteSpace(SavedSearch.QueryText)
        ? "No query"
        : SavedSearch.QueryText;
}
