using System.IO;

namespace XTMF2.UnitTests.Modules;

[Module(Name = "Path Validation Module", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
    Description = "Validates a worker-local path.")]
public sealed class PathValidationModule : BaseFunction<string>
{
    [Parameter(Name = "Path", Description = "Path to validate", Required = true, Index = 0)]
    public IFunction<string> Path;

    public override string Invoke() => Path?.Invoke() ?? string.Empty;

    public override bool RuntimeValidation(ref string error)
    {
        var path = Path?.Invoke();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error = $"The worker-local path '{path}' does not exist.";
            return false;
        }
        return true;
    }
}