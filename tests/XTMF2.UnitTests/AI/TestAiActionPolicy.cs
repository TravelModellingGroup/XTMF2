using System.Collections.Generic;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.AI;

namespace XTMF2.UnitTests.AI;

[TestClass]
public sealed class TestAiActionPolicy
{
    [TestMethod]
    public void SuggestOnlyRejectsExecution()
    {
        var result = AiActionPolicy.ValidateForExecution(
            CreateBatch(),
            AiAutonomyPolicy.SuggestOnly,
            approvalGranted: false,
            destructiveApprovalGranted: false);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.Error!, "Suggest-only");
    }

    [TestMethod]
    public void ApprovedBatchAllowsNonDestructiveActions()
    {
        var result = AiActionPolicy.ValidateForExecution(
            CreateBatch(),
            AiAutonomyPolicy.ApproveBatch,
            approvalGranted: true,
            destructiveApprovalGranted: false);

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void AutonomousModeStillRequiresDestructiveApproval()
    {
        var result = AiActionPolicy.ValidateForExecution(
            CreateBatch(isDestructive: true),
            AiAutonomyPolicy.Autonomous,
            approvalGranted: false,
            destructiveApprovalGranted: false);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.Error!, "Destructive");
    }

    [TestMethod]
    public void DuplicateActionIdsAreRejected()
    {
        using var document = JsonDocument.Parse("{}");
        var action = new AiActionProposal("same", AiActionKind.UpdateNode, "Update", document.RootElement.Clone());
        var batch = new AiActionBatch("batch-1", "Duplicate", [action, action]);

        var result = AiActionPolicy.ValidateForExecution(
            batch,
            AiAutonomyPolicy.Autonomous,
            approvalGranted: false,
            destructiveApprovalGranted: false);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.Error!, "duplicate");
    }

    private static AiActionBatch CreateBatch(bool isDestructive = false)
    {
        using var document = JsonDocument.Parse("{}");
        var action = new AiActionProposal(
            "action-1",
            isDestructive ? AiActionKind.DeleteNode : AiActionKind.UpdateNode,
            "Update the model system",
            document.RootElement.Clone(),
            isDestructive);
        return new AiActionBatch("batch-1", "Update the model system", new List<AiActionProposal> { action });
    }
}