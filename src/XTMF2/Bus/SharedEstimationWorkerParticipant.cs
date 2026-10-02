using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace XTMF2.Bus;

public sealed class SharedEstimationWorkerParticipant
{
    private readonly RunContext _context;

    private SharedEstimationWorkerParticipant(RunContext context)
    {
        _context = context;
    }

    public static bool TryCreate(
        XTMFRuntime runtime,
        SharedEstimationRunRequest request,
        [NotNullWhen(true)] out SharedEstimationWorkerParticipant? participant,
        [NotNullWhen(false)] out RunError? error)
    {
        participant = null;
        if (!RunContext.CreateRunContext(runtime, request.RunId, request.ModelSystem,
            request.WorkingDirectory, request.StartToExecute, RunMode.Estimation, out var context,
            request.BasicParameterOverrides))
        {
            error = new RunError(RunErrorType.Validation,
                "Unable to create the shared-estimation run context.", null, string.Empty);
            return false;
        }

        if (!context.PrepareSharedEstimationWorker(out error))
            return false;

        participant = new SharedEstimationWorkerParticipant(context);
        return true;
    }

    public SharedEstimationEvaluationResult Evaluate(SharedEstimationCandidate candidate)
        => _context.EvaluateSharedEstimationCandidate(candidate);

    public IReadOnlyList<SharedEstimationEvaluationResult> Evaluate(
        IReadOnlyList<SharedEstimationCandidate> candidates)
    {
        var results = new SharedEstimationEvaluationResult[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
            results[i] = Evaluate(candidates[i]);
        return results;
    }
}
