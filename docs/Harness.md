# Harness

Created with `./tools/dev.ps1 new-mod`. Build / deploy / run:

```powershell
./tools/dev.ps1 build Harness
./tools/dev.ps1 deploy Harness
./tools/dev.ps1 launch
./tools/dev.ps1 log --source Harness
```

Anything placed in `assets/` is copied next to the DLL by `deploy` and `package`.
Acceptance markers: log lines starting with `[SFM-ACCEPT]` are what `wait-log` and the agent look for.
