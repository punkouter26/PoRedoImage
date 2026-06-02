---
project: PoRedoImage
tier: 0
type: index
last_updated: 2026-06-01
---

# PoRedoImage — Documentation Index

> Machine-Readable, Human-Glanceable documentation suite for .NET 10 / C# 14 / Zero-Waste AI.

## Dependency Tree

```mermaid
flowchart TD
    T0["📖 README.md\n(Dependency Tree)"] --> T1a["📋 PRD_Master.md\n(Source of Truth)"]
    T0 --> T1b["🏗️ Architecture_Blueprint.mmd\n(C4 L1+L2)"]
    T1a --> T2a["🔄 User_Journey_Master.mmd\n(Behavior Flow)"]
    T1a --> T2b["⚡ System_State.mmd\n(Entity Lifecycle)"]
    T1b --> T2c["🔀 Interaction_Trace.mmd\n(Sequence Diagram)"]
    T2a --> T3a["🎨 UI_Design_Tokens.md\n(Component Registry)"]
    T2b --> T3b["📝 ADR_Log.md\n(Decision Records)"]
    T2c --> T3c["📊 Data_Lineage.mmd\n(Ingress → Egress)"]

    classDef tier0 fill:#1e1e2e,stroke:#89b4fa,color:#fff
    classDef tier1 fill:#1e1e2e,stroke:#a6e3a1,color:#fff
    classDef tier2 fill:#1e1e2e,stroke:#f9e2af,color:#fff
    classDef tier3 fill:#1e1e2e,stroke:#f38ba8,color:#fff

    class T0 tier0
    class T1a,T1b tier1
    class T2a,T2b,T2c tier2
    class T3a,T3b,T3c tier3
```

## Documentation Tiers

| Tier | File | Purpose | Audience |
|------|------|---------|----------|
| **0** | [README.md](README.md) | This index — dependency graph + navigation | Everyone |
| **1** | [PRD_Master.md](PRD_Master.md) | Source of Truth — API contracts, slices, constraints | Architects, AI Agents |
| **1** | [Architecture_Blueprint.mmd](Architecture_Blueprint.mmd) | C4 L1+L2 — MSI Dev Machine vs. Azure Container Apps | Architects, DevOps |
| **2** | [User_Journey_Master.mmd](User_Journey_Master.mmd) | Identity, success path, error handling flows | Product, UX |
| **2** | [System_State.mmd](System_State.mmd) | Entity lifecycle state machines with guards | Backend Devs |
| **2** | [Interaction_Trace.mmd](Interaction_Trace.mmd) | Blazor WASM → API → Table Storage sequence | Backend Devs |
| **3** | [UI_Design_Tokens.md](UI_Design_Tokens.md) | Radzen component settings, CSS vars, layout rules | Frontend Devs |
| **3** | [ADR_Log.md](ADR_Log.md) | Architecture Decision Records — why, not just what | Architects |
| **3** | [Data_Lineage.mmd](Data_Lineage.mmd) | Data flow from ingress through transformation to UI | Data Engineers |

## Quick Start

```bash
# Convert all Mermaid diagrams to SVG
Get-ChildItem docs\*.mmd | ForEach-Object {
    mmdc -i $_.FullName -o "$($_.DirectoryName)\$($_.BaseName).svg" -t dark
}
```

## Architecture at a Glance

```
PoRedoImage = Blazor Web App (.NET 10)
  ├── Vertical Slice Features (Minimal API)
  │   ├── Auth (OIDC + Dev Cookie)
  │   ├── ImageAnalysis (CV → OpenAI → Gemini)
  │   ├── BulkGenerate (10× parallel Gemini)
  │   ├── CaptionBattle (8-persona parallel)
  │   ├── MemeTemplates (Normalized coords)
  │   └── StyleDirector (4-agent sequential)
  ├── Domain Layer (Entities + Interfaces)
  ├── Application Layer (Orchestrators + Agents)
  ├── Infrastructure Layer (Azure Services)
  └── Shared Layer (DTOs)