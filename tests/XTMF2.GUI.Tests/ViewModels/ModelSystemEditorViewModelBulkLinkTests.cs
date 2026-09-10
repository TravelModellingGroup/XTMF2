using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Tests.Modules;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public class ModelSystemEditorViewModelBulkLinkTests
{
    [TestMethod]
    public void BulkLinkSelectedAsync_LinksOrderedDestinationsAsOneUndoableAction()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(BulkLinkSelectedAsync_LinksOrderedDestinationsAsOneUndoableAction),
            (user, _, session) =>
            {
                var boundary = session.ModelSystem.GlobalBoundary;
                Assert.IsTrue(session.AddNode(user, boundary, "Origin",
                    typeof(MultiLinkedGuiTestModule), new Rectangle(20, 20, 160, 60),
                    out var origin, out var originError), originError?.Message);
                Assert.IsTrue(session.AddNode(user, boundary, "First",
                    typeof(SimpleGuiTestModule), new Rectangle(240, 20, 160, 60),
                    out var first, out var firstError), firstError?.Message);
                Assert.IsTrue(session.AddNode(user, boundary, "Second",
                    typeof(SimpleGuiTestModule), new Rectangle(460, 20, 160, 60),
                    out var second, out var secondError), secondError?.Message);

                using var editor = new ModelSystemEditorViewModel(session, user, runController: null);
                var originVm = editor.Nodes.Single(vm => vm.UnderlyingNode == origin);
                var firstVm = editor.Nodes.Single(vm => vm.UnderlyingNode == first);
                var secondVm = editor.Nodes.Single(vm => vm.UnderlyingNode == second);

                editor.BulkLinkSelectedAsync(new ICanvasElement[] { originVm, firstVm, secondVm })
                    .GetAwaiter().GetResult();

                var link = boundary.Links.OfType<MultiLink>().Single();
                CollectionAssert.AreEqual(new[] { first, second }, link.Destinations.ToArray());

                Assert.IsTrue(session.Undo(user, out var undoError), undoError?.Message);
                Assert.IsEmpty(boundary.Links);
                Assert.IsTrue(session.Redo(user, out var redoError), redoError?.Message);
                var redoneLink = boundary.Links.OfType<MultiLink>().Single();
                CollectionAssert.AreEqual(new[] { first, second }, redoneLink.Destinations.ToArray());
            });
    }

    [TestMethod]
    public void BulkLinkSelectedAsync_RejectsSingleCardinalityBeforeCreatingLinks()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(BulkLinkSelectedAsync_RejectsSingleCardinalityBeforeCreatingLinks),
            (user, _, session) =>
            {
                var boundary = session.ModelSystem.GlobalBoundary;
                Assert.IsTrue(session.AddNode(user, boundary, "Origin",
                    typeof(LinkedGuiTestModule), new Rectangle(20, 20, 160, 60),
                    out var origin, out var originError), originError?.Message);
                Assert.IsTrue(session.AddNode(user, boundary, "First",
                    typeof(SimpleGuiTestModule), new Rectangle(240, 20, 160, 60),
                    out var first, out var firstError), firstError?.Message);
                Assert.IsTrue(session.AddNode(user, boundary, "Second",
                    typeof(SimpleGuiTestModule), new Rectangle(460, 20, 160, 60),
                    out var second, out var secondError), secondError?.Message);

                using var editor = new ModelSystemEditorViewModel(session, user, runController: null);
                var selection = new ICanvasElement[]
                {
                    editor.Nodes.Single(vm => vm.UnderlyingNode == origin),
                    editor.Nodes.Single(vm => vm.UnderlyingNode == first),
                    editor.Nodes.Single(vm => vm.UnderlyingNode == second)
                };

                editor.BulkLinkSelectedAsync(selection).GetAwaiter().GetResult();

                Assert.IsEmpty(boundary.Links);
            });
    }
}
