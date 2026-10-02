
[![CI](https://github.com/TravelModellingGroup/XTMF2/actions/workflows/blank.yml/badge.svg)](https://github.com/TravelModellingGroup/XTMF2/actions/workflows/blank.yml)

# XTMF2
The eXtensible Travel Modelling Framework 2

This repository contains the core DLL for operating with and a desktop user interface a desktop user interface.

The modules curated by TMG can be found in different repositories.
* [TMG-Framework](https://github.com/TravelModellingGroup/TMG-Framework) contains
the core modules for building travel demand models.

* [TMG.Tasha2](https://github.com/TravelModellingGroup/TMG.Tasha2) contains the modules for
running TASHA (included in GTAModel V4) and TASHA2 (to be included in GTAModel V5).

* [TMG.EMME](https://github.com/TravelModellingGroup/TMG.EMME) contains the modules
for interacting with INRO's EMME software.  Additionally it contains TMG's TMGToolbox2 for EMME.

* [TMG.Visum](https://github.com/TravelModellingGroup/TMG.Visum) contains the modules for interacting with PTV Groups' VISUM from XTMF2.

## Building XTMF2

### Requirements

1. DotNet Core 10.0+ SDK

### Clone the XTMF2 repository

> git clone https://github.com/TravelModellingGroup/XTMF2.git

### Compile from command line

> dotnet build -c Release

> dotnet test -c Release

### Running from the command line

> dotnet run -c Release --project src/XTMF2.GUI/XTMF2.GUI.csproj

## Remote RunServer

Local RunServer execution requires no setup. XTMF2 creates and connects to its local RunServer automatically.

Remote TCP RunServers require TLS and token authentication. On the machine that will host the RunServer, generate its security files once:

```bash
dotnet run -c Release --project src/XTMF2.Client/XTMF2.RunServer.csproj -- -setup-security ./runserver-security
```

This creates:

* `runserver-cert.pem`: the self-signed TLS certificate.
* `runserver-key.pem`: the certificate private key. Keep this file private.
* `runserver-token.txt`: the authentication token. Keep this file private.

The setup command prints the certificate's SHA-256 fingerprint. The server also prints the same fingerprint each time it starts; this is safe to share with GUI users and is not the authentication token. Start the remote server with the security directory:

```bash
dotnet run -c Release --project src/XTMF2.Client/XTMF2.RunServer.csproj -- -tcp 0.0.0.0 5000 -security ./runserver-security
```

In XTMF2, open **Settings**, open **RunServers**, and add a remote endpoint. Enter:

1. The remote machine's address and TCP port.
2. The contents of `runserver-token.txt` in **Token**.
3. The printed SHA-256 fingerprint in **Certificate**. Colons and spaces are accepted.

The GUI pins the server certificate to this fingerprint and authenticates with the token before creating the RunServer bus. A mismatched certificate or token is rejected. Do not expose the TCP port to untrusted networks; use firewall rules or a private network as appropriate.

## Distributed estimation

Estimation runs can evaluate candidate parameter vectors concurrently across multiple connected RunServers. Normal model runs and calibration runs continue to use one RunServer.

To start a distributed estimation run:

1. Connect the required RunServers from **Settings** > **RunServers**.
2. Open the model system and choose **Run Estimation**.
3. Select two or more RunServers in the run configuration dialog and choose the orchestrator RunServer. The other selected servers are workers.
4. If the model has an estimation `InputDirectory`, review its worker-local value for each selected worker. These values are saved in the GUI settings by model node ID and are reused on later estimation runs.

The orchestrator RunServer owns the estimation algorithm and connects directly to the workers. Each worker constructs and validates its own local copy of the model. Input-directory overrides are applied only to that worker's copy; the saved model system is not modified. The orchestrator must be able to reach every worker's configured TCP endpoint.

After submission, the GUI is not required for the optimization to continue. The orchestrator persists `estimation-completion.json` in the run directory when the job finishes, including the best parameter values and completion status.

RunServers that receive path overrides must be running the current shared-estimation worker build. Runs without path overrides retain the version-1 shared-estimation protocol and remain compatible with workers that support the original distributed-estimation protocol.

## Main Branches

There are 4 major branches for XTMF 2 intended for different purposes:
* [dev](https://github.com/TravelModellingGroup/XTMF2/tree/dev) contains the latest build that is
taking in all of the latest pull-requests.
* [InnerRing](https://github.com/TravelModellingGroup/XTMF2/tree/InnerRing) contains the latest
build that is stable enough for TMG to develop their software against.
* [OuterRing](https://github.com/TravelModellingGroup/XTMF2/tree/OuterRing) contains the latest
stable pre-release build.
* [master](https://github.com/TravelModellingGroup/XTMF2/tree/master) contains the latest
supported build of XTMF2.
