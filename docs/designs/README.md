# Design Specifications

This directory contains technical design specifications conforming to the
[Elephant-Goldfish Model (EGM)](https://egm.dev) specification standards.

Specifications in this directory define architectural decisions, component contracts,
and implementation plans before code is merged. They are audited against the active
codebase using `/egm:egm-drift-report`.

## Specification Catalog

| Document | Author | Status | Description |
| :------- | :----- | :----- | :---------- |
| [consolidated-modernization-and-security-hardening.md](consolidated-modernization-and-security-hardening.md) | Aaron Metz (`@acmetz`) | Approved / Merged (v1.0.0) | Complete design specification consolidating regression recovery, .NET 8/9/10 multi-targeting, `CancellationToken` support, `Microsoft.Extensions.Http.Resilience`, atomic `PriorityQueue` cache eviction, URI security validation, and bearer/secret masking. |

## EGM Workflow

New features and architectural changes follow the EGM lifecycle:

1. **Brainstorm (`/egm:egm-brainstorm`)**: Explore requirements, evaluate trade-offs, and stress-test assumptions with a fresh Goldfish review swarm.
2. **Draft (`/egm:egm-draft`)**: Construct a technical design document section-by-section adhering to the EGM Feature Specification Standard.
3. **Review (`/egm:egm-review`)**: Run isolated Goldfish persona reviews (structural, style, design, codebase details, implementation readiness) before opening a PR.
4. **Implement**: Implement according to the approved plan.
5. **Drift Detection (`/egm:egm-drift-report`)**: Regularly verify that active code matches checked-in specifications.
