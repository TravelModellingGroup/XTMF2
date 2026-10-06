# XTMF2.RunServer.Updater

`XTMF2.RunServer.Updater` is a separate .NET 10 executable used by RunServer when activating an uploaded module deployment. RunServer starts the updater and exits, allowing the updater to replace files without holding the server's runtime assembly open.

## Build

Run these commands from the repository root:

```powershell
dotnet build .\src\XTMF2.RunServer.Updater\XTMF2.RunServer.Updater.csproj --configuration Release
dotnet build .\XTMF2.sln --configuration Release
```

The updater project writes its output to the shared `XTMF2-Dev` directory, alongside RunServer. Building the RunServer project or the full solution also builds the updater. Keep the updater executable and its accompanying .NET runtime files beside RunServer; RunServer launches it automatically, so it is not normally run manually.

## What it does

1. Waits up to 30 seconds for the previous RunServer process to exit.
2. Extracts and validates the staged deployment archive, then backs up and replaces `XTMF2.dll` and the `Modules` directory.
3. Starts RunServer again with its original command-line arguments and waits up to 30 seconds for its readiness signal.
4. If activation fails, stops the deployed server, restores the backups, and starts the previous RunServer version.

Temporary deployment and staging files are removed after successful activation or rollback. If the updater cannot stop a failed deployed server or complete rollback, it retains the deployment backup for recovery.
