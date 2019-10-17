# XTMF2
The eXtensible Travel Modelling Framework 2

This repository contains the core DLL for operating with and using XTMF problematically.
The modules curated by TMG can be found in different repositories.
* [TMG-Framework](https://github.com/TravelModellingGroup/TMG-Framework) contains
the core modules for building travel demand models.
* [TMG.Tasha2](https://github.com/TravelModellingGroup/TMG.Tasha2) contains the modules for
running TASHA (included in GTAModel V4) and TASHA2 (to be included in GTAModel V5).
* [TMG.EMME](https://github.com/TravelModellingGroup/TMG.EMME) contains the modules
for interacting with INRO's EMME software.  Additionally it contains TMG's TMGToolbox2 for EMME.

[XTMF2.Web](https://github.com/TravelModellingGroup/XTMF2.Web) provides a web user experience for
operating XTMF2.

## Building XTMF2

### Requirements

1. DotNet Core 3.0+ SDK

### Clone the XTMF2 repository

> git clone https://github.com/TravelModellingGroup/XTMF2.git

### Compile from command line

> dotnet build

> dotnet test


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
