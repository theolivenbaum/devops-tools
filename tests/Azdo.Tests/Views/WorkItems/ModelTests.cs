using Azdo.Core.AzureDevOps;
using Azdo.Tui.Components;
using Azdo.Tui.Runtime;
using Azdo.Tui.Views.WorkItems;
using Xunit;
using static Azdo.Tests.Views.WorkItems.TestHelpers;

namespace Azdo.Tests.Views.WorkItems;

public class ModelTests
{
    private static Model NewModel() => new(null);

    private static Model WithSize(int w = 100, int h = 30)
    {
        var m = NewModel();
        m.Update(new WindowSizeMsg(w, h));
        return m;
    }

    [Fact]
    public void SetWorkItemsMsg_PopulatesList()
    {
        var m = WithSize();
        var items = new List<WorkItem>
        {
            new() { Id = 123, Fields = new() { Title = "Fix bug", State = "Active", WorkItemType = "Bug" } },
            new() { Id = 456, Fields = new() { Title = "Add feature", State = "New", WorkItemType = "Task" } },
        };
        m.Update(new SetWorkItemsMsg(items));

        Assert.Equal(2, m.ListItems.Count);
        Assert.Equal(123, m.ListItems[0].Id);
    }

    [Fact]
    public void ViewMode_Navigation_EnterAndEsc()
    {
        var m = WithSize();
        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 123, Fields = new() { Title = "Fix bug", State = "Active", WorkItemType = "Bug" } },
        }));

        m.Update(Key("enter"));
        Assert.Equal(ViewMode.Detail, m.GetViewMode());
        Assert.NotNull(m.Detail);

        m.Update(Key("esc"));
        Assert.Equal(ViewMode.List, m.GetViewMode());
    }

    [Fact]
    public void View_Loading_ShowsMessage()
    {
        var m = NewModel();
        m.Init();
        m.Update(new WindowSizeMsg(100, 30));
        var view = m.View();
        Assert.True(view.Contains("work items") || view.Contains("quit"));
    }

    [Fact]
    public void View_Error_ShowsErrorMessage()
    {
        var m = WithSize();
        m.Update(new WorkItemsMsg(Array.Empty<WorkItem>(), new InvalidOperationException("boom")));
        Assert.Contains("Error", m.View());
    }

    [Fact]
    public void View_Empty_ShowsNoItemsMessage()
    {
        var m = NewModel();
        m.SetListItemsForTest(new List<WorkItem>());
        m.Update(new WindowSizeMsg(100, 30));
        Assert.Contains("No work items", m.View());
    }

    [Fact]
    public void GetContextItems_ListMode_Empty()
    {
        var m = NewModel();
        Assert.Empty(m.GetContextItems());
    }

    [Fact]
    public void HasContextBar_DetailView()
    {
        var m = NewModel();
        Assert.False(m.HasContextBar());

        m.SetListItemsForTest(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "Test", WorkItemType = "Bug" } } });
        m.Update(Key("enter"));
        Assert.True(m.HasContextBar());
    }

    // --- My items ---

    [Fact]
    public void MyItems_Toggle()
    {
        var m = WithSize();
        Assert.False(m.IsMyItemsActive());

        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Title = "My task" } },
            new() { Id = 2, Fields = new() { Title = "Other task" } },
        }));

        var cmd = m.Update(Rune('m'));
        Assert.True(m.IsMyItemsActive());
        Assert.NotNull(cmd);

        m.Update(new MyWorkItemsMsg(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "My task" } } }, null));
        Assert.Single(m.ListItems);

        m.Update(Rune('m'));
        Assert.False(m.IsMyItemsActive());
        Assert.Equal(2, m.ListItems.Count);
    }

    [Fact]
    public void MyItems_IgnoredDuringSearch()
    {
        var m = WithSize();
        m.Update(new SetWorkItemsMsg(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "Item" } } }));
        m.Update(Rune('f'));
        Assert.True(m.IsSearching());

        m.Update(Rune('m'));
        Assert.False(m.IsMyItemsActive());
    }

    [Fact]
    public void MyItems_IgnoredInDetailView()
    {
        var m = WithSize();
        m.SetListItemsForTest(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "Item", WorkItemType = "Task" } } });
        m.Update(Key("enter"));
        Assert.Equal(ViewMode.Detail, m.GetViewMode());

        m.Update(Rune('m'));
        Assert.False(m.IsMyItemsActive());
    }

    [Fact]
    public void MyItems_PollingWhileFilterActive_DoesNotChangeVisible()
    {
        var m = WithSize();
        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Title = "Mine" } },
            new() { Id = 2, Fields = new() { Title = "Theirs" } },
        }));
        m.Update(Rune('m'));
        m.Update(new MyWorkItemsMsg(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "Mine" } } }, null));
        Assert.Single(m.ListItems);

        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Title = "Mine" } },
            new() { Id = 3, Fields = new() { Title = "New item" } },
            new() { Id = 4, Fields = new() { Title = "Another new" } },
        }));

        Assert.True(m.IsMyItemsActive());
        Assert.Single(m.ListItems);
        Assert.Equal(3, m.AllItems.Count);

        m.Update(Rune('m'));
        Assert.Equal(3, m.ListItems.Count);
    }

    [Fact]
    public void MyItems_FetchError_FallsBack()
    {
        var m = WithSize();
        m.Update(new SetWorkItemsMsg(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "Item" } } }));
        m.Update(Rune('m'));
        m.Update(new MyWorkItemsMsg(Array.Empty<WorkItem>(), new InvalidOperationException("boom")));
        Assert.False(m.IsMyItemsActive());
    }

    [Fact]
    public void Refresh_WhileMyItemsActive_ChainsAndClearsLoading()
    {
        var m = WithSize();
        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Title = "Mine" } },
            new() { Id = 2, Fields = new() { Title = "Theirs" } },
        }));
        m.Update(Rune('m'));
        m.Update(new MyWorkItemsMsg(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "Mine" } } }, null));
        Assert.True(m.IsMyItemsActive());

        m.Update(Rune('r'));

        var cmd = m.Update(new WorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Title = "Mine (updated)" } },
            new() { Id = 2, Fields = new() { Title = "Theirs" } },
            new() { Id = 3, Fields = new() { Title = "New item" } },
        }, null));

        Assert.Equal(3, m.AllItems.Count);
        Assert.NotNull(cmd);

        m.Update(new MyWorkItemsMsg(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "Mine (updated)" } } }, null));
        Assert.DoesNotContain("Loading work items", m.View());
    }

    [Fact]
    public void Refresh_AfterStateChange_ReturnsCommand()
    {
        var m = WithSize();
        m.Update(new SetWorkItemsMsg(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "Bug", State = "Active", WorkItemType = "Bug" } } }));
        m.Update(Key("enter"));
        Assert.Equal(ViewMode.Detail, m.GetViewMode());

        var cmd = m.Update(WorkItemStateChangedMsg.Instance);
        Assert.NotNull(cmd);
    }

    // --- Tag filter ---

    [Fact]
    public void TagFilter_ApplyAndClear()
    {
        var m = WithSize();
        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Title = "Item 1", Tags = "Sprint 1; Backend" } },
            new() { Id = 2, Fields = new() { Title = "Item 2", Tags = "Sprint 1; Frontend" } },
            new() { Id = 3, Fields = new() { Title = "Item 3", Tags = "Sprint 2; Backend" } },
        }));
        Assert.False(m.IsTagFilterActive());

        m.Update(new TagSelectedMsg("Backend"));
        Assert.True(m.IsTagFilterActive());
        Assert.Equal("Backend", m.ActiveTag());
        Assert.Equal(2, m.ListItems.Count);

        m.Update(new TagSelectedMsg(""));
        Assert.False(m.IsTagFilterActive());
        Assert.Equal(3, m.ListItems.Count);
    }

    [Fact]
    public void TagFilter_ComposesWithMyItems()
    {
        var m = WithSize();
        var items = new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Title = "My Backend", Tags = "Backend" } },
            new() { Id = 2, Fields = new() { Title = "My Frontend", Tags = "Frontend" } },
            new() { Id = 3, Fields = new() { Title = "Other Backend", Tags = "Backend" } },
        };
        m.Update(new SetWorkItemsMsg(items));
        m.Update(Rune('m'));
        m.Update(new MyWorkItemsMsg(new List<WorkItem> { items[0], items[1] }, null));
        Assert.Equal(2, m.ListItems.Count);

        m.Update(new TagSelectedMsg("Backend"));
        Assert.Single(m.ListItems);
        Assert.Equal(1, m.ListItems[0].Id);
    }

    [Fact]
    public void TagFilter_PollingRespectsActiveFilter()
    {
        var m = WithSize();
        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Title = "Item 1", Tags = "Sprint 1" } },
            new() { Id = 2, Fields = new() { Title = "Item 2", Tags = "Sprint 2" } },
        }));
        m.Update(new TagSelectedMsg("Sprint 1"));
        Assert.Single(m.ListItems);

        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Title = "Item 1", Tags = "Sprint 1" } },
            new() { Id = 2, Fields = new() { Title = "Item 2", Tags = "Sprint 2" } },
            new() { Id = 3, Fields = new() { Title = "Item 3", Tags = "Sprint 1" } },
        }));
        Assert.Equal(3, m.AllItems.Count);
        Assert.Equal(2, m.ListItems.Count);
    }

    [Fact]
    public void TagFilter_IgnoredDuringSearch()
    {
        var m = WithSize();
        m.Update(new SetWorkItemsMsg(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "Item", Tags = "Sprint 1" } } }));
        m.Update(Rune('f'));
        Assert.True(m.IsSearching());

        m.Update(Rune('T'));
        Assert.False(m.IsTagPickerVisible());
        Assert.False(m.IsTagFilterActive());
    }

    [Fact]
    public void TagFilter_IgnoredInDetailView()
    {
        var m = WithSize();
        m.SetListItemsForTest(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "Item", Tags = "Sprint 1", WorkItemType = "Task" } } });
        m.Update(Key("enter"));
        Assert.Equal(ViewMode.Detail, m.GetViewMode());

        m.Update(Rune('T'));
        Assert.False(m.IsTagFilterActive());
    }

    // --- State filter ---

    [Fact]
    public void StateFilter_OpensPickerAndApplies()
    {
        var m = WithSize();
        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Title = "A", State = "Active" } },
            new() { Id = 2, Fields = new() { Title = "B", State = "New" } },
            new() { Id = 3, Fields = new() { Title = "C", State = "Active" } },
        }));

        m.Update(Rune('s'));
        Assert.True(m.IsStatePickerVisible());

        m.Update(new ListPickerSelectedMsg("Active"));
        Assert.True(m.IsStateFilterActive());
        Assert.Equal("Active", m.ActiveState());
        Assert.Equal(2, m.ListItems.Count);

        m.Update(Rune('s'));
        m.Update(new ListPickerSelectedMsg(""));
        Assert.False(m.IsStateFilterActive());
        Assert.Equal(3, m.ListItems.Count);
    }

    // --- Tag picker passthrough ---

    private static Model WithTagPickerOpen()
    {
        var m = WithSize();
        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 1, Fields = new() { Title = "A", Tags = "Spring; Summer" } },
            new() { Id = 2, Fields = new() { Title = "B", Tags = "Monday" } },
        }));
        m.Update(Rune('T'));
        Assert.True(m.IsTagPickerVisible());
        return m;
    }

    [Fact]
    public void TagPicker_SKeyTypedIntoSearch()
    {
        var m = WithTagPickerOpen();
        m.Update(Rune('s'));
        Assert.False(m.IsStatePickerVisible());
        Assert.Equal("s", m.TagPickerSearchQuery());
    }

    [Fact]
    public void TagPicker_MKeyTypedIntoSearch()
    {
        var m = WithTagPickerOpen();
        m.Update(Rune('m'));
        Assert.False(m.IsMyItemsActive());
        Assert.Equal("m", m.TagPickerSearchQuery());
    }

    [Fact]
    public void TagPicker_TKeyTypedIntoSearch()
    {
        var m = WithTagPickerOpen();
        m.Update(Rune('T'));
        Assert.Equal("T", m.TagPickerSearchQuery());
    }

    [Fact]
    public void WorkItemsMsg_CriticalError_NotShownInline()
    {
        var m = WithSize(120, 30);
        var criticalErr = new InvalidOperationException("HTTP request failed with status 400");
        var cmd = m.Update(new WorkItemsMsg(Array.Empty<WorkItem>(), criticalErr));

        Assert.NotNull(cmd);
        var msg = Run(cmd);
        Assert.IsType<CriticalErrorMsg>(msg);
        Assert.DoesNotContain("Error loading", m.View());
    }

    // --- Comment form visibility / esc routing ---

    private static Model OpenDetailWithItem()
    {
        var m = WithSize();
        m.SetListItemsForTest(new List<WorkItem> { new() { Id = 123, Fields = new() { Title = "Fix bug", State = "Active", WorkItemType = "Bug" } } });
        m.Update(Key("enter"));
        Assert.Equal(ViewMode.Detail, m.GetViewMode());
        return m;
    }

    [Fact]
    public void IsCommentFormVisible_AfterPressingC()
    {
        var m = OpenDetailWithItem();
        Assert.False(m.IsCommentFormVisible());
        m.Update(Rune('c'));
        Assert.True(m.IsCommentFormVisible());
    }

    [Fact]
    public void IsCommentFormVisible_FalseInListView()
    {
        var m = WithSize();
        Assert.False(m.IsCommentFormVisible());
    }

    [Fact]
    public void EscWithCommentFormOpen_StaysInDetail()
    {
        var m = OpenDetailWithItem();
        m.Update(Rune('c'));
        Assert.True(m.IsCommentFormVisible());

        m.Update(Key("esc"));
        Assert.Equal(ViewMode.Detail, m.GetViewMode());
        Assert.False(m.IsCommentFormVisible());
    }

    [Fact]
    public void StatePickerEsc_ClosesPickerNotDetailView()
    {
        var m = WithSize();
        m.SetListItemsForTest(new List<WorkItem> { new() { Id = 123, Fields = new() { Title = "Test WI", State = "Active", WorkItemType = "Bug" } } });
        m.Update(Key("enter"));
        Assert.Equal(ViewMode.Detail, m.GetViewMode());

        // Open the detail state picker via a StatesLoadedMsg routed to the detail.
        var detail = (DetailModel)m.Detail!;
        detail.Update(new StatesLoadedMsg(new List<WorkItemTypeState>
        {
            new() { Name = "New", Color = "b2b2b2", Category = "Proposed" },
            new() { Name = "Active", Color = "007acc", Category = "InProgress" },
            new() { Name = "Resolved", Color = "ff9d00", Category = "Resolved" },
        }, null));
        Assert.True(detail.IsStatePickerVisible);

        m.Update(Key("esc"));
        Assert.Equal(ViewMode.Detail, m.GetViewMode());
        Assert.False(detail.IsStatePickerVisible);

        m.Update(Key("esc"));
        Assert.Equal(ViewMode.List, m.GetViewMode());
    }

    // --- FilterLabel ---

    [Fact]
    public void FilterLabel_CombinesActiveFilters()
    {
        var m = WithSize();
        Assert.Equal("", m.FilterLabel());

        m.Update(new SetWorkItemsMsg(new List<WorkItem> { new() { Id = 1, Fields = new() { Title = "X", State = "Active", Tags = "Backend" } } }));
        m.Update(new TagSelectedMsg("Backend"));
        Assert.Equal("Tag: Backend", m.FilterLabel());

        m.Update(Rune('s'));
        m.Update(new ListPickerSelectedMsg("Active"));
        Assert.Equal("Tag: Backend + State: Active", m.FilterLabel());
    }

    [Fact]
    public void DefaultKeybindings_ContainsExpectedActions()
    {
        var m = NewModel();
        var kb = m.DefaultKeybindings();
        foreach (var word in new[] { "refresh", "navigate", "details", "search", "my items", "tags", "state", "back", "help", "quit" })
            Assert.Contains(word, kb);
    }
}
