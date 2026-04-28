# GitHub-Style Issues And Projects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework Tracky's primary Avalonia UX into an English, repository-style Issues and Projects work surface while avoiding GitHub brand assets, copied icons, screenshots, and copied CSS.

**Architecture:** Keep the existing Core domain and workspace service intact. The implementation changes the `MainWindowViewModel` tab state and display helpers, then rewires `MainWindow.axaml` so Issues and Projects share one repository shell with Projects saved views, table/board controls, and fields available inside the Projects tab.

**Tech Stack:** .NET 10, Avalonia 12 compiled bindings, xUnit/Avalonia headless tests, existing Tracky MVVM helpers.

---

## Scope Check

The approved spec covers one subsystem: Tracky's local Issues and Projects UX. It does not require new persistence tables, new domain models, network integration, GitHub branding, or drag-and-drop. This plan keeps the work in the App layer and preserves current issue/project service behavior.

## File Structure

- Modify `tests/Tracky.App.Tests/MainWindowViewModelTests.cs` to lock the repository Projects tab behavior before touching the ViewModel.
- Modify `tests/Tracky.App.Tests/MainWindowHeadlessTests.cs` to lock the visible repository shell, Projects saved view tabs, table/board controls, field panel, and secondary placement of non-primary tools.
- Modify `src/Tracky.App/ViewModels/MainWindowViewModel.cs` to make `ShowProjectsCommand` activate the repository `Projects` tab and expose small display helpers for project counts and saved view empty states.
- Modify `src/Tracky.App/Views/MainWindow.axaml` to update the repository tab row and Projects content area. Keep comments near the changed XAML because the user requested visible intent for code edits.

## Task 1: Lock Projects Tab State In ViewModel Tests

**Files:**
- Modify: `tests/Tracky.App.Tests/MainWindowViewModelTests.cs`
- Modify: `src/Tracky.App/ViewModels/MainWindowViewModel.cs`
- Test: `tests/Tracky.App.Tests/MainWindowViewModelTests.cs`

- [ ] **Step 1: Write the failing ViewModel assertion**

In `Project_screen_loads_phase2_views_and_updates_project_metadata`, replace the assertion block after `ShowProjectsCommand.Execute(null)` with this block:

```csharp
viewModel.ShowProjectsCommand.Execute(null);
await TestWaiter.UntilAsync(
    () => viewModel.SelectedProject is not null && viewModel.ProjectBoardColumns.Count > 0,
    "The Projects screen did not load a selected project with board columns.");

Assert.True(viewModel.IsProjectsViewActive);
Assert.False(viewModel.IsIssuesViewActive);
Assert.True(viewModel.IsRepositoryProjectsTabActive);
Assert.False(viewModel.IsRepositoryIssuesTabActive);
Assert.NotEmpty(viewModel.Projects);
Assert.NotEmpty(viewModel.ProjectTableItems);
Assert.NotEmpty(viewModel.ProjectCustomFields);
Assert.NotEmpty(viewModel.ProjectSavedViews);
```

- [ ] **Step 2: Run the focused ViewModel test and verify it fails**

Run:

```powershell
dotnet test tests/Tracky.App.Tests/Tracky.App.Tests.csproj --filter "FullyQualifiedName~Project_screen_loads_phase2_views_and_updates_project_metadata"
```

Expected result: the test fails because `ShowProjectsCommand` currently leaves `IsRepositoryIssuesTabActive` true.

- [ ] **Step 3: Implement the minimal tab-state fix**

In `src/Tracky.App/ViewModels/MainWindowViewModel.cs`, replace `ShowProjects()` with:

```csharp
private void ShowProjects()
{
    // Projects is a first-class repository tab in the GitHub-style shell,
    // so the command must activate both the project screen and the repository Projects tab.
    IsProjectsViewActive = true;
    SetRepositoryDetailTab(RepositoryDetailTab.Projects);
}
```

- [ ] **Step 4: Run the focused ViewModel test and verify it passes**

Run:

```powershell
dotnet test tests/Tracky.App.Tests/Tracky.App.Tests.csproj --filter "FullyQualifiedName~Project_screen_loads_phase2_views_and_updates_project_metadata"
```

Expected result: the test passes.

- [ ] **Step 5: Commit the tab-state fix**

Run:

```powershell
git add tests/Tracky.App.Tests/MainWindowViewModelTests.cs src/Tracky.App/ViewModels/MainWindowViewModel.cs
git commit -m "fix: activate repository projects tab"
```

## Task 2: Add Headless Tests For The Repository Shell

**Files:**
- Modify: `tests/Tracky.App.Tests/MainWindowHeadlessTests.cs`
- Test: `tests/Tracky.App.Tests/MainWindowHeadlessTests.cs`

- [ ] **Step 1: Update the shell test expectations**

In `MainWindow_uses_github_like_single_column_shell_without_left_panel`, keep the existing assertions and add these assertions after `RepositoryProjectsTabButton` is checked:

```csharp
Assert.NotNull(FindVisual<Button>(window, "RepositoryMilestonesTabButton"));
Assert.NotNull(FindVisual<Button>(window, "RepositoryMoreTabButton"));
Assert.Null(FindTextBlockByText(window, "Discussions"));
Assert.Null(FindTextBlockByText(window, "Actions"));
Assert.Null(FindTextBlockByText(window, "Security and quality"));
```

- [ ] **Step 2: Update the repository dashboard control assertions**

In the test that currently finds `RepositoryMilestonesButton` and `RepositoryProjectsContent`, replace the assertions around repository tabs and project controls with this block:

```csharp
var repositoryMilestonesTabButton = FindVisual<Button>(window, "RepositoryMilestonesTabButton");
var repositoryMoreTabButton = FindVisual<Button>(window, "RepositoryMoreTabButton");
var repositoryIssueSearchBox = FindVisual<TextBox>(window, "RepositoryIssueSearchBox");
var repositoryLabelsButton = FindVisual<Button>(window, "RepositoryLabelsButton");
var repositoryNewIssueButton = FindVisual<Button>(window, "RepositoryNewIssueButton");
var repositoryOpenIssuesText = FindVisual<TextBlock>(window, "RepositoryOpenIssuesText");
var repositoryClosedIssuesText = FindVisual<TextBlock>(window, "RepositoryClosedIssuesText");

Assert.NotNull(repositoryListBox);
Assert.NotNull(repositoryIssuesTabButton);
Assert.NotNull(repositoryMilestonesTabButton);
Assert.NotNull(repositoryProjectsTabButton);
Assert.NotNull(repositoryMoreTabButton);
Assert.Null(FindVisual<Button>(window, "RepositoryDiscussionsTabButton"));
Assert.Null(FindVisual<Button>(window, "RepositorySecurityTabButton"));
Assert.NotNull(repositoryIssuesListBox);
Assert.NotNull(repositoryOwnerNameText);
Assert.NotNull(repositoryNameText);
Assert.NotNull(repositoryVisibilityBadge);
Assert.Null(repositoryWatchButton);
Assert.Null(repositoryForkButton);
Assert.Null(repositoryStarButton);
Assert.NotNull(repositoryIssueSearchBox);
Assert.NotNull(repositoryLabelsButton);
Assert.NotNull(repositoryNewIssueButton);
Assert.NotNull(repositoryOpenIssuesText);
Assert.NotNull(repositoryClosedIssuesText);
Assert.Same(viewModel.Projects, repositoryListBox.ItemsSource);
Assert.Same(viewModel.RepositoryIssues, repositoryIssuesListBox.ItemsSource);

repositoryMilestonesTabButton.Command?.Execute(repositoryMilestonesTabButton.CommandParameter);
window.UpdateLayout();
var repositoryMilestonesPanel = FindVisual<ItemsControl>(window, "RepositoryMilestonesPanel");
var repositoryMilestonesOpenText = FindVisual<TextBlock>(window, "RepositoryMilestonesOpenText");
var repositoryMilestonesClosedText = FindVisual<TextBlock>(window, "RepositoryMilestonesClosedText");
var repositoryMilestonesSortButton = FindVisual<Button>(window, "RepositoryMilestonesSortButton");
Assert.NotNull(repositoryMilestonesPanel);
Assert.NotNull(repositoryMilestonesOpenText);
Assert.NotNull(repositoryMilestonesClosedText);
Assert.NotNull(repositoryMilestonesSortButton);

repositoryProjectsTabButton.Command?.Execute(repositoryProjectsTabButton.CommandParameter);
window.UpdateLayout();
var repositoryProjectsContent = FindVisual<ContentControl>(window, "RepositoryProjectsContent");
var repositoryProjectSavedViewsTabs = FindVisual<ItemsControl>(window, "RepositoryProjectSavedViewsTabs");
var repositoryProjectsSearchBox = FindVisual<TextBox>(window, "RepositoryProjectsSearchBox");
var repositoryProjectViewButton = FindVisual<Button>(window, "RepositoryProjectViewButton");
var repositoryProjectTableButton = FindVisual<Button>(window, "RepositoryProjectTableButton");
var repositoryProjectBoardButton = FindVisual<Button>(window, "RepositoryProjectBoardButton");
var repositoryProjectGroupComboBox = FindVisual<ComboBox>(window, "RepositoryProjectGroupComboBox");
var repositoryProjectSortComboBox = FindVisual<ComboBox>(window, "RepositoryProjectSortComboBox");
var repositoryProjectFieldsPanel = FindVisual<StackPanel>(window, "RepositoryProjectFieldsPanel");
var repositoryProjectsResultsText = FindVisual<TextBlock>(window, "RepositoryProjectsResultsText");
Assert.NotNull(repositoryProjectsContent);
Assert.NotNull(repositoryProjectSavedViewsTabs);
Assert.NotNull(repositoryProjectsSearchBox);
Assert.NotNull(repositoryProjectViewButton);
Assert.NotNull(repositoryProjectTableButton);
Assert.NotNull(repositoryProjectBoardButton);
Assert.NotNull(repositoryProjectGroupComboBox);
Assert.NotNull(repositoryProjectSortComboBox);
Assert.NotNull(repositoryProjectFieldsPanel);
Assert.NotNull(repositoryProjectsResultsText);
Assert.Null(FindVisual<Button>(window, "RepositoryProjectCalendarButton"));
Assert.Null(FindVisual<Button>(window, "RepositoryProjectTimelineButton"));
Assert.Same(viewModel.ProjectSavedViews, repositoryProjectSavedViewsTabs.ItemsSource);
```

- [ ] **Step 3: Run the headless tests and verify they fail**

Run:

```powershell
dotnet test tests/Tracky.App.Tests/Tracky.App.Tests.csproj --filter "FullyQualifiedName~MainWindowHeadlessTests"
```

Expected result: the headless tests fail because the new `RepositoryMilestonesTabButton`, `RepositoryMoreTabButton`, saved view tabs, and project toolbar controls are not present yet.

- [ ] **Step 4: Commit the failing test expectations**

Run:

```powershell
git add tests/Tracky.App.Tests/MainWindowHeadlessTests.cs
git commit -m "test: cover repository shell project controls"
```

## Task 3: Add Project Shell Display Helpers

**Files:**
- Modify: `src/Tracky.App/ViewModels/MainWindowViewModel.cs`
- Test: `tests/Tracky.App.Tests/MainWindowViewModelTests.cs`

- [ ] **Step 1: Add helper properties near existing repository display properties**

In `MainWindowViewModel.cs`, after `RepositoryMilestoneCountText`, add:

```csharp
public string RepositoryProjectCountText => Projects.Count == 1
    ? "1 project"
    : $"{Projects.Count} projects";

public bool HasProjectSavedViews => ProjectSavedViews.Count > 0;

public bool HasNoProjectSavedViews => !HasProjectSavedViews;
```

- [ ] **Step 2: Notify helper properties when project lists change**

In `ApplyProjects`, update the property notifications to:

```csharp
OnPropertyChanged(nameof(HasProjects));
OnPropertyChanged(nameof(HasNoProjects));
OnPropertyChanged(nameof(RepositoryProjectCountText));
SelectedProject = SelectPreferredProject([.. Projects], preferredProjectId);
RefreshSelectedRepositoryContent();
```

In `OnRepositoryContentPropertiesChanged`, update the notification block to:

```csharp
OnPropertyChanged(nameof(HasRepositoryIssues));
OnPropertyChanged(nameof(HasNoRepositoryIssues));
OnPropertyChanged(nameof(HasRepositoryMilestones));
OnPropertyChanged(nameof(HasNoRepositoryMilestones));
OnPropertyChanged(nameof(HasProjectSavedViews));
OnPropertyChanged(nameof(HasNoProjectSavedViews));
OnPropertyChanged(nameof(RepositoryIssueCountText));
OnPropertyChanged(nameof(RepositoryMilestoneCountText));
OnPropertyChanged(nameof(RepositoryProjectCountText));
OnPropertyChanged(nameof(RepositoryProjectViewsText));
OnPropertyChanged(nameof(RepositoryOpenIssuesText));
OnPropertyChanged(nameof(RepositoryClosedIssuesText));
OnPropertyChanged(nameof(RepositoryProjectsResultsText));
```

- [ ] **Step 3: Run the ViewModel tests**

Run:

```powershell
dotnet test tests/Tracky.App.Tests/Tracky.App.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests"
```

Expected result: tests pass because the new helpers are additive and `ShowProjects()` was fixed in Task 1.

- [ ] **Step 4: Commit the display helpers**

Run:

```powershell
git add src/Tracky.App/ViewModels/MainWindowViewModel.cs
git commit -m "feat: expose repository project display state"
```

## Task 4: Update The Repository Header And Tabs

**Files:**
- Modify: `src/Tracky.App/Views/MainWindow.axaml`
- Test: `tests/Tracky.App.Tests/MainWindowHeadlessTests.cs`

- [ ] **Step 1: Replace the repository header title grid**

In `MainWindow.axaml`, replace the `<Grid>` at the start of the Projects repository header with:

```xml
<!-- The repository header mirrors the familiar Issues/Projects workflow shape,
     but uses Tracky's local workspace identity and no GitHub brand assets. -->
<Grid ColumnDefinitions="*,Auto">
    <WrapPanel VerticalAlignment="Center">
        <TextBlock x:Name="RepositoryOwnerNameText"
                   Text="{Binding SelectedRepositoryOwnerName}"
                   FontSize="20"
                   Foreground="{StaticResource TrackyAccentBrush}" />
        <TextBlock Text=" / "
                   FontSize="20"
                   Foreground="{StaticResource TrackyMutedInkBrush}" />
        <TextBlock x:Name="RepositoryNameText"
                   Text="{Binding SelectedRepositoryName}"
                   FontSize="20"
                   FontWeight="SemiBold"
                   Foreground="{StaticResource TrackyAccentBrush}" />
        <Border x:Name="RepositoryVisibilityBadge"
                Background="Transparent"
                BorderBrush="{StaticResource TrackyBorderBrush}"
                BorderThickness="1"
                CornerRadius="999"
                Margin="10,2,0,0"
                Padding="7,1">
            <TextBlock Classes="muted"
                       FontSize="12"
                       FontWeight="SemiBold"
                       Text="Local" />
        </Border>
    </WrapPanel>

    <Button x:Name="RepositoryNewProjectButton"
            Grid.Column="1"
            Classes="primary"
            Command="{Binding CreateProjectCommand}"
            Content="New project"
            IsVisible="{Binding IsRepositoryProjectsTabActive}" />
</Grid>
```

- [ ] **Step 2: Replace the repository tab row**

Replace the `<StackPanel Grid.Row="1" ...>` tab row with:

```xml
<!-- Only Issues and Projects are first-class active surfaces. Pull requests is inactive because Tracky has no PR model,
     while Milestones and More keep related local tools reachable without taking over the main workflow. -->
<StackPanel Grid.Row="1"
            Orientation="Horizontal"
            Spacing="2">
    <Button Classes="github-tab" Content="Code" IsEnabled="False" />
    <Button x:Name="RepositoryIssuesTabButton"
            Classes="github-tab"
            Classes.active="{Binding IsRepositoryIssuesTabActive}"
            Command="{Binding ShowRepositoryIssuesCommand}"
            Content="{Binding RepositoryIssueCountText, StringFormat='Issues  {0}'}" />
    <Button Classes="github-tab" Content="Pull requests" IsEnabled="False" />
    <Button x:Name="RepositoryProjectsTabButton"
            Classes="github-tab"
            Classes.active="{Binding IsRepositoryProjectsTabActive}"
            Command="{Binding ShowRepositoryProjectsCommand}"
            Content="{Binding RepositoryProjectCountText, StringFormat='Projects  {0}'}" />
    <Button x:Name="RepositoryMilestonesTabButton"
            Classes="github-tab"
            Classes.active="{Binding IsRepositoryMilestonesTabActive}"
            Command="{Binding ShowRepositoryMilestonesCommand}"
            Content="Milestones" />
    <Button x:Name="RepositoryMoreTabButton"
            Classes="github-tab"
            Content="More"
            IsEnabled="False" />
</StackPanel>
```

- [ ] **Step 3: Remove the old toolbar Milestones button**

In the issue toolbar grid, remove the `RepositoryMilestonesButton` because Milestones is now a repository tab. Change `ColumnDefinitions` from `*,Auto,Auto,Auto` to `*,Auto,Auto`, keep `RepositoryIssueSearchBox`, keep `RepositoryLabelsButton` in `Grid.Column="1"`, and move `RepositoryNewIssueButton` to `Grid.Column="2"`.

The resulting issue toolbar grid should be:

```xml
<Grid ColumnDefinitions="*,Auto,Auto"
      ColumnSpacing="8"
      Margin="16,16,16,12">
    <TextBox x:Name="RepositoryIssueSearchBox"
             Classes="search-box"
             PlaceholderText="is:issue state:open"
             Text="{Binding RepositoryIssueSearchText, UpdateSourceTrigger=PropertyChanged}"
             IsVisible="{Binding IsRepositoryIssuesTabActive}" />
    <Button x:Name="RepositoryLabelsButton"
            Grid.Column="1"
            Classes="secondary"
            Content="Labels"
            IsVisible="{Binding IsRepositoryIssuesTabActive}" />
    <Button x:Name="RepositoryNewIssueButton"
            Grid.Column="2"
            Classes="primary"
            Command="{Binding PrepareNewIssueCommand}"
            Content="New issue"
            IsVisible="{Binding IsRepositoryIssuesTabActive}" />

    <!-- GitHub-like milestone management is a repository tab, so this row only appears after the Milestones tab is selected. -->
    <Grid Grid.ColumnSpan="3"
          ColumnDefinitions="Auto,Auto,*,Auto"
          ColumnSpacing="16"
          IsVisible="{Binding IsRepositoryMilestonesTabActive}">
        <TextBlock x:Name="RepositoryMilestonesOpenText"
                   Classes="body"
                   FontWeight="SemiBold"
                   VerticalAlignment="Center"
                   Text="{Binding RepositoryMilestoneCountText, StringFormat='Open {0}'}" />
        <TextBlock x:Name="RepositoryMilestonesClosedText"
                   Grid.Column="1"
                   Classes="muted"
                   VerticalAlignment="Center"
                   Text="Closed 0" />
        <Button x:Name="RepositoryMilestonesSortButton"
                Grid.Column="3"
                Classes="secondary"
                Content="Sort" />
    </Grid>
</Grid>
```

- [ ] **Step 4: Run the headless tests**

Run:

```powershell
dotnet test tests/Tracky.App.Tests/Tracky.App.Tests.csproj --filter "FullyQualifiedName~MainWindowHeadlessTests"
```

Expected result: tests still fail on the Projects saved view and project toolbar controls, but they no longer fail on the repository tab row.

- [ ] **Step 5: Commit the header and tab work**

Run:

```powershell
git add src/Tracky.App/Views/MainWindow.axaml
git commit -m "feat: align repository header tabs"
```

## Task 5: Rebuild The Projects Work Surface Inside The Repository Shell

**Files:**
- Modify: `src/Tracky.App/Views/MainWindow.axaml`
- Test: `tests/Tracky.App.Tests/MainWindowHeadlessTests.cs`

- [ ] **Step 1: Replace the Projects content opening layout**

Inside `RepositoryProjectsContent`, replace the opening `<Grid RowDefinitions="Auto,*" RowSpacing="14">` and its toolbar with:

```xml
<Grid RowDefinitions="Auto,Auto,*" RowSpacing="14">
    <!-- Saved views sit directly under the repository tabs so Projects feels like the approved repository-shell design. -->
    <Border Background="{StaticResource TrackySurfaceStrongBrush}"
            BorderBrush="{StaticResource TrackyBorderBrush}"
            BorderThickness="0,0,0,1"
            Padding="16,0,16,10">
        <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
            <ItemsControl x:Name="RepositoryProjectSavedViewsTabs"
                          ItemsSource="{Binding ProjectSavedViews}"
                          IsVisible="{Binding HasProjectSavedViews}">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <StackPanel Orientation="Horizontal" Spacing="6" />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="vm:ProjectSavedViewViewModel">
                        <Button Classes="scope-chip"
                                Command="{Binding #RootWindow.((vm:MainWindowViewModel)DataContext).ApplyProjectSavedViewCommand}"
                                CommandParameter="{Binding}"
                                Content="{Binding Name}" />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            <TextBlock Grid.Column="1"
                       Classes="muted"
                       VerticalAlignment="Center"
                       Text="{Binding RepositoryProjectViewsText}" />
        </Grid>
    </Border>

    <Grid Grid.Row="1"
          ColumnDefinitions="*,Auto,Auto,Auto,150,150,Auto"
          ColumnSpacing="8">
        <TextBox x:Name="RepositoryProjectsSearchBox"
                 Classes="search-box"
                 PlaceholderText="is:open status:ready priority:high"
                 Text="{Binding ProjectFilterText, UpdateSourceTrigger=PropertyChanged}" />
        <Button x:Name="RepositoryProjectViewButton"
                Grid.Column="1"
                Classes="secondary"
                Content="View" />
        <Button x:Name="RepositoryProjectTableButton"
                Grid.Column="2"
                Classes="scope-chip"
                Classes.active="{Binding IsProjectTableViewActive}"
                Command="{Binding ShowProjectTableCommand}"
                Content="Table" />
        <Button x:Name="RepositoryProjectBoardButton"
                Grid.Column="3"
                Classes="scope-chip"
                Classes.active="{Binding IsProjectBoardViewActive}"
                Command="{Binding ShowProjectBoardCommand}"
                Content="Board" />
        <ComboBox x:Name="RepositoryProjectGroupComboBox"
                  Grid.Column="4"
                  ItemsSource="{Binding AvailableProjectGroupFields}"
                  SelectedItem="{Binding ProjectGroupByText}" />
        <ComboBox x:Name="RepositoryProjectSortComboBox"
                  Grid.Column="5"
                  ItemsSource="{Binding AvailableProjectSortFields}"
                  SelectedItem="{Binding ProjectSortText}" />
        <Button x:Name="RepositoryProjectFieldsButton"
                Grid.Column="6"
                Classes="secondary"
                Content="Fields" />
    </Grid>
```

- [ ] **Step 2: Move the existing board/table/calendar/timeline content to row 2**

For each `ScrollViewer` inside `RepositoryProjectsContent`, change `Grid.Row="1"` to `Grid.Row="2"`. Keep the existing board, table, calendar, and timeline item templates intact during this step.

The first board viewer should begin like this:

```xml
<ScrollViewer Grid.Row="2" IsVisible="{Binding IsProjectBoardViewActive}">
    <ItemsControl ItemsSource="{Binding ProjectBoardColumns}">
```

The first table viewer should begin like this:

```xml
<ScrollViewer Grid.Row="2" IsVisible="{Binding IsProjectTableViewActive}">
    <ItemsControl ItemsSource="{Binding ProjectTableGroups}">
```

- [ ] **Step 3: Remove Calendar and Timeline from the primary toolbar**

Delete the old `RepositoryProjectsContent` toolbar buttons that had `Content="Calendar"` and `Content="Timeline"`. Do not delete `ProjectCalendarItems`, `ProjectCalendarGroups`, `ProjectTimelineItems`, or `ProjectTimelineGroups` from the ViewModel. This preserves the data for later placement under `More`.

- [ ] **Step 4: Add the fields side panel below the project content**

After the main project content viewers and before `RepositoryProjectsContent` closes, add this compact fields panel:

```xml
<StackPanel x:Name="RepositoryProjectFieldsPanel"
            Grid.Row="2"
            Width="280"
            HorizontalAlignment="Right"
            VerticalAlignment="Top"
            Spacing="10"
            Margin="0,0,16,16"
            IsVisible="{Binding IsRepositoryProjectsTabActive}">
    <!-- Custom field editing remains available, but it is visually secondary to the saved view and table/board workflow. -->
    <Border Background="{StaticResource TrackySurfaceStrongBrush}"
            BorderBrush="{StaticResource TrackyBorderBrush}"
            BorderThickness="1"
            CornerRadius="8"
            Padding="12">
        <StackPanel Spacing="8">
            <TextBlock FontWeight="SemiBold"
                       Foreground="{StaticResource TrackyInkBrush}"
                       Text="Fields" />
            <ComboBox ItemsSource="{Binding ProjectTableItems}"
                      SelectedItem="{Binding SelectedProjectItemForFields}" />
            <ComboBox ItemsSource="{Binding ProjectCustomFields}"
                      SelectedItem="{Binding SelectedProjectCustomField}" />
            <TextBox PlaceholderText="Field value"
                     Text="{Binding DraftCustomFieldValue, UpdateSourceTrigger=PropertyChanged}" />
            <Button Classes="secondary"
                    Command="{Binding SaveProjectCustomFieldValueCommand}"
                    Content="Save field value" />
        </StackPanel>
    </Border>
</StackPanel>
```

- [ ] **Step 5: Run the headless tests**

Run:

```powershell
dotnet test tests/Tracky.App.Tests/Tracky.App.Tests.csproj --filter "FullyQualifiedName~MainWindowHeadlessTests"
```

Expected result: headless tests pass after the Projects work surface exists and Calendar/Timeline are no longer primary toolbar buttons.

- [ ] **Step 6: Commit the Projects work surface**

Run:

```powershell
git add src/Tracky.App/Views/MainWindow.axaml
git commit -m "feat: add repository projects work surface"
```

## Task 6: Final Verification And Polish

**Files:**
- Modify: `src/Tracky.App/Views/MainWindow.axaml` only if verification exposes clipping, overlapping text, or malformed XAML.
- Test: all test projects through `Tracky.sln`

- [ ] **Step 1: Run the full test suite**

Run:

```powershell
dotnet test
```

Expected result: all tests pass.

- [ ] **Step 2: Build the app project**

Run:

```powershell
dotnet build src/Tracky.App/Tracky.App.csproj
```

Expected result: build succeeds without XAML binding errors.

- [ ] **Step 3: Inspect the final diff for copyright and branding guardrails**

Run:

```powershell
git diff HEAD~4 -- src/Tracky.App/Views/MainWindow.axaml src/Tracky.App/ViewModels/MainWindowViewModel.cs tests/Tracky.App.Tests/MainWindowHeadlessTests.cs tests/Tracky.App.Tests/MainWindowViewModelTests.cs
```

Expected result: the diff contains no Octocat, GitHub logo assets, copied SVG icon art, screenshots, copied stylesheet names, or pixel-identical branding comments. It should contain Tracky-specific comments explaining why the repository shell, Projects tab, and secondary field panel are structured this way.

- [ ] **Step 4: Commit verification-only polish if changes were needed**

If Step 2 or Step 3 required a small XAML adjustment, run:

```powershell
git add src/Tracky.App/Views/MainWindow.axaml
git commit -m "fix: polish repository shell layout"
```

If Step 2 and Step 3 required no code changes, do not create an empty commit.

## Self-Review

Spec coverage is complete: the plan covers the repository shell, English UI labels, Issues and Projects as primary tabs, Projects saved views, table/board controls, fields, lower priority Calendar/Timeline placement, and copyright guardrails.

Red-flag wording scan is clean: the plan uses explicit file paths, exact code snippets, exact commands, and expected outcomes.

Type consistency is clean: every added binding name corresponds to a ViewModel property defined in Task 3 or to an existing property already present in `MainWindowViewModel`. Every test control name is introduced in the XAML tasks.
