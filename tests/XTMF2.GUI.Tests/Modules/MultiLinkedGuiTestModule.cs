namespace XTMF2.GUI.Tests.Modules;

[Module(Name = "Multi Linked GUI Test Module",
    DocumentationLink = "http://example.com",
    Description = "A minimal module with an unbounded outgoing hook used in GUI tests.")]
public sealed class MultiLinkedGuiTestModule : BaseFunction<string>
{
    [SubModule(Name = "Children", Description = "Linked child modules", Required = false, Index = 0)]
    public SimpleGuiTestModule[] Children { get; set; } = [];

    public override string Invoke() => string.Empty;
}
