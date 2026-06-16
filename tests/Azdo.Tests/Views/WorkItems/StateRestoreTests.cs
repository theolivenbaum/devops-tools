using Azdo.Core.AzureDevOps;
using Azdo.Tui.Components;
using Azdo.Tui.Views.WorkItems;
using Xunit;
using static Azdo.Tests.Views.WorkItems.TestHelpers;

namespace Azdo.Tests.Views.WorkItems;

public class StateRestoreTests
{
    [Fact]
    public void PendingDetailRestore_OpensDetailWhenItemAppears()
    {
        var m = new Model(null);
        m.SetPendingDetailRestore(99);
        Assert.Equal(ViewMode.List, m.GetViewMode());

        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 12, Fields = new() { Title = "Other", WorkItemType = "Task" } },
            new() { Id = 99, Fields = new() { Title = "Target", WorkItemType = "Task", State = "Active" } },
        }));

        Assert.Equal(ViewMode.Detail, m.GetViewMode());
        Assert.Equal(99, m.DetailItemId());
    }

    [Fact]
    public void PendingDetailRestore_NoMatchStaysOnList()
    {
        var m = new Model(null);
        m.SetPendingDetailRestore(99);

        m.Update(new SetWorkItemsMsg(new List<WorkItem>
        {
            new() { Id = 12, Fields = new() { Title = "Only", WorkItemType = "Task" } },
        }));

        Assert.Equal(ViewMode.List, m.GetViewMode());
    }

    [Fact]
    public void PendingDetailRestore_IsOneShot()
    {
        var m = new Model(null);
        m.SetPendingDetailRestore(99);

        m.Update(new SetWorkItemsMsg(new List<WorkItem> { new() { Id = 12, Fields = new() { Title = "A", WorkItemType = "Task" } } }));
        Assert.Equal(ViewMode.List, m.GetViewMode());

        m.Update(new SetWorkItemsMsg(new List<WorkItem> { new() { Id = 99, Fields = new() { Title = "Target", WorkItemType = "Task" } } }));
        Assert.Equal(ViewMode.List, m.GetViewMode());
    }

    [Fact]
    public void DetailItemId_TracksOpenAndClose()
    {
        var m = new Model(null);
        Assert.Equal(0, m.DetailItemId());

        m.SetListItemsForTest(new List<WorkItem>
        {
            new() { Id = 1337, Fields = new() { Title = "Test", WorkItemType = "Task", State = "Active" } },
        });

        m.Update(Key("enter"));
        Assert.Equal(1337, m.DetailItemId());

        m.Update(Key("esc"));
        Assert.Equal(0, m.DetailItemId());
    }
}
