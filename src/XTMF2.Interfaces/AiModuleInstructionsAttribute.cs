using System;

namespace XTMF2;

/// <summary>
/// Provides concise composition guidance for AI-assisted model-system editing.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class AiModuleInstructionsAttribute : Attribute
{
    public AiModuleInstructionsAttribute(string instructions)
    {
        Instructions = instructions ?? throw new ArgumentNullException(nameof(instructions));
    }

    public string Instructions { get; }
}
