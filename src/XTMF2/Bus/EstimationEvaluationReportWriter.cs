using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace XTMF2.Bus;

internal sealed class EstimationEvaluationReportWriter : IDisposable
{
    private readonly StreamWriter _writer;

    public EstimationEvaluationReportWriter(
        string path,
        string algorithmName,
        IReadOnlyList<string> parameterNames)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        _writer = new StreamWriter(path, append: false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var iterationColumnName = algorithmName.Contains("genetic", StringComparison.OrdinalIgnoreCase)
            ? "Generation"
            : "Iteration";
        var headers = new List<string> { iterationColumnName, "Fitness" };
        headers.AddRange(parameterNames.Select((name, index) =>
            string.IsNullOrWhiteSpace(name) ? $"Parameter {index + 1}" : name));
        _writer.WriteLine(string.Join(",", headers.Select(EscapeCsv)));
        _writer.Flush();
    }

    public void Write(int iteration, IReadOnlyList<double> parameters, double fitness)
    {
        var values = new List<string>(parameters.Count + 2)
        {
            iteration.ToString(CultureInfo.InvariantCulture),
            fitness.ToString("R", CultureInfo.InvariantCulture)
        };
        values.AddRange(parameters.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
        _writer.WriteLine(string.Join(",", values.Select(EscapeCsv)));
        _writer.Flush();
    }

    public void Dispose() => _writer.Dispose();

    private static string EscapeCsv(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
}