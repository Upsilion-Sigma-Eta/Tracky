namespace Tracky.Core.Issues;

public sealed record IssueTypeDefinition(
    Guid Id,
    string Name,
    string ColorHex,
    string Description);
