using System.Globalization;
using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueRelationViewModel(IssueRelation relation) : ViewModelBase
{
    public IssueRelation Relation { get; } = relation;

    public string TargetText => $"#{Relation.TargetIssueNumber} {Relation.TargetIssueTitle}";

    public string RelationTypeText => Relation.RelationType switch
    {
        IssueRelationType.BlockedBy => "Blocked by",
        IssueRelationType.DuplicateOf => "Duplicate of",
        IssueRelationType.DuplicatedBy => "Duplicated by",
        IssueRelationType.Parent => "Parent",
        IssueRelationType.Child => "Child",
        IssueRelationType.Blocks => "Blocks",
        _ => "Related",
    };

    public string CreatedText => Relation.CreatedAtUtc
        .ToLocalTime()
        .ToString("MMM dd, HH:mm", CultureInfo.CurrentCulture);
}
