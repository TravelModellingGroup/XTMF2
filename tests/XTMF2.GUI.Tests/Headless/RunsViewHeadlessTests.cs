using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using XTMF2.Bus;
using XTMF2.GUI.ViewModels;
using XTMF2.GUI.Views;

namespace XTMF2.GUI.Tests.Headless;

[TestClass]
public class RunsViewHeadlessTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void RunLogSupportsSelectingAllTextForCopy()
    {
        Session.Dispatch(() =>
        {
            var view = new RunsView();
            var messageLog = view.FindControl<SelectableTextBlock>("MessageTextBlock");

            Assert.IsNotNull(messageLog);
            messageLog!.Text = $"newest message{System.Environment.NewLine}older message";
            messageLog.SelectAll();

            Assert.AreEqual(messageLog.Text, messageLog.SelectedText);
            Assert.IsTrue(messageLog.CanCopy);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void RunOutputAndCancelButtonsAreRightAlignedBesideConsoleTitle()
    {
        Session.Dispatch(() =>
        {
            var run = new RunViewModel("run-icons", "run icons",
                System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "xtmf-run-icons-" + Guid.NewGuid().ToString("N")),
                "Local RunServer", null!, null!);
            run.SetRunMode(RunMode.Normal,
                Array.Empty<(int nodeIndex, string name, double min, double max)>(), () => null);
            var runsViewModel = new RunsViewModel { SelectedRun = run };
            var view = new RunsView { DataContext = runsViewModel };
            var openButton = view.FindControl<Button>("OpenRunDirectoryButton");
            var cancelButton = view.FindControl<Button>("CancelRunButton");

            Assert.IsNotNull(openButton);
            Assert.IsNotNull(cancelButton);
            Assert.IsTrue(openButton!.IsVisible);
            Assert.IsTrue(cancelButton!.IsVisible);
            Assert.AreEqual(1, Grid.GetColumn(openButton));
            Assert.AreEqual(2, Grid.GetColumn(cancelButton));
            Assert.AreEqual(HorizontalAlignment.Center, openButton.HorizontalContentAlignment);
            Assert.AreEqual(VerticalAlignment.Center, openButton.VerticalContentAlignment);
            Assert.AreEqual(HorizontalAlignment.Right, cancelButton.HorizontalAlignment);
            Assert.AreEqual(HorizontalAlignment.Center, cancelButton.HorizontalContentAlignment);
            Assert.AreEqual(VerticalAlignment.Center, cancelButton.VerticalContentAlignment);
            Assert.IsTrue(openButton.IsEnabled);
            Assert.IsTrue(cancelButton.IsEnabled);

            run.MarkFinished();

            Assert.IsTrue(openButton.IsVisible);
            Assert.IsTrue(cancelButton.IsVisible);
            Assert.IsTrue(openButton.IsEnabled);
            Assert.IsFalse(cancelButton.IsEnabled);

            run.SetRemoteRunTracking(true);

            Assert.IsFalse(run.CanOpenRunDirectory);
            Assert.IsFalse(openButton.IsEnabled);
            Assert.IsFalse(cancelButton.IsEnabled);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public async Task RunStatusBurstIsAppliedInCoalescedBatches()
    {
        RunViewModel? run = null;
        var cancellationProcessed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finalBatchApplied = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var messageLogUpdates = 0;

        Session.Dispatch(() =>
        {
            var createdRun = new RunViewModel(
                "run-burst", "run burst", string.Empty, "local", null!, null!);
            run = createdRun;
            createdRun.SetRunMode(
                RunMode.Estimation,
                Array.Empty<(int nodeIndex, string name, double min, double max)>(),
                () =>
                {
                    cancellationProcessed.TrySetResult(true);
                    return null;
                });
            createdRun.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(RunViewModel.MessageLog))
                    return;
                messageLogUpdates++;
                if (createdRun.Messages.Count == 200 && createdRun.Messages[0] == "status 999")
                    finalBatchApplied.TrySetResult();
            };

            for (int i = 0; i < 1_000; i++)
                createdRun.QueueStatus($"status {i}");
            Dispatcher.UIThread.Post(
                () => createdRun.CancelRunCommand.Execute(null),
                DispatcherPriority.Input);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();

        Assert.IsTrue(await cancellationProcessed.Task.WaitAsync(TimeSpan.FromMilliseconds(TestGuiHelper.TestTimeoutMilliseconds)),
            "The cancel command should run before the queued log batches are drained.");
        await finalBatchApplied.Task.WaitAsync(TimeSpan.FromMilliseconds(TestGuiHelper.TestTimeoutMilliseconds));

        Assert.HasCount(200, run!.Messages);
        Assert.AreEqual("status 999", run.Messages[0]);
        Assert.AreEqual("status 999", run.StatusText);
        Assert.IsLessThan(200, messageLogUpdates,
            "The selectable log should be refreshed once per batch, not once per status.");
    }

    [TestMethod]
    public void CancelRunCommandShowsRequestAndAllowsRetryAfterFailure()
    {
        var cancellationAttempts = 0;
        var run = new RunViewModel(
            "run-cancel", "run cancel", string.Empty, "local", null!, null!);
        run.SetRunMode(
            RunMode.Normal,
            Array.Empty<(int nodeIndex, string name, double min, double max)>(),
            () => ++cancellationAttempts == 1 ? "RunServer disconnected." : null);

        Assert.IsTrue(run.CanCancelRun);
        Assert.IsTrue(run.CanRequestCancelRun);
        Assert.IsTrue(run.CancelRunCommand.CanExecute(null));

        run.CancelRunCommand.Execute(null);

        Assert.AreEqual("Cancellation request failed.", run.StatusText);
        Assert.IsFalse(run.IsCancellationRequested);
        Assert.IsTrue(run.CanRequestCancelRun);
        Assert.IsTrue(run.CancelRunCommand.CanExecute(null));
        Assert.AreEqual("Unable to cancel run: RunServer disconnected.", run.Messages[0]);

        run.CancelRunCommand.Execute(null);

        Assert.AreEqual(2, cancellationAttempts);
        Assert.IsTrue(run.IsCancellationRequested);
        Assert.AreEqual("Cancellation requested…", run.StatusText);
        Assert.IsTrue(run.CanCancelRun);
        Assert.IsFalse(run.CanRequestCancelRun);
        Assert.IsFalse(run.CancelRunCommand.CanExecute(null));

        run.MarkCancelled();

        Assert.IsFalse(run.CanCancelRun);
        Assert.IsFalse(run.IsCancellationRequested);
        Assert.IsFalse(run.CancelRunCommand.CanExecute(null));
        Assert.AreEqual(RunStatus.Cancelled, run.Status);
    }
}
