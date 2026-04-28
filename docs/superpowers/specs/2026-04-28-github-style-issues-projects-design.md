# GitHub-Style Issues And Projects Design

## 1. Purpose

This design records the approved direction for changing Tracky's primary UX into a local repository-style work surface focused on Issues and Projects. The goal is not to copy GitHub's protected branding, mascot, icon artwork, screenshots, CSS, or exact visual assets. The goal is to make Tracky's issue and project management feel immediately familiar to users who already understand GitHub Issues and GitHub Projects.

The user approved the "Full Repository Shell" approach. Tracky should feel like a local repository page where `Issues` and `Projects` are the main tabs, while non-GitHub helper features move behind lower-priority controls.

## 2. Approved Direction

Tracky will keep English UI labels for GitHub-like work surfaces. Labels such as `Issues`, `Projects`, `Labels`, `Milestones`, `New issue`, `Saved views`, `Table`, `Board`, `Group`, `Sort`, and `Fields` should remain in English because they are part of the expected workflow vocabulary.

The main screen should use a repository header, tab navigation, search/filter toolbar, issue list, project saved views, and project table/board controls. The experience should be familiar, dense, and operational rather than decorative or marketing-like.

Calendar, Timeline, Reminders, Export, and other Tracky-specific helper features should stay available, but they should move to `More`, a secondary side panel, or another lower-priority area. They should not compete visually with the Issues and Projects workflows.

## 3. Copyright And Brand Guardrails

The implementation must avoid GitHub-owned brand assets and copied implementation details. Do not use Octocat, GitHub logos, GitHub screenshots, copied GitHub SVGs, copied GitHub CSS, copied color token names, or pixel-identical recreation of GitHub pages.

The implementation may use common product-management patterns such as repository-like headers, tab navigation, issue lists, search qualifier input, saved views, table layouts, board layouts, grouping, sorting, fields, open and closed issue scopes, labels, milestones, and metadata side panels. These are workflow patterns rather than protected brand assets.

Tracky should use its existing Avalonia components and local resource brushes where practical. If colors or spacing are adjusted, they should be Tracky's own values and should serve readability, density, and desktop usability rather than exact GitHub replication.

## 4. Information Architecture

The first-level work surface is a repository shell. The header displays the workspace/repository identity, a local/private indicator, and a short description. The top tab row should prioritize `Issues` and `Projects`. `Pull requests` can be disabled, hidden, or treated as an inactive tab stub because Tracky does not manage pull requests. `Milestones` can remain accessible as a related repository tab, and `More` can hold secondary Tracky tools.

The `Issues` tab contains the main issue workflow. It should show a toolbar with `Filters`, a search query text box, `Labels`, `Milestones`, and `New issue`. Below the toolbar, the issue list header should show `Open` and `Closed` counts and metadata filter affordances. Issue rows should present state, title, labels, priority, issue number, author/assignee metadata, projects, milestones, due dates, comments, and attachments in a compact list.

The `Projects` tab lives inside the same repository shell rather than becoming a separate app-like page. It should show project saved view tabs first, then a project toolbar with query input, `View`, layout mode, `Group`, `Sort`, and `Fields`. The active layout can be `Table` or `Board`. Custom fields should be editable from the Projects work surface without forcing the user into a separate form-heavy management area.

## 5. Existing Code Fit

The implementation should stay centered on `src/Tracky.App/Views/MainWindow.axaml` and `src/Tracky.App/ViewModels/MainWindowViewModel.cs`. The existing ViewModel already exposes the key binding surfaces needed for this design, including `VisibleIssues`, `Projects`, `RepositoryIssues`, `RepositoryMilestones`, `ProjectSavedViews`, `ProjectBoardColumns`, `ProjectTableItems`, `ProjectTableGroups`, `SelectedProjectViewMode`, `ShowIssuesCommand`, `ShowProjectsCommand`, and `ApplyProjectSavedViewCommand`.

Because the domain model already has Issues and Projects modules, the first implementation pass should not rewrite the core data model. It should reconnect the existing state and commands into the new repository-shell layout.

If small ViewModel properties are needed for clearer XAML binding, they should be narrowly scoped and named after the UX concept they support. For example, a `HasProjectSavedViews` boolean or a display count property is acceptable if it simplifies the view and improves testability.

## 6. Interaction Design

The user should be able to switch between `Issues` and `Projects` using the repository tab row. Keyboard shortcuts that already exist, such as `Ctrl+1` and `Ctrl+2`, should keep working.

The issue list should remain the main triage surface. Search and dropdown filters should update the list without changing screens. `New issue` should open the existing quick capture or creation flow, but the visual placement should match the repository toolbar.

The project surface should support saved views as top-level tabs within the `Projects` tab. Applying a saved view should update the project filter, sort, group, and layout mode. Changing `Table` or `Board` should preserve the selected project context and keep field controls nearby.

For board movement, existing forward/backward commands can remain for keyboard and accessibility safety. If drag-and-drop is added later, it should be a separate enhancement and should not block this redesign.

## 7. Secondary Feature Placement

Calendar, Timeline, Reminders, Export, Preferences, and other Tracky-specific workflows should be moved out of the primary visual path. They can appear in `More`, in a lower side panel, or in a compact secondary tools region.

This keeps the main experience focused on the parts the user explicitly requested: GitHub Issues and GitHub Projects management behavior. No feature should be removed unless it is already obsolete; the design only lowers visual priority.

## 8. Error Handling And Empty States

Existing loading and status messages should remain visible but should be placed where they support the current tab. Issue loading errors should appear near the issue list or issue detail surface. Project loading errors should appear near the Projects toolbar or project content area.

Empty Issues and Projects states should be concise. An empty issue list should offer `New issue`. An empty Projects tab should offer `New project`. Empty saved views should explain the absence in one short sentence and provide an action when the ViewModel already supports it.

## 9. Testing Strategy

Headless Avalonia tests should verify that the repository shell renders with the expected tabs and primary controls. The tests should check that `Issues` is visible after initialization, `Projects` can be activated, and the primary project controls render when the Projects tab is active.

Existing tests around issue creation, issue detail navigation, repository tabs, and project view switching should continue to pass. If XAML names move, tests should be updated to assert the new user-facing structure rather than the old layout internals.

After implementation, the verification command should be `dotnet test`. If the UI can be launched locally, the app should also be visually checked to ensure there is no overlapping text, clipped controls, or accidental exposure of secondary tools as primary workflows.

## 10. References

The design is informed by GitHub's public documentation for issue dashboards, project views, table layout, and project layout switching. These references are used for workflow understanding, not for copying assets or source styles.

- GitHub Docs: Viewing all issues and pull requests
- GitHub Docs: Managing your project views
- GitHub Docs: Customizing the table layout
- GitHub Docs: Changing the layout of a view
