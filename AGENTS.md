# TigerQuery agent guidance

## Long-running validation commands

- Run potentially quiet commands such as `dotnet docfx docs/api-docfx/docfx.json` through a yielded or otherwise monitored execution.
- Poll at intervals no longer than 30 seconds and give the user a progress update at least once per minute.
- If an interrupted command may have left a child process running, identify the exact process and command line before stopping it or retrying.
- If DocFX is idle without producing output in the sandbox, stop only the identified DocFX process and retry with normal process/network access after requesting approval.
