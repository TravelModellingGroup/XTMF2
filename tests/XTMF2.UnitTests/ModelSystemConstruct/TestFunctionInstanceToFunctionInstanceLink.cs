/*
    Copyright 2026 University of Toronto

    This file is part of XTMF2.

    XTMF2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/>.
*/
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;
using XTMF2.UnitTests.Modules;
using static XTMF2.UnitTests.TestHelper;

namespace XTMF2.UnitTests.ModelSystemConstruct;

/// <summary>
/// Regression test for the bug where a link from an inner node of a FunctionTemplate's
/// InternalModules boundary to a FunctionInstance on the outer boundary was silently
/// ignored during construction because <c>ResolveRuntimeDestModule</c> fell back to
/// <c>Node.Module</c> (always <c>null</c> for FunctionInstances) instead of calling
/// <c>FunctionInstance.GetRuntimeModule</c>.
/// </summary>
[TestClass]
public class TestFunctionInstanceToFunctionInstanceLink
{
    /// <summary>
    /// Builds a model system where an inner node of FT_Inner links to a FunctionInstance
    /// of FT_Outer that lives on the outer boundary, then verifies the model system
    /// constructs and runs to completion without error.
    ///
    /// Layout:
    ///   FT_Outer  – entry node: SimpleTestModule       (IFunction&lt;string&gt;)
    ///   FT_Inner  – entry node: SimpleParameterModule  (IFunction&lt;string&gt;)
    ///                 inner link: Consumer.RealValue → FI_Outer
    ///
    ///   Outer boundary:
    ///     Start → IgnoreResult&lt;string&gt; → FI_Inner
    ///     FI_Outer  (standalone, supplies the IFunction&lt;string&gt; to FI_Inner's inner node)
    /// </summary>
    [TestMethod]
    public void InnerNodeLinkingToFunctionInstance_ConstructsAndRunsSuccessfully()
    {
        RunInModelSystemContext(
            nameof(InnerNodeLinkingToFunctionInstance_ConstructsAndRunsSuccessfully),
            (user, pSession, msSession) =>
            {
                var ms       = msSession.ModelSystem;
                var boundary = ms.GlobalBoundary;
                CommandError error = null;

                // ── FT_Outer: SimpleTestModule is the entry node (returns "Hello World") ──
                Assert.IsTrue(msSession.AddFunctionTemplate(user, boundary, "FT_Outer",
                    out var ftOuter, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ftOuter!.InternalModules, "Provider",
                    typeof(SimpleTestModule), Rectangle.Hidden, out var providerNode, out error),
                    error?.Message);
                Assert.IsTrue(msSession.SetFunctionTemplateEntryNode(user, ftOuter, providerNode,
                    out error), error?.Message);

                // ── FT_Inner: SimpleParameterModule is the entry node ────────────────────
                //    Its RealValue hook will be wired to FI_Outer (cross-boundary link).
                Assert.IsTrue(msSession.AddFunctionTemplate(user, boundary, "FT_Inner",
                    out var ftInner, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ftInner!.InternalModules, "Consumer",
                    typeof(SimpleParameterModule), Rectangle.Hidden, out var consumerNode, out error),
                    error?.Message);
                Assert.IsTrue(msSession.SetFunctionTemplateEntryNode(user, ftInner, consumerNode,
                    out error), error?.Message);

                // ── Place FunctionInstances on the outer boundary ─────────────────────────
                Assert.IsTrue(msSession.AddFunctionInstance(user, boundary, ftOuter, "FI_Outer",
                    Rectangle.Hidden, out var fiOuter, out error), error?.Message);
                Assert.IsTrue(msSession.AddFunctionInstance(user, boundary, ftInner, "FI_Inner",
                    Rectangle.Hidden, out var fiInner, out error), error?.Message);

                // ── Cross-boundary link: consumerNode.RealValue (Hooks[0]) → FI_Outer ────
                //    Stored in ftInner.InternalModules; processed by ConstructRuntimeLinks.
                Assert.IsTrue(msSession.AddLink(user, consumerNode!, consumerNode!.Hooks[0],
                    fiOuter!, out _, out error), error?.Message);

                // ── Outer execution chain: Start → IgnoreResult<string> → FI_Inner ───────
                Assert.IsTrue(msSession.AddModelSystemStart(user, boundary, "Start",
                    Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, boundary, "Ignore",
                    typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreNode, out error),
                    error?.Message);
                Assert.IsTrue(msSession.AddLink(user, start!, start!.Hooks[0], ignoreNode!,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, ignoreNode!, ignoreNode!.Hooks[0], fiInner!,
                    out _, out error), error?.Message);

                // ── Run and assert success ────────────────────────────────────────────────
                CreateRunClient(true, (runBus) =>
                {
                    bool success = false;
                    CommandError runError = null;
                    using var sem = new SemaphoreSlim(0);

                    runBus.ClientFinishedModelSystem += (_, _) =>
                    {
                        success = true;
                        sem.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (_, _, e, stack) =>
                    {
                        runError = new CommandError(e + "\r\n" + stack);
                        sem.Release();
                    };

                    Assert.IsTrue(runBus.RunModelSystem(msSession,
                        Path.Combine(pSession.RunsDirectory, "InnerFIToOuterFI"),
                        "Start", out _, out runError), runError?.Message);

                    if (!sem.Wait(2000))
                        Assert.Fail("Model system did not complete within the time limit.");

                    Assert.IsTrue(success,
                        "Model system failed to run to completion: " + runError?.Message);
                });
            });
    }
}
