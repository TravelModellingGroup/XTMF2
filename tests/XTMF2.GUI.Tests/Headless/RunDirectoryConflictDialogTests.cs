using Avalonia.Headless;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Views;

namespace XTMF2.GUI.Tests.Headless;

[TestClass]
public class RunDirectoryConflictDialogTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void KeepExistingFilesAcceptsWithoutRequestingDirectoryClear()
    {
        Session.Dispatch(() =>
        {
            var dialog = new RunDirectoryConflictDialog("/runs/Model");

            Assert.IsTrue(dialog.WasCancelled);
            dialog.ChooseKeepExistingFiles();

            Assert.IsFalse(dialog.WasCancelled);
            Assert.IsFalse(dialog.OverwriteExistingFiles);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void OverwriteChoiceRequestsDirectoryClear()
    {
        Session.Dispatch(() =>
        {
            var dialog = new RunDirectoryConflictDialog("/runs/Model");

            dialog.ChooseOverwriteExistingFiles();

            Assert.IsFalse(dialog.WasCancelled);
            Assert.IsTrue(dialog.OverwriteExistingFiles);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }
}
