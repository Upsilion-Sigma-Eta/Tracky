namespace Tracky.Core.Projects;

public sealed record MoveProjectItemInput(
    Guid ProjectItemId,
    string BoardColumn);
