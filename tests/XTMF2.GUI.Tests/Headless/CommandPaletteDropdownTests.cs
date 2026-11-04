using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Views;

namespace XTMF2.GUI.Tests.Headless;

[TestClass]
public class CommandPaletteDropdownTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void CommandPalette_LoadsCompiledXamlAndSelectsFirstEnabledAction()
    {
        Session.Dispatch(() =>
        {
            var palette = new CommandPaletteDropdown();
            palette.SetActions(
            [
                new CommandPaletteAction("Disabled", () => { }, isEnabled: false),
                new CommandPaletteAction("Enabled", () => { })
            ]);

            var actionList = palette.FindControl<ListBox>("ActionList");
            Assert.IsNotNull(actionList);
            Assert.IsNull(palette.FindControl<Button>("OpenPaletteButton"));
            Assert.AreEqual("Enabled", (actionList.SelectedItem as CommandPaletteAction)?.DisplayName);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void CommandPalette_CentersOnItsTarget()
    {
        Session.Dispatch(() =>
        {
            var palette = new CommandPaletteDropdown();
            var target = new Border();

            palette.CenterPopupOn(target);

            var popup = palette.FindControl<Popup>("ActionPopup")!;
            Assert.AreSame(target, popup.PlacementTarget);
            Assert.AreEqual(PlacementMode.Center, popup.Placement);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void CommandPalette_ShortcutHeaderStretchesToRightAlignWithShortcuts()
    {
        Session.Dispatch(() =>
        {
            var palette = new CommandPaletteDropdown();
            var shortcutHeader = palette.FindControl<TextBlock>("ShortcutHeader");

            Assert.IsNotNull(shortcutHeader);
            Assert.AreEqual(HorizontalAlignment.Stretch, shortcutHeader.HorizontalAlignment);
            Assert.AreEqual(TextAlignment.Right, shortcutHeader.TextAlignment);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void CommandPalette_FiltersActionsAsTheUserTypes()
    {
        Session.Dispatch(() =>
        {
            var palette = new CommandPaletteDropdown();
            palette.SetActions(
            [
                new CommandPaletteAction("Run model system", () => { }),
                new CommandPaletteAction("Run estimation", () => { })
            ]);

            var searchBox = palette.FindControl<TextBox>("SearchBox")!;
            searchBox.Text = "estimation";

            var actionList = palette.FindControl<ListBox>("ActionList")!;
            Assert.AreEqual("estimation", searchBox.Text);
            Assert.HasCount(1, palette.FilteredActions);
            Assert.HasCount(1, actionList.Items);
            Assert.AreEqual("Run estimation", (actionList.SelectedItem as CommandPaletteAction)?.DisplayName);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void Escape_ClosesDropdownAndRequestsCanvasFocus()
    {
        Session.Dispatch(() =>
        {
            var palette = new CommandPaletteDropdown();
            var focusRequested = false;
            palette.CanvasFocusRequested += (_, _) => focusRequested = true;
            palette.FocusSearch();

            var searchBox = palette.FindControl<TextBox>("SearchBox")!;
            var keyEvent = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape
            };
            searchBox.RaiseEvent(keyEvent);

            Assert.IsTrue(keyEvent.Handled);
            Assert.IsFalse(palette.FindControl<Popup>("ActionPopup")!.IsOpen);
            Assert.IsTrue(focusRequested);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }
}
