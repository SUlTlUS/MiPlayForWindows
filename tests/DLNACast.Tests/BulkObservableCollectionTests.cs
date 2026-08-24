using System.Collections.Specialized;
using DLNACast.Core.Collections;

namespace DLNACast.Tests;

public sealed class BulkObservableCollectionTests
{
    [Fact]
    public void ReplaceAll_PublishesOneResetWithOnlyTheFinalItemsVisible()
    {
        var collection = new BulkObservableCollection<string> { "old" };
        var notifications = new List<(NotifyCollectionChangedAction Action, string[] Items)>();
        collection.CollectionChanged += (_, args) =>
            notifications.Add((args.Action, [.. collection]));

        collection.ReplaceAll(["小爱音箱-6333", "小爱音箱-7503"]);

        var notification = Assert.Single(notifications);
        Assert.Equal(NotifyCollectionChangedAction.Reset, notification.Action);
        Assert.Equal(["小爱音箱-6333", "小爱音箱-7503"], notification.Items);
    }
}
