using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class QueuedWpfDispatcherServiceTests
{
    [Fact]
    public void CheckAccessReturnsTrueOnOwnerThread()
    {
        var dispatcher = new QueuedWpfDispatcherService();

        Assert.True(dispatcher.CheckAccess());
    }

    [Fact]
    public void PostRejectsInvalidArguments()
    {
        var dispatcher = new QueuedWpfDispatcherService();

        Assert.Throws<ArgumentNullException>(() => dispatcher.Post(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => dispatcher.Post(() => { }, (WpfDispatcherPriority)42));
    }

    [Fact]
    public void PostRaisesWorkAvailableAfterCallbackIsQueued()
    {
        var dispatcher = new QueuedWpfDispatcherService();
        var ran = false;
        var workAvailableCount = 0;
        var processedFromEvent = false;
        dispatcher.WorkAvailable += (_, _) =>
        {
            workAvailableCount++;
            processedFromEvent = dispatcher.ProcessPending();
        };

        var operation = dispatcher.Post(() => ran = true, WpfDispatcherPriority.Render);

        Assert.Equal(1, workAvailableCount);
        Assert.True(processedFromEvent);
        Assert.True(ran);
        Assert.True(operation.IsCompleted);
    }

    [Fact]
    public void ProcessPendingRunsCallbacksByPriorityAndFifoOrder()
    {
        var dispatcher = new QueuedWpfDispatcherService();
        var order = new List<int>();

        dispatcher.Post(() => order.Add(1), WpfDispatcherPriority.Background);
        dispatcher.Post(() => order.Add(2), WpfDispatcherPriority.Normal);
        dispatcher.Post(() => order.Add(3), WpfDispatcherPriority.Render);
        dispatcher.Post(() => order.Add(4), WpfDispatcherPriority.Normal);

        Assert.True(dispatcher.ProcessPending());

        Assert.Equal(new[] { 2, 4, 3, 1 }, order);
    }

    [Fact]
    public void ProcessPendingCompletesOperationAfterCallbackRuns()
    {
        var dispatcher = new QueuedWpfDispatcherService();
        var ran = false;

        var operation = dispatcher.Post(() => ran = true);

        Assert.True(dispatcher.ProcessPending());

        Assert.True(ran);
        Assert.True(operation.IsCompleted);
        Assert.False(operation.IsCanceled);
    }

    [Fact]
    public void ProcessPendingDefersCallbacksPostedByCallbackUntilNextTurn()
    {
        var dispatcher = new QueuedWpfDispatcherService();
        var order = new List<int>();

        dispatcher.Post(
            () =>
            {
                order.Add(1);
                dispatcher.Post(() => order.Add(2), WpfDispatcherPriority.Normal);
            },
            WpfDispatcherPriority.Normal);

        Assert.True(dispatcher.ProcessPending());
        Assert.Equal(new[] { 1 }, order);

        Assert.True(dispatcher.ProcessPending());
        Assert.Equal(new[] { 1, 2 }, order);
    }

    [Fact]
    public void WorkAvailableCannotReenterAnActiveDispatcherTurn()
    {
        var dispatcher = new QueuedWpfDispatcherService();
        var order = new List<int>();
        var nestedProcessResults = new List<bool>();
        dispatcher.WorkAvailable += (_, _) =>
            nestedProcessResults.Add(dispatcher.ProcessPending());

        dispatcher.Post(
            () =>
            {
                order.Add(1);
                dispatcher.Post(() => order.Add(2));
            });

        Assert.Equal(new[] { 1 }, order);
        Assert.Equal(new[] { false, true }, nestedProcessResults);

        Assert.True(dispatcher.ProcessPending());
        Assert.Equal(new[] { 1, 2 }, order);
    }

    [Fact]
    public void CanceledOperationDoesNotRun()
    {
        var dispatcher = new QueuedWpfDispatcherService();
        var ran = false;
        var operation = dispatcher.Post(() => ran = true);

        Assert.True(operation.Cancel());
        Assert.True(dispatcher.ProcessPending() is false);

        Assert.False(ran);
        Assert.True(operation.IsCanceled);
        Assert.False(operation.IsCompleted);
        Assert.False(operation.Cancel());
    }

    [Fact]
    public void ProcessPendingRequiresOwnerThread()
    {
        var dispatcher = new QueuedWpfDispatcherService();
        Exception? exception = null;
        var worker = new Thread(() => exception = Record.Exception(() => dispatcher.ProcessPending()));

        worker.Start();
        worker.Join();

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void CallbackPostedFromWorkerThreadRunsOnOwnerThread()
    {
        var dispatcher = new QueuedWpfDispatcherService();
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var callbackThreadId = 0;
        var worker = new Thread(() => dispatcher.Post(() => callbackThreadId = Environment.CurrentManagedThreadId));

        worker.Start();
        worker.Join();

        Assert.True(dispatcher.ProcessPending());
        Assert.Equal(ownerThreadId, callbackThreadId);
    }
}
