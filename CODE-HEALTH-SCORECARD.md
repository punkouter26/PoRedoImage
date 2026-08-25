# Code Health Scorecard

Generated 2026-08-25 11:17:06 by `SCRIPTS/generate-scorecard.ps1`.

> **Partial result.** No data from: CodeScene, SonarQube - **excluded**, and the remaining weights renormalised to 100.
>
> A tool reports no data when its credentials are absent, so this reflects tooling setup, not necessarily code quality.

| Tool | Key Metrics Extracted | Tool Score (0-100) | Weight | Weighted Score |
|---|---|---|---|---|
| **CodeScene** | UNAVAILABLE - CODESCENE_API_TOKEN not set | n/a | 0% | 0 / 0 |
| **SonarQube** | UNAVAILABLE - SONAR_TOKEN not set | n/a | 0% | 0 / 0 |
| **NetArchTest** | Total rules: 27; Passed: 27; Failed: 0; Pass rate: 100% | 100 | 100% | 100 / 100 |
| **FINAL SCORE** | **Overall Codebase Quality Rating** | **100** | **100%** | **100 / 100 (Grade A)** |

## Weighting

`Final = (CodeScene Health x 3.5) + (Sonar Score x 0.35) + (NetArchTest Pass Rate x 0.30)`

Equivalently: each tool normalised to 0-100, then weighted 35 / 35 / 30.

| Grade | Score |
|---|---|
| A | 90-100 |
| B | 80-89 |
| C | 70-79 |
| D | 60-69 |
| F | below 60 |

