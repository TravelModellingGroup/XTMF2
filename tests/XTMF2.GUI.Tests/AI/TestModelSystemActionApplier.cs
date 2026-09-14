using System;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.AI;
using XTMF2.Editing;
using XTMF2.GUI.AI;
using XTMF2.GUI.Tests.Modules;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;

namespace XTMF2.GUI.Tests.AI;

[TestClass]
public sealed class TestModelSystemActionApplier
{
    [TestMethod]
    public void FailedActionReportsItsProposalId()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(FailedActionReportsItsProposalId), (user, _, msSession) =>
        {
            var applier = new ModelSystemActionApplier(msSession, user);
            using var arguments = JsonDocument.Parse(
                "{\"nodeId\":\"11111111-1111-1111-1111-111111111111\",\"value\":\"New name\"}");
            var proposal = new AiActionProposal(
                "missing-node-action",
                AiActionKind.UpdateNode,
                "Rename a node that does not exist",
                arguments.RootElement.Clone());
            var batch = new AiActionBatch("batch-1", "Test batch", [proposal]);

            var result = applier.ApplyAsync(batch).GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccessful);
            Assert.AreEqual("missing-node-action", result.FailedActionId);
        });
    }

    [TestMethod]
    public void CreateNodeGeneratesParameterNodesAndLinks()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(CreateNodeGeneratesParameterNodesAndLinks), (user, _, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var nodeId = Guid.NewGuid();
            var applier = new ModelSystemActionApplier(msSession, user);
            using var arguments = JsonDocument.Parse($"{{\"id\":\"{nodeId}\",\"boundaryId\":\"{boundary.Id}\",\"typeName\":\"{typeof(FailA).FullName}\",\"name\":\"Configured\",\"x\":0,\"y\":0}}");
            var proposal = new AiActionProposal(
                "create-node-with-parameters",
                AiActionKind.CreateNode,
                "Create configured node",
                arguments.RootElement.Clone());

            var result = applier.ApplyAsync(new AiActionBatch("batch-1", "Create node", [proposal]))
                .GetAwaiter()
                .GetResult();

            Assert.IsTrue(result.IsSuccessful, result.Error);
            var node = boundary.Modules.Single(candidate => candidate.Id == nodeId);
            var parameterNode = boundary.Modules.Single(candidate =>
                candidate.Name == "Message" && candidate.ContainedWithin == boundary);
            Assert.IsNotNull(parameterNode.ParameterValue);
            Assert.IsTrue(boundary.Links.Any(link =>
                link.Origin == node &&
                link.OriginHook.Name == "Message" &&
                link is SingleLink singleLink &&
                singleLink.Destination == parameterNode));
        });
    }

    [TestMethod]
    public void CreateNodePreservesRequestedCanvasPosition()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(CreateNodePreservesRequestedCanvasPosition),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                var nodeId = Guid.NewGuid();
                var applier = new ModelSystemActionApplier(msSession, user);
                using var arguments = JsonDocument.Parse(
                    $"{{\"id\":\"{nodeId}\",\"boundaryId\":\"{boundary.Id}\",\"typeName\":\"{typeof(FailA).FullName}\",\"name\":\"Placed\",\"x\":320,\"y\":180}}");
                var proposal = new AiActionProposal("create-placed", AiActionKind.CreateNode,
                    "Create placed node", arguments.RootElement.Clone());

                var result = applier.ApplyAsync(new AiActionBatch("batch-position", "Place node", [proposal]))
                    .GetAwaiter().GetResult();

                Assert.IsTrue(result.IsSuccessful, result.Error);
                var node = boundary.Modules.Single(candidate => candidate.Id == nodeId);
                Assert.AreEqual(320, node.Location.X);
                Assert.AreEqual(180, node.Location.Y);
            });
    }

    [TestMethod]
    public void DuplicateCreateNodeReportsRecoveryGuidance()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(DuplicateCreateNodeReportsRecoveryGuidance),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                var nodeId = Guid.NewGuid();
                var applier = new ModelSystemActionApplier(msSession, user);
                Node? existingNode;
                CommandError? addError;
                Assert.IsTrue(msSession.AddNode(user, boundary, "Existing", typeof(FailA),
                    Rectangle.Hidden, out existingNode, out addError, nodeId), addError?.Message);
                using var arguments = JsonDocument.Parse(
                    $"{{\"id\":\"{nodeId}\",\"boundaryId\":\"{boundary.Id}\",\"typeName\":\"{typeof(FailA).FullName}\",\"name\":\"Existing\"}}");
                var proposal = new AiActionProposal("duplicate-node", AiActionKind.CreateNode,
                    "Repeat node creation", arguments.RootElement.Clone());

                var result = applier.ApplyAsync(new AiActionBatch("batch-duplicate", "Repeat node", [proposal]))
                    .GetAwaiter().GetResult();

                Assert.IsFalse(result.IsSuccessful);
                StringAssert.Contains(result.Error!, "duplicate");
                StringAssert.Contains(result.Error!, "UpdateNode");
                Assert.AreEqual("duplicate-node", result.FailedActionId);
            });
    }

    [TestMethod]
    public void ContextProjectsExistingNodePosition()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(ContextProjectsExistingNodePosition),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(user, boundary, "Existing", typeof(FailA),
                    new Rectangle(410, 230, 120, 50), out var node, out var addError), addError?.Message);

                var snapshot = new ModelSystemContextProjector("model", "test", msSession)
                    .CreateSnapshot(boundary);
                var position = snapshot.Elements.Single(element => element.Id == node!.Id.ToString()).Position;

                Assert.IsNotNull(position);
                Assert.AreEqual(410, position.X);
                Assert.AreEqual(230, position.Y);
                Assert.AreEqual(120, position.Width);
                Assert.AreEqual(50, position.Height);
            });
    }

    [TestMethod]
    public void ContextProjectsAndSearchesCommentBlocksSeparatelyFromElements()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(ContextProjectsAndSearchesCommentBlocksSeparatelyFromElements),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                var commentBlock = new CommentBlock(
                    "Peak period demand is documented here.",
                    new Rectangle(30, 40, 260, 100),
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "Peak period assumptions");
                Assert.IsTrue(msSession.AddCommentBlock(
                    user, boundary, commentBlock.Comment, commentBlock.Location, out var addedBlock, out var addError),
                    addError?.Message);
                Assert.IsNotNull(addedBlock);
                Assert.IsTrue(msSession.SetCommentBlockHeader(
                    user, addedBlock, commentBlock.Header, out addError), addError?.Message);

                var projector = new ModelSystemContextProjector("model", "test", msSession);
                var snapshot = projector.CreateSnapshot(boundary);

                Assert.HasCount(1, snapshot.CommentBlocks!);
                Assert.IsEmpty(snapshot.Elements);
                Assert.AreEqual(addedBlock.Id.ToString(), snapshot.CommentBlocks![0].Id);
                Assert.AreEqual("Peak period assumptions", snapshot.CommentBlocks[0].Header);
                Assert.AreEqual(30, snapshot.CommentBlocks[0].Position!.X);

                var byId = projector.DescribeCommentBlocks([
                    new AiCommentBlockRequest(CommentBlockId: addedBlock.Id.ToString())]);
                var byText = projector.DescribeCommentBlocks([
                    new AiCommentBlockRequest(Query: "demand")]);

                Assert.HasCount(1, byId);
                Assert.HasCount(1, byText);
                Assert.AreEqual(addedBlock.Id.ToString(), byText[0].Id);
            });
    }

    [TestMethod]
    public void CommentBlockActionsCreateUpdateAndDeleteDocumentation()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(CommentBlockActionsCreateUpdateAndDeleteDocumentation),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                var applier = new ModelSystemActionApplier(msSession, user);

                using var createArguments = JsonDocument.Parse($"{{\"boundaryId\":\"{boundary.Id}\",\"comment\":\"Initial note\",\"header\":\"Assumptions\",\"x\":20,\"y\":30,\"width\":240,\"height\":90}}");
                var create = new AiActionProposal(
                    "create-comment",
                    AiActionKind.CreateCommentBlock,
                    "Create documentation note",
                    createArguments.RootElement.Clone());

                var createResult = applier.ApplyAsync(new AiActionBatch("create-batch", "Create note", [create]))
                    .GetAwaiter().GetResult();

                Assert.IsTrue(createResult.IsSuccessful, createResult.Error);
                var block = boundary.CommentBlocks.Single();
                Assert.AreEqual("Initial note", block.Comment);
                Assert.AreEqual("Assumptions", block.Header);
                Assert.AreEqual(20, block.Location.X);

                using var updateArguments = JsonDocument.Parse($"{{\"id\":\"{block.Id}\",\"comment\":\"Updated note\",\"header\":\"Updated assumptions\",\"x\":40,\"y\":50,\"width\":260,\"height\":110}}");
                var update = new AiActionProposal(
                    "update-comment",
                    AiActionKind.UpdateCommentBlock,
                    "Update documentation note",
                    updateArguments.RootElement.Clone());

                var updateResult = applier.ApplyAsync(new AiActionBatch("update-batch", "Update note", [update]))
                    .GetAwaiter().GetResult();

                Assert.IsTrue(updateResult.IsSuccessful, updateResult.Error);
                Assert.AreEqual("Updated note", block.Comment);
                Assert.AreEqual("Updated assumptions", block.Header);
                Assert.AreEqual(40, block.Location.X);
                Assert.AreEqual(260, block.Location.Width);

                using var deleteArguments = JsonDocument.Parse($"{{\"id\":\"{block.Id}\"}}");
                var delete = new AiActionProposal(
                    "delete-comment",
                    AiActionKind.DeleteCommentBlock,
                    "Delete documentation note",
                    deleteArguments.RootElement.Clone());

                var deleteResult = applier.ApplyAsync(new AiActionBatch("delete-batch", "Delete note", [delete]))
                    .GetAwaiter().GetResult();

                Assert.IsTrue(deleteResult.IsSuccessful, deleteResult.Error);
                Assert.IsEmpty(boundary.CommentBlocks);
            });
    }

    [TestMethod]
    public void ContextDescribesNestedBoundaryByIdAndPath()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(ContextDescribesNestedBoundaryByIdAndPath),
            (user, _, msSession) =>
            {
                var root = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddBoundary(user, root, "Nested", out var nested, out var addError),
                    addError?.Message);
                Assert.IsNotNull(nested);
                Assert.IsTrue(msSession.AddNode(user, nested, "NestedNode", typeof(FailA),
                    new Rectangle(10, 20, 120, 50), out var node, out addError), addError?.Message);

                var projector = new ModelSystemContextProjector("model", "test", msSession);
                var byId = projector.DescribeBoundaries([
                    new AiBoundaryRequest(BoundaryId: nested.Id.ToString())]);
                var byPath = projector.DescribeBoundaries([
                    new AiBoundaryRequest(Path: nested.FullPath)]);

                Assert.HasCount(1, byId);
                Assert.HasCount(1, byPath);
                Assert.AreEqual(nested.Id.ToString(), byId[0].Id);
                Assert.AreEqual(nested.FullPath, byPath[0].FullPath);
                Assert.AreEqual(node!.Id.ToString(), byId[0].Elements[0].Id);
                Assert.AreEqual("NestedNode", byPath[0].Elements[0].Name);
            });
    }

    [TestMethod]
    public void ContextProjectsModelSystemVariableNamesAndTypes()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(ContextProjectsModelSystemVariableNamesAndTypes),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(user, boundary, "RunCount", typeof(BasicParameter<int>),
                    Rectangle.Hidden, out var variable, out var addError), addError?.Message);
                Assert.IsTrue(msSession.AddVariable(user, variable!, out var variableError), variableError?.Message);

                var snapshot = new ModelSystemContextProjector("model", "test", msSession)
                    .CreateSnapshot(boundary);

                Assert.IsTrue(snapshot.Variables.TryGetValue("RunCount", out var typeName));
                Assert.AreEqual(typeof(int).FullName, typeName);
            });
    }

    [TestMethod]
    public void ContextUsesCompactModuleIndexAndDetailedLookup()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(ContextUsesCompactModuleIndexAndDetailedLookup),
            (_, _, msSession) =>
            {
                var snapshot = new ModelSystemContextProjector("model", "test", msSession)
                    .CreateSnapshot(msSession.ModelSystem.GlobalBoundary);
                var writeToLog = snapshot.AvailableModules!
                    .Single(module => module.TypeName == typeof(WriteToLogA).FullName);

                Assert.IsEmpty(writeToLog.Members);
                Assert.IsNull(writeToLog.AiInstructions);

                var detailed = new ModelSystemContextProjector("model", "test", msSession)
                    .DescribeModuleType(typeof(WriteToLogA).FullName!);
                Assert.IsNotNull(detailed);
                Assert.IsNotEmpty(detailed.Members);
                StringAssert.Contains(detailed.AiInstructions!, "Execute.To Execute");
                StringAssert.Contains(detailed.AiInstructions!, "parameterName=Message");
            });
    }

    [TestMethod]
    public void ContextIncludesInstructionsForTypesInCurrentBoundary()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ContextIncludesInstructionsForTypesInCurrentBoundary), (user, _, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            Assert.IsTrue(msSession.AddNode(user, boundary, "Execute", typeof(Execute), Rectangle.Hidden,
                out var node, out var addError), addError?.Message);

            var snapshot = new ModelSystemContextProjector("model", "test", msSession)
                .CreateSnapshot(boundary);

            var description = snapshot.CurrentBoundaryModules!
                .Single(module => module.TypeName == typeof(Execute).FullName);
            Assert.IsNotEmpty(description.Members);
            StringAssert.Contains(description.AiInstructions!, "To Execute");
            Assert.IsNull(snapshot.AvailableModules!
                .Single(module => module.TypeName == typeof(Execute).FullName)
                .AiInstructions);
        });
    }

        [TestMethod]
        public void ContextProvidesRuntimeModuleCompositionInstructions()
        {
            TestGuiHelper.RunInModelSystemContext(nameof(ContextProvidesRuntimeModuleCompositionInstructions),
                (_, _, msSession) =>
                {
                    var projector = new ModelSystemContextProjector("model", "test", msSession);

                    var stream = projector.DescribeModuleType(typeof(OpenReadStreamFromMemoryPipe).FullName!);
                    var execution = projector.DescribeModuleType(typeof(Execute).FullName!);
                    var failure = projector.DescribeModuleType(typeof(FailA).FullName!);
                    var conditional = projector.DescribeModuleType(typeof(IfA).FullName!);

                    Assert.IsNotNull(stream);
                    StringAssert.Contains(stream.AiInstructions!, "MemoryPipe");
                    Assert.IsNotNull(execution);
                    StringAssert.Contains(execution.AiInstructions!, "To Execute");
                    Assert.IsNotNull(failure);
                    StringAssert.Contains(failure.AiInstructions!, "Message");
                    Assert.IsNotNull(conditional);
                    StringAssert.Contains(conditional.AiInstructions!, "at least one execution branch");
                    StringAssert.Contains(conditional.AiInstructions!, "If True or If False");
                });
        }

    [TestMethod]
    public void ContextDescribesModuleByExactTypeName()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(ContextDescribesModuleByExactTypeName),
            (_, _, msSession) =>
            {
                var projector = new ModelSystemContextProjector("model", "test", msSession);

                var description = projector.DescribeModuleType(typeof(WriteToLogA).FullName!);

                Assert.IsNotNull(description);
                Assert.AreEqual(typeof(WriteToLogA).FullName, description.TypeName);
                Assert.IsNull(projector.DescribeModuleType("XTMF2.RuntimeModules.DoesNotExist"));
            });
    }

        [TestMethod]
        public void ContextDescribesConnectionBetweenTwoNodes()
        {
            TestGuiHelper.RunInModelSystemContext(nameof(ContextDescribesConnectionBetweenTwoNodes),
                (user, _, msSession) =>
                {
                    var boundary = msSession.ModelSystem.GlobalBoundary;
                    var originId = Guid.NewGuid();
                    var destinationId = Guid.NewGuid();
                    var linkId = Guid.NewGuid();
                    var applier = new ModelSystemActionApplier(msSession, user);
                    var actions = new[]
                    {
                        CreateNodeProposal("origin", originId, boundary.Id, typeof(StartModule), "Origin"),
                        CreateNodeProposal("destination", destinationId, boundary.Id, typeof(FailA), "Destination"),
                        CreateLinkProposal("link", linkId, originId, destinationId, "ToExecute")
                    };

                    var applied = applier.ApplyAsync(new AiActionBatch(
                        "connection-test", "Create connection", actions)).GetAwaiter().GetResult();
                    Assert.IsTrue(applied.IsSuccessful, applied.Error);

                    var projector = new ModelSystemContextProjector("model", "test", msSession);
                    var connections = projector.DescribeConnections(
                        originId.ToString(), destinationId.ToString());
                    var json = JsonSerializer.Serialize(connections);

                    StringAssert.Contains(json, originId.ToString());
                    StringAssert.Contains(json, destinationId.ToString());
                    StringAssert.Contains(json, "ToExecute");
                });
        }

    [TestMethod]
    public void ContextProjectsRequiredHookConnectionState()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(ContextProjectsRequiredHookConnectionState),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user,
                    boundary,
                    "WriteToLogA",
                    typeof(WriteToLogA),
                    Rectangle.Hidden,
                    out var node,
                    out System.Collections.Generic.List<Node>? generatedChildren,
                    out XTMF2.Editing.CommandError? addError), addError?.Message);

                var snapshot = new ModelSystemContextProjector("model", "test", msSession)
                    .CreateSnapshot(boundary);
                var states = snapshot.Elements
                    .Single(element => element.Id == node!.Id.ToString())
                    .AvailableHookStates!;
                var log = states.Single(state => state.Name == "Log");
                var message = states.Single(state => state.Name == "Message");

                Assert.IsTrue(log.Required);
                Assert.IsFalse(log.IsParameter);
                Assert.IsFalse(log.IsConnected);
                Assert.IsTrue(message.Required);
                Assert.IsTrue(message.IsParameter);
                Assert.IsTrue(message.IsConnected);
            });
    }

    [TestMethod]
    public void CreateLinkReplacingSingleHookReturnsAffectedLink()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(CreateLinkReplacingSingleHookReturnsAffectedLink), (user, _, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var originId = Guid.NewGuid();
            var firstDestinationId = Guid.NewGuid();
            var secondDestinationId = Guid.NewGuid();
            var firstLinkId = Guid.NewGuid();
            var secondLinkId = Guid.NewGuid();
            var applier = new ModelSystemActionApplier(msSession, user);
            var actions = new[]
            {
                CreateNodeProposal("origin", originId, boundary.Id, typeof(StartModule), "Origin"),
                CreateNodeProposal("first-destination", firstDestinationId, boundary.Id, typeof(FailA), "First"),
                CreateNodeProposal("second-destination", secondDestinationId, boundary.Id, typeof(FailA), "Second"),
                CreateLinkProposal("first-link", firstLinkId, originId, firstDestinationId),
                CreateLinkProposal("second-link", secondLinkId, originId, secondDestinationId)
            };

            var result = applier.ApplyAsync(new AiActionBatch("batch-1", "Replace single hook", actions))
                .GetAwaiter()
                .GetResult();

            Assert.IsTrue(result.IsSuccessful, result.Error);
            var link = boundary.Links.Single(candidate => candidate.Id == firstLinkId);
            Assert.IsInstanceOfType<SingleLink>(link);
            Assert.AreEqual(secondDestinationId, ((SingleLink)link).Destination!.Id);
        });
    }

    [TestMethod]
    public void AddLinkDestinationAppendsToExistingMultiLink()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(AddLinkDestinationAppendsToExistingMultiLink), (user, _, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var originId = Guid.NewGuid();
            var firstDestinationId = Guid.NewGuid();
            var secondDestinationId = Guid.NewGuid();
            var linkId = Guid.NewGuid();
            var applier = new ModelSystemActionApplier(msSession, user);
            var actions = new[]
            {
                CreateNodeProposal("origin", originId, boundary.Id, typeof(Execute), "Origin"),
                CreateNodeProposal("first-destination", firstDestinationId, boundary.Id, typeof(FailA), "First"),
                CreateNodeProposal("second-destination", secondDestinationId, boundary.Id, typeof(FailA), "Second"),
                CreateLinkProposal("initial-link", linkId, originId, firstDestinationId, "To Execute"),
                CreateAddLinkDestinationProposal("append-destination", originId, secondDestinationId, "To Execute")
            };

            var result = applier.ApplyAsync(new AiActionBatch("batch-1", "Append destination", actions))
                .GetAwaiter()
                .GetResult();

            Assert.IsTrue(result.IsSuccessful, result.Error);
            var link = boundary.Links.OfType<MultiLink>().Single();
            Assert.AreEqual(linkId, link.Id);
            var destinations = link.Destinations.Select(node => node.Id).ToArray();
            CollectionAssert.AreEquivalent(new[] { firstDestinationId, secondDestinationId }, destinations);
        });
    }

    [TestMethod]
    public void CreateLinkSkipsExistingParameterConnection()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(CreateLinkSkipsExistingParameterConnection),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user,
                    boundary,
                    "WriteToLog",
                    typeof(WriteToLogA),
                    Rectangle.Hidden,
                    out var origin,
                    out var generatedChildren,
                    out var addError), addError?.Message);

                var messageHook = origin!.Hooks.Single(hook => hook.Name == "Message");
                var messageChild = generatedChildren!.Single(child => child.Name == "Message");
                var existingLink = boundary.Links.Single(link =>
                    link.Origin == origin && link.OriginHook == messageHook);
                var duplicateLinkId = Guid.NewGuid();
                var proposal = CreateLinkProposal(
                    "duplicate-message-link",
                    duplicateLinkId,
                    origin.Id,
                    messageChild.Id,
                    "Message");

                var result = new ModelSystemActionApplier(msSession, user)
                    .ApplyAsync(new AiActionBatch("duplicate-parameter-link", "Duplicate parameter link", [proposal]))
                    .GetAwaiter()
                    .GetResult();

                Assert.IsTrue(result.IsSuccessful, result.Error);
                Assert.IsNotNull(boundary.Links.SingleOrDefault(link => link.Id == existingLink.Id));
                Assert.AreEqual(1, boundary.Links.Count(link =>
                    link.Origin == origin && link.OriginHook == messageHook));
                Assert.IsNull(boundary.Links.SingleOrDefault(link => link.Id == duplicateLinkId));
            });
    }

    [TestMethod]
    public void CreateNodeCanBeUsedAsCreateLinkDestinationInSameBatch()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(CreateNodeCanBeUsedAsCreateLinkDestinationInSameBatch), (user, _, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var originId = Guid.NewGuid();
            var destinationId = Guid.NewGuid();
            var linkId = Guid.NewGuid();
            var applier = new ModelSystemActionApplier(msSession, user);
            var actions = new[]
            {
                CreateNodeProposal("origin", originId, boundary.Id, typeof(StartModule), "Origin"),
                CreateNodeProposal("created-destination", destinationId, boundary.Id, typeof(FailA), "Created destination"),
                CreateLinkProposal("link-to-created-node", linkId, originId, destinationId)
            };

            var result = applier.ApplyAsync(new AiActionBatch("batch-1", "Link created node", actions))
                .GetAwaiter()
                .GetResult();

            Assert.IsTrue(result.IsSuccessful, result.Error);
            var link = boundary.Links.Single(candidate => candidate.Id == linkId);
            Assert.IsInstanceOfType<SingleLink>(link);
            Assert.AreEqual(destinationId, ((SingleLink)link).Destination!.Id);
        });
    }

    [TestMethod]
    public void RequiredStructuralHookIsValidatedWithoutMutation()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(RequiredStructuralHookIsValidatedWithoutMutation),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                var nodeId = Guid.NewGuid();
                var applier = new ModelSystemActionApplier(msSession, user);
                var create = CreateNodeProposal("create-write-to-log", nodeId, boundary.Id,
                    typeof(WriteToLogA), "WriteToLogA");
                var initialNodeCount = boundary.Modules.Count;

                var result = applier.ValidateAsync(new AiActionBatch(
                    "batch-incomplete-log", "Create incomplete logging action", [create]))
                    .GetAwaiter()
                    .GetResult();

                Assert.IsFalse(result.IsSuccessful);
                Assert.AreEqual("create-write-to-log", result.FailedActionId);
                Assert.IsTrue(result.RequiresModelDecision);
                StringAssert.Contains(result.Error!, "Log");
                StringAssert.Contains(result.Error!, "unsatisfied");
                Assert.HasCount(initialNodeCount, boundary.Modules);
                Assert.IsNull(boundary.Modules.FirstOrDefault(node => node.Id == nodeId));
            });
    }

    [TestMethod]
    public void ApplyAllowsIntentionalMissingRequiredStructuralHook()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(ApplyAllowsIntentionalMissingRequiredStructuralHook),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                var nodeId = Guid.NewGuid();
                var applier = new ModelSystemActionApplier(msSession, user);
                var create = CreateNodeProposal("create-write-to-log", nodeId, boundary.Id,
                    typeof(WriteToLogA), "WriteToLogA");

                var result = applier.ApplyAsync(new AiActionBatch(
                    "batch-intentional-incomplete", "Apply intentional logging action", [create]))
                    .GetAwaiter()
                    .GetResult();

                Assert.IsTrue(result.IsSuccessful, result.Error);
                Assert.IsNotNull(boundary.Modules.FirstOrDefault(node => node.Id == nodeId));
            });
    }

        [TestMethod]
        public void ValidateAsyncRejectsIncompleteGraphWithoutMutation()
        {
            TestGuiHelper.RunInModelSystemContext(nameof(ValidateAsyncRejectsIncompleteGraphWithoutMutation),
                (user, _, msSession) =>
                {
                    var boundary = msSession.ModelSystem.GlobalBoundary;
                    var nodeId = Guid.NewGuid();
                    var applier = new ModelSystemActionApplier(msSession, user);
                    var create = CreateNodeProposal("create-write-to-log", nodeId, boundary.Id,
                        typeof(WriteToLogA), "WriteToLogA");
                    var initialNodeCount = boundary.Modules.Count;

                    var result = applier.ValidateAsync(new AiActionBatch(
                        "batch-incomplete-log", "Validate incomplete logging action", [create]))
                        .GetAwaiter()
                        .GetResult();

                    Assert.IsFalse(result.IsSuccessful);
                    Assert.AreEqual("create-write-to-log", result.FailedActionId);
                    StringAssert.Contains(result.Error!, "Log");
                    Assert.HasCount(initialNodeCount, boundary.Modules);
                    Assert.IsNull(boundary.Modules.FirstOrDefault(node => node.Id == nodeId));
                });
        }

    [TestMethod]
    public void CompleteWriteToLogExecutionChainPassesPreflight()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(CompleteWriteToLogExecutionChainPassesPreflight),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                var executeId = Guid.NewGuid();
                var writeToLogId = Guid.NewGuid();
                var logId = Guid.NewGuid();
                var streamId = Guid.NewGuid();
                var pipeId = Guid.NewGuid();
                var executeLinkId = Guid.NewGuid();
                var logLinkId = Guid.NewGuid();
                var streamLinkId = Guid.NewGuid();
                var pipeLinkId = Guid.NewGuid();
                var applier = new ModelSystemActionApplier(msSession, user);
                var createExecute = CreateNodeProposal("create-execute", executeId, boundary.Id,
                    typeof(Execute), "Execute");
                var createWriteToLog = CreateNodeProposal("create-write-to-log", writeToLogId, boundary.Id,
                    typeof(WriteToLogA), "WriteToLogA");
                var createLog = CreateNodeProposal("create-log", logId, boundary.Id,
                    typeof(Log), "Log");
                var createStream = CreateNodeProposal("create-stream", streamId, boundary.Id,
                    typeof(OpenWriteStreamFromMemoryPipe), "LogStream");
                var createPipe = CreateNodeProposal("create-pipe", pipeId, boundary.Id,
                    typeof(BasicParameter<MemoryPipe>), "Pipe");
                var executeLink = CreateLinkProposal("link-execute", executeLinkId, executeId, writeToLogId,
                    "To Execute");
                var logLink = CreateLinkProposal("link-log", logLinkId, writeToLogId, logId, "Log");
                var streamLink = CreateLinkProposal("link-stream", streamLinkId, logId, streamId, "LogStream");
                var pipeLink = CreateLinkProposal("link-pipe", pipeLinkId, streamId, pipeId, "Pipe");
                using var messageArguments = JsonDocument.Parse(
                    $"{{\"nodeId\":\"{writeToLogId}\",\"parameterName\":\"Message\",\"value\":\"Hello From AI\"}}");
                var setMessage = new AiActionProposal("set-message", AiActionKind.SetBasicParameter,
                    "Set log message", messageArguments.RootElement.Clone());

                var result = applier.ApplyAsync(new AiActionBatch(
                    "batch-complete-log-chain",
                    "Create complete Execute logging chain",
                    [setMessage, executeLink, createLog, streamLink, createWriteToLog,
                        createStream, logLink, createExecute, pipeLink, createPipe]))
                    .GetAwaiter()
                    .GetResult();

                Assert.IsTrue(result.IsSuccessful, result.Error);
                Assert.HasCount(5, boundary.Modules.Where(node =>
                    new[] { executeId, writeToLogId, logId, streamId, pipeId }.Contains(node.Id)));
                Assert.IsTrue(boundary.Links.Any(link => link.Id == executeLinkId));
                Assert.IsTrue(boundary.Links.Any(link => link.Id == logLinkId));
                Assert.IsTrue(boundary.Links.Any(link => link.Id == streamLinkId));
                Assert.IsTrue(boundary.Links.Any(link => link.Id == pipeLinkId));
                var message = boundary.Modules.Single(node => node.Name == "Message");
                Assert.AreEqual("Hello From AI", message.ParameterValue?.Representation);
            });
    }

    [TestMethod]
    public void SetBasicParameterSetsLiteralValue()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(SetBasicParameterSetsLiteralValue), (user, _, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var nodeId = Guid.NewGuid();
            var applier = new ModelSystemActionApplier(msSession, user);
            Assert.IsTrue(msSession.AddNode(user, boundary, "Parameter", typeof(BasicParameter<string>),
                Rectangle.Hidden, out var parameter, out var addError), addError?.Message);
            using var arguments = JsonDocument.Parse(
                $"{{\"nodeId\":\"{parameter!.Id}\",\"value\":\"Hello From AI\"}}");
            var proposal = new AiActionProposal("set-basic", AiActionKind.SetBasicParameter,
                "Set literal parameter", arguments.RootElement.Clone());

            var result = applier.ApplyAsync(new AiActionBatch("batch-basic", "Set basic", [proposal]))
                .GetAwaiter().GetResult();

            Assert.IsTrue(result.IsSuccessful, result.Error);
            Assert.AreEqual("Hello From AI", parameter.ParameterValue?.Representation);
        });
    }

    [TestMethod]
    public void ConvertBasicParameterToScriptedParameterPreservesNodeId()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ConvertBasicParameterToScriptedParameterPreservesNodeId), (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(user, boundary, "Parameter", typeof(BasicParameter<string>),
                    Rectangle.Hidden, out var parameter, out var addError), addError?.Message);
                var originalId = parameter!.Id;
                using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    nodeId = originalId,
                    value = "\"Hello \" + \"AI\""
                }));
                var proposal = new AiActionProposal(
                    "convert-parameter", AiActionKind.ConvertBasicParameterToScriptedParameter,
                    "Convert parameter to an expression", arguments.RootElement.Clone());
                var applier = new ModelSystemActionApplier(msSession, user);

                var result = applier.ApplyAsync(new AiActionBatch(
                    "batch-convert", "Convert parameter", [proposal])).GetAwaiter().GetResult();

                Assert.IsTrue(result.IsSuccessful, result.Error);
                Assert.AreEqual(originalId, parameter.Id);
                Assert.AreEqual(typeof(ScriptedParameter<string>), parameter.Type);
                Assert.AreEqual("\"Hello \" + \"AI\"", parameter.ParameterValue?.Representation);
            });
    }

    [TestMethod]
    public void CreateBasicParameterThenAssignsValueInSameBatch()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(CreateBasicParameterThenAssignsValueInSameBatch),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                var nodeId = Guid.NewGuid();
                var applier = new ModelSystemActionApplier(msSession, user);
                var create = CreateNodeProposal("create-message", nodeId, boundary.Id,
                    typeof(BasicParameter<string>), "Message");
                using var arguments = JsonDocument.Parse(
                    $"{{\"nodeId\":\"{nodeId}\",\"value\":\"Hello From AI\"}}");
                var setValue = new AiActionProposal("set-message-text", AiActionKind.SetBasicParameter,
                    "Set message text", arguments.RootElement.Clone());

                var result = applier.ApplyAsync(new AiActionBatch("batch-message", "Create message", [
                    setValue,
                    create
                ])).GetAwaiter().GetResult();

                Assert.IsTrue(result.IsSuccessful, result.Error);
                var parameter = boundary.Modules.Single(candidate => candidate.Id == nodeId);
                Assert.AreEqual("Hello From AI", parameter.ParameterValue?.Representation);
            });
    }

    [TestMethod]
    public void SetBasicParameterResolvesGeneratedChildFromOwningNode()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(SetBasicParameterResolvesGeneratedChildFromOwningNode),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                var nodeId = Guid.NewGuid();
                var applier = new ModelSystemActionApplier(msSession, user);
                var create = CreateNodeProposal("create-owner", nodeId, boundary.Id, typeof(FailA), "HelloFromAI");
                using var arguments = JsonDocument.Parse(
                    $"{{\"nodeId\":\"{nodeId}\",\"parameterName\":\"Message\",\"value\":\"Hello From AI\"}}");
                var setValue = new AiActionProposal("set-msg", AiActionKind.SetBasicParameter,
                    "Set generated message parameter", arguments.RootElement.Clone());

                var result = applier.ApplyAsync(new AiActionBatch("batch-generated-parameter", "Configure generated parameter",
                    [create, setValue])).GetAwaiter().GetResult();

                Assert.IsTrue(result.IsSuccessful, result.Error);
                var parameter = boundary.Modules.Single(candidate => candidate.Name == "Message");
                Assert.AreEqual("Hello From AI", parameter.ParameterValue?.Representation);
            });
    }

    [TestMethod]
    public void SetScriptedParameterSetsExpression()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(SetScriptedParameterSetsExpression), (user, _, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var applier = new ModelSystemActionApplier(msSession, user);
            Assert.IsTrue(msSession.AddNode(user, boundary, "Expression", typeof(ScriptedParameter<string>),
                Rectangle.Hidden, out var parameter, out var addError), addError?.Message);
            using var arguments = JsonDocument.Parse(
                $"{{\"nodeId\":\"{parameter!.Id}\",\"value\":\"\\\"Hello From AI\\\"\"}}");
            var proposal = new AiActionProposal("set-scripted", AiActionKind.SetScriptedParameter,
                "Set scripted parameter", arguments.RootElement.Clone());

            var result = applier.ApplyAsync(new AiActionBatch("batch-scripted", "Set scripted", [proposal]))
                .GetAwaiter().GetResult();

            Assert.IsTrue(result.IsSuccessful, result.Error);
            Assert.AreEqual("\"Hello From AI\"", parameter.ParameterValue?.Representation);
        });
    }

    [TestMethod]
    public void SetScriptedParameterConvertsBasicParameter()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(SetScriptedParameterConvertsBasicParameter), (user, _, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            Assert.IsTrue(msSession.AddNode(user, boundary, "CurrentIteration", typeof(BasicParameter<int>),
                Rectangle.Hidden, out var iteration, out var iterationError), iterationError?.Message);
            Assert.IsTrue(msSession.AddVariable(user, iteration!, out var variableError), variableError?.Message);
            Assert.IsTrue(msSession.AddNode(user, boundary, "Condition", typeof(BasicParameter<bool>),
                Rectangle.Hidden, out var parameter, out var addError), addError?.Message);
            var originalId = parameter!.Id;
            using var arguments = JsonDocument.Parse(
                $"{{\"nodeId\":\"{originalId}\",\"value\":\"CurrentIteration == 1\"}}");
            var proposal = new AiActionProposal("set-condition", AiActionKind.SetScriptedParameter,
                "Set condition expression", arguments.RootElement.Clone());

            var result = new ModelSystemActionApplier(msSession, user).ApplyAsync(new AiActionBatch(
                "batch-condition", "Set condition", [proposal])).GetAwaiter().GetResult();

            Assert.IsTrue(result.IsSuccessful, result.Error);
            Assert.AreEqual(originalId, parameter.Id);
            Assert.AreEqual(typeof(ScriptedParameter<bool>), parameter.Type);
            Assert.AreEqual("CurrentIteration == 1", parameter.ParameterValue?.Representation);
        });
    }

    [TestMethod]
    public void SetScriptedParameterSupportsImplicitStringConversion()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(SetScriptedParameterSupportsImplicitStringConversion), (user, _, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            Assert.IsTrue(msSession.AddNode(user, boundary, "CurrentIteration", typeof(BasicParameter<int>),
                Rectangle.Hidden, out var iteration, out var iterationError), iterationError?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, iteration!, "3", out var valueError),
                valueError?.Message);
            Assert.IsTrue(msSession.AddVariable(user, iteration!, out var variableError), variableError?.Message);
            Assert.IsTrue(msSession.AddNode(user, boundary, "Message", typeof(ScriptedParameter<string>),
                Rectangle.Hidden, out var message, out var messageError), messageError?.Message);

            using var arguments = JsonDocument.Parse(
                $"{{\"nodeId\":\"{message!.Id}\",\"value\":\"\\\"Iteration: \\\" + CurrentIteration\"}}");
            var proposal = new AiActionProposal("set-message-expression", AiActionKind.SetScriptedParameter,
                "Set iteration message", arguments.RootElement.Clone());
            var result = new ModelSystemActionApplier(msSession, user).ApplyAsync(new AiActionBatch(
                "batch-string-conversion", "Set iteration message", [proposal])).GetAwaiter().GetResult();

            Assert.IsTrue(result.IsSuccessful, result.Error);
            Assert.AreEqual("\"Iteration: \" + CurrentIteration", message.ParameterValue?.Representation);
        });
    }

    [TestMethod]
    public void ScriptedParameterFailureDoesNotReportChildParameters()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ScriptedParameterFailureDoesNotReportChildParameters), (user, _, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            Assert.IsTrue(msSession.AddNode(user, boundary, "Message", typeof(ScriptedParameter<string>),
                Rectangle.Hidden, out var message, out var messageError), messageError?.Message);
            using var arguments = JsonDocument.Parse(
                $"{{\"nodeId\":\"{message!.Id}\",\"value\":\"CurrentIteration.ToString()\"}}");
            var proposal = new AiActionProposal("invalid-message-expression", AiActionKind.SetScriptedParameter,
                "Set invalid message expression", arguments.RootElement.Clone());

            var result = new ModelSystemActionApplier(msSession, user).ApplyAsync(new AiActionBatch(
                "batch-invalid-expression", "Set invalid expression", [proposal])).GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccessful);
            StringAssert.Contains(result.Error, "do not support C# methods");
            Assert.IsFalse(result.Error.Contains("Available parameters: none", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public void SetScriptedParameterConvertsBasicParameterWithStringExpression()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(SetScriptedParameterConvertsBasicParameterWithStringExpression), (user, _, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var applier = new ModelSystemActionApplier(msSession, user);
            Assert.IsTrue(msSession.AddNode(user, boundary, "Parameter", typeof(BasicParameter<string>),
                Rectangle.Hidden, out var parameter, out var addError), addError?.Message);
            using var arguments = JsonDocument.Parse(
                $"{{\"nodeId\":\"{parameter!.Id}\",\"value\":\"\\\"x\\\" + \\\"y\\\"\"}}");
            var proposal = new AiActionProposal("wrong-kind", AiActionKind.SetScriptedParameter,
                "Set scripted parameter", arguments.RootElement.Clone());

            var result = applier.ApplyAsync(new AiActionBatch("batch-wrong-kind", "Wrong kind", [proposal]))
                .GetAwaiter().GetResult();

            Assert.IsTrue(result.IsSuccessful, result.Error);
            Assert.AreEqual(typeof(ScriptedParameter<string>), parameter.Type);
            Assert.AreEqual("\"x\" + \"y\"", parameter.ParameterValue?.Representation);
        });
    }

    private static AiActionProposal CreateNodeProposal(
        string proposalId,
        Guid nodeId,
        Guid boundaryId,
        Type type,
        string name)
    {
        using var arguments = JsonDocument.Parse(
            $"{{\"id\":\"{nodeId}\",\"boundaryId\":\"{boundaryId}\",\"typeName\":\"{type.FullName}\",\"name\":\"{name}\"}}");
        return new AiActionProposal(proposalId, AiActionKind.CreateNode, name, arguments.RootElement.Clone());
    }

    private static AiActionProposal CreateLinkProposal(
        string proposalId,
        Guid linkId,
        Guid originId,
        Guid destinationId,
        string hookName = "ToExecute")
    {
        using var arguments = JsonDocument.Parse(
            $"{{\"id\":\"{linkId}\",\"originId\":\"{originId}\",\"destinationId\":\"{destinationId}\",\"hookName\":\"{hookName}\"}}");
        return new AiActionProposal(proposalId, AiActionKind.CreateLink, "Connect nodes", arguments.RootElement.Clone());
    }

    private static AiActionProposal CreateAddLinkDestinationProposal(
        string proposalId,
        Guid originId,
        Guid destinationId,
        string hookName)
    {
        using var arguments = JsonDocument.Parse(
            $"{{\"originId\":\"{originId}\",\"destinationId\":\"{destinationId}\",\"hookName\":\"{hookName}\"}}");
        return new AiActionProposal(proposalId, AiActionKind.AddLinkDestination, "Append destination", arguments.RootElement.Clone());
    }
}
