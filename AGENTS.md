# Modules Agent Guide

Read this before changing the Modules project.

## Role

`Modules/` contains the shared extension contracts and concrete core modules used by Backend and Node.

Modules are split into two halves:

- backend modules describe a core to Backend and Frontend;
- node modules execute core-specific local behavior on Node.

The stable bridge between the halves is `CoreId` plus aliases.

## Projects

- `CitadelX.Backend.Abstractions/` - backend-side module contracts.
- `CitadelX.Node.Abstractions/` - node-side module contracts and shared node models.
- `SingboxModule/` - backend metadata for `Singbox`.
- `SingboxExtendedModule/` - backend metadata for `SingboxExtended`.
- `SingboxNodeModule/` - node runtime/config/user implementation for sing-box.
- `SingboxExtendedNodeModule/` - independent node module for `SingboxExtended`; it has its own duplicated runtime engine.
- `WireGuardModule/` / `WireGuardNodeModule/` - Linux `wg-quick` WireGuard backend/node plugin pair.
- `AmneziaWGModule/` / `AmneziaWGNodeModule/` - Linux `awg-quick` AmneziaWG backend/node plugin pair.
- `TrustTunnelModule/` / `TrustTunnelNodeModule/` - AdGuard TrustTunnel endpoint backend/node plugin pair.
- `tools/build-modules.ps1` - builds and packages module DLLs.

## Coding Rules

- Keep all projects on `net8.0`.
- Keep abstraction packages minimal and dependency-light.
- Do not reference Backend from node abstractions or node modules.
- Do not reference Node from backend abstractions or backend modules.
- Keep `CoreId` values stable; they are persisted in Backend `ServerEntity.Type` and Node `ServerLaunchProfile.CoreId`.
- Keep aliases case-insensitive and include common external names.
- When adding module capabilities, update both docs and Frontend assumptions.
- Do not make Frontend depend on node module implementation details.
- Prefer generic schema/catalog metadata for UI configuration rather than hardcoding every core in Vue.

## Current Core IDs

- `Singbox`
- `SingboxExtended`
- `WireGuard`
- `AmneziaWG`
- `TrustTunnel`

## Build Commands

From workspace root:

```powershell
dotnet build .\Modules\CitadelX.Backend.Abstractions\CitadelX.Backend.Abstractions.csproj
dotnet build .\Modules\CitadelX.Node.Abstractions\CitadelX.Node.Abstractions.csproj
dotnet build .\Modules\SingboxModule\CitadelX.SingboxModule.csproj
dotnet build .\Modules\SingboxNodeModule\SingboxNodeModule.csproj
dotnet build .\Modules\SingboxExtendedModule\CitadelX.SingboxExtendedModule.csproj
dotnet build .\Modules\SingboxExtendedNodeModule\SingboxExtendedNodeModule.csproj
dotnet build .\Modules\WireGuardModule\CitadelX.WireGuardModule.csproj
dotnet build .\Modules\WireGuardNodeModule\WireGuardNodeModule.csproj
dotnet build .\Modules\AmneziaWGModule\CitadelX.AmneziaWGModule.csproj
dotnet build .\Modules\AmneziaWGNodeModule\AmneziaWGNodeModule.csproj
dotnet build .\Modules\TrustTunnelModule\CitadelX.TrustTunnelModule.csproj
dotnet build .\Modules\TrustTunnelNodeModule\TrustTunnelNodeModule.csproj
```

Packaging:

```powershell
.\Modules\tools\build-modules.ps1
```

Packaging script note: the script now prefers this workspace's `Backend` folder and falls back to the old `CitadelX.Backend` folder name.

## Verification

For backend module changes:

```powershell
dotnet build .\Modules\SingboxModule\CitadelX.SingboxModule.csproj
dotnet build .\Backend\CitadelX.Backend.csproj
```

For node module changes:

```powershell
dotnet build .\Modules\SingboxNodeModule\SingboxNodeModule.csproj
dotnet build .\Node\CitadelX.Node.csproj
```

For contract changes, build all consumers:

```powershell
dotnet build .\Backend\CitadelX.Backend.csproj
dotnet build .\Node\CitadelX.Node.csproj
cd Frontend
npm run build
```

## Sharp Edges

- Modules are loaded as plugins only (no DI/ProjectReference). Build a module with a full `dotnet build` of its project so the `CopyToDropFolder` target copies the DLL into repo-root `Drop/modules/`.
- After editing a shared contract assembly, rebuild the host (Backend/Node) too — a stale host bin + fresh plugin throws `MissingMethodException` at runtime.
- `SingboxModule.SimpleSetupSchema` exposes a real schema/defaults; the Frontend renders it generically via `SchemaForm.vue` (no core-specific form).
- `SingboxExtendedModule.SimpleSetupSchema` exposes a minimal runnable mixed-inbound/direct-outbound setup; keep it independent from the base sing-box module.
- WireGuard defaults are meant to work on a clean Linux node: artifact filename `wg0.conf` is authoritative, server-side config omits `DNS`, client DNS is carried as `# CitadelX-ClientDNS`, and default `PostUp`/`PostDown` enable IPv4 forwarding plus NAT for `10.77.0.0/24`.
- AmneziaWG is a separate core id, not a WireGuard mode. It controls `awg`/`awg-quick`, stores configs under `data/amneziawg`, emits `wg://` URIs with `enable_amnezia=true`, and returns full `.conf` subscription files that include Amnezia interface keys. Its `SystemPackageInstall` declares apt/dnf pre-install repository steps plus package-manager-specific package names.
- `CoreInstaller` is generic (D4): the binary name, package names, and pre-install repository steps come from module `Install` metadata, not hardcoded core names.
- `SingboxConfigPatcher` chooses user key based on inbound type: `username` for `mixed`/`socks`/`http`, otherwise `name`.
- Disabling sing-box users removes them from config and stores their previous JSON in `DisabledUserStore`.
- `SingboxExtendedNodeModule` has its **own** duplicated engine (does not use `SingboxNodeServer`) — SingboxExtended is a fully independent fork; apply fixes to both copies when needed.
- Subscription builders (`SingboxSubscriptionBuilder` + the independent `SingboxExtendedSubscriptionBuilder`) produce vless/vmess/trojan/shadowsocks/hysteria2/tuic links where credentials/config allow it, plus `socks5://`/`http://` links for `mixed`/`socks`/`http` inbounds, including unauthenticated proxy inbounds. Per-user creds come from `ServerUserEntity.UserTemplateJson`; full config/file payloads are server-scoped at the Backend endpoint.
