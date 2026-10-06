using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace XTMF2.GUI;

internal static class RemoteRunOutputPaths
{
    public static string GetLocalDirectory(string runId)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runId)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTMF2", "GUI", "RemoteRunOutputs", key);
    }
}