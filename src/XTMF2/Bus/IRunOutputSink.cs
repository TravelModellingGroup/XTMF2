using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XTMF2.Bus;

internal interface IRunOutputSink
{
    IReadOnlyList<string> ExtraDlls { get; }

    Task StartProcessingRequestFromRun(string id, Stream clientToRunStream);

    void ModelRunFailedValidation(string runId, string? error, string? moduleName = null, string? elementId = null);

    void ModelRunFailed(string runId, string? message, string? stackTrace, string? moduleName = null, string? elementId = null);

    void SendStatusMessage(string runId, string? message);

    void SendOptimizationResults(string runId, IReadOnlyList<(int nodeIndex, double value)> results);

    void SendIterationProgress(string runId, int iteration, double fitness,
        int fitnessTestsThisIteration, IReadOnlyList<(int nodeIndex, double value)> values);

    void ModelRunComplete(string runId);

    void SendRunArtifacts(string runId, string runDirectory);
}