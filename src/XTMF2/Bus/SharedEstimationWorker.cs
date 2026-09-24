using System;
using System.Collections.Generic;

namespace XTMF2.Bus;

public interface ISharedEstimationWorker : IDisposable
{
    string WorkerId { get; }

    event EventHandler<IReadOnlyList<SharedEstimationEvaluationResult>>? ResultsReceived;

    event EventHandler? Disconnected;

    bool SendCandidates(IReadOnlyList<SharedEstimationCandidate> candidates, out string? error);
}
