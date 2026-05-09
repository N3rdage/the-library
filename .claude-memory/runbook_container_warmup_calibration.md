---
name: WEBSITES_CONTAINER_START_TIME_LIMIT calibration for AAD-authed Linux App Service
description: Default 230s warmup probe is too tight when cold-start stacks ca-cert update + AAD handshake + EF migration + app init. Set to 600s; now in Bicep.
type: project
originSessionId: 06e95d36-5868-496f-9999-8b65f480b83c
---
Linux App Service's warmup probe gives a container 230 seconds to come up before declaring failure and entering a kill-and-restart loop. BookTracker's cold-start adds up to ~250s under realistic conditions, so the default eventually trips on a slow day. Hard-set to 600s in Bicep via `WEBSITES_CONTAINER_START_TIME_LIMIT: '600'` in `infra/modules/app-config.bicep` `commonAppSettings` (PR #192).

**Why:** Cold start stacks:
- Platform `update-ca-certificates` (5-80s — variable, sometimes very slow on regional bad days)
- Dotnet bootstrap (~15s)
- First SQL connection with AAD-only auth on Basic tier (~30-40s — Managed Identity token acquisition + AAD validation against SQL)
- EF migration applock + history check (~10s)
- App initialisation, Kestrel binding (~30-40s)

Once a container crosses 230s, the platform kills it. The fresh container starts the whole sequence again, which makes the next attempt slower because cert updates and AAD calls happen across more concurrent containers. The restart loop reinforces itself.

**How to apply:** Already permanent in Bicep. If the value comes into question:
- The setting must be on both prod and staging slots; it's environment-shape, not slot-sticky.
- During the May 2026 incident, Drew set it manually via `az webapp config appsettings set`. Because it's not slot-sticky, a swap *moves it with the bits* — meaning a manual fix can be lost on swap. Putting it in Bicep prevents that, but if anyone reaches for the manual command again, remember that setting it on both slots before swapping is the safe shape.
- Max is 1800s. 600s gives generous headroom; reach higher only if cold-start measurements legitimately exceed 5 minutes.