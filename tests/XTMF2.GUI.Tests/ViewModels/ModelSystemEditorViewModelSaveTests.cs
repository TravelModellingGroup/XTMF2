using Avalonia.Headless;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Tests.Modules;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public class ModelSystemEditorViewModelSaveTests
{
    [TestMethod]
    public void SaveModelSystemAsync_DisablesCommandAndRejectsOverlappingSave()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(SaveModelSystemAsync_DisablesCommandAndRejectsOverlappingSave),
            (user, _, session) =>
            {
                Assert.IsTrue(session.AddNode(user, session.ModelSystem.GlobalBoundary,
                    "SaveTestNode", typeof(SimpleGuiTestModule), Rectangle.Hidden,
                    out var node, out var error), error?.Message);
                Assert.IsNotNull(node);
                Assert.IsTrue(session.IsDirty);

                using var viewModel = new ModelSystemEditorViewModel(session, user, runController: null);
                Task<bool>? overlappingSave = null;
                bool commandDisabledWhileSaving = false;
                viewModel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(viewModel.IsSaving) && viewModel.IsSaving)
                    {
                        commandDisabledWhileSaving = !viewModel.SaveModelSystemCommand.CanExecute(null);
                        overlappingSave = viewModel.SaveModelSystemAsync();
                    }
                };

                Task<bool>? save = null;
                HeadlessAppLifetime.HeadlessSession!.Dispatch(
                    () => save = viewModel.SaveModelSystemAsync(),
                    System.Threading.CancellationToken.None).GetAwaiter().GetResult();

                Assert.IsTrue(save!.GetAwaiter().GetResult());
                Assert.IsNotNull(overlappingSave);
                Assert.IsFalse(overlappingSave!.GetAwaiter().GetResult());
                Assert.IsTrue(commandDisabledWhileSaving);
                Assert.IsFalse(viewModel.IsSaving);
                Assert.IsFalse(session.IsDirty);
                Assert.IsTrue(viewModel.SaveModelSystemCommand.CanExecute(null));
            });
    }
}