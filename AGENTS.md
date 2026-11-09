# AGENTS.md — Agent Operating Guidelines

This document serves as the persistent Level 1 Rules File for AI agents working in this repository. It defines operational rules, development workflows, skill mappings, context engineering practices, and quality standards for the entire project lifecycle.

---

## Project Overview & Technical Baseline

- **Project Name:** `pcb-deftek`
- **Domain:** PCB Defect Detection & Analysis Application
- **Runtime & Framework:** .NET 10.0 Windows Desktop (`net10.0-windows`), WPF (Windows Presentation Foundation), C# 13
- **Configuration:** Nullable reference types enabled (`<Nullable>enable</Nullable>`), Implicit Usings enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
- **Machine Learning Assets:** ONNX Runtime models (e.g., `Assets/Models/best.onnx`) for real-time defect inference
- **Core Commands:**
  - Build solution: `dotnet build`
  - Run application: `dotnet run --project pcb-deftek.csproj`
  - Run tests: `dotnet test` (when test project is configured)
  - Restore dependencies: `dotnet restore`

---

## Context Engineering Hierarchy & Budget Management

Agents must deliberately curate and manage context according to the 5-level hierarchy to prevent hallucination and focus degradation:

```
┌────────────────────────────────────────────────────────┐
│  Level 1: Rules Files (AGENTS.md)                       │ ← Always active, persistent across sessions
├────────────────────────────────────────────────────────┤
│  Level 2: Spec / Architecture Docs (specs, ADRs, etc.)  │ ← Loaded per feature/session
├────────────────────────────────────────────────────────┤
│  Level 3: Relevant Source Files (code, types, tests)    │ ← Loaded per task (<2,000 lines focused)
├────────────────────────────────────────────────────────┤
│  Level 4: Error Output / Test Results                   │ ← Loaded per iteration (targeted, not raw)
├────────────────────────────────────────────────────────┤
│  Level 5: Conversation History                          │ ← Proactively managed, compacted
└────────────────────────────────────────────────────────┘
```

### Context Budgeting & Compaction
- **What to cut first:** Past failed attempts and resolved error logs (keep the conclusion, drop the exploration); verbose directory/tool listings; obsolete intermediate code drafts.
- **What to protect:** The original task definition, active constraints, current failing test/compiler error, and the exact files actively being edited.
- **Ordering for recency:** Position task-critical material (active error, current diff, immediate next step) at the end of context near the generation point to avoid lost-in-the-middle degradation.
- **Restartable session boundaries:** When ending or handing off a session, persist:
  1. Accepted scope and decisions in specification/plan files.
  2. Current task status and next pending action.
  3. Working-tree state and files changed.
  4. Exact verification commands and test outcomes.
  5. Unresolved questions, risks, and required approvals.

### Confusion Management
- **Conflicting context:** If a spec contradicts existing codebase patterns, do not silently guess or pick one. Formulate clear options and ask the user before proceeding.
- **Incomplete requirements:** If requirements do not cover an edge case, inspect existing code for precedent. If no precedent exists, stop and ask the user rather than inventing requirements.
- **Lightweight inline plan:** For multi-step tasks, state the planned steps briefly before execution to ensure alignment.

---

## 1. Documentation and Specification

When managing documentation, project specifications, and architectural artifacts, invoke and follow the designated skills:

- **Persistent Configuration Files:** When creating or modifying persistent configuration files (e.g., `AGENTS.md`), use the `context-engineering` skill.
- **High-Level Specifications:** When creating or modifying high-level specification files (`mission.md`, `tech-stack.md`, `roadmap.md`, and `AGENTS.md`), use the `spec-driven-development` skill.
- **Feature Development Specifications:** When creating or modifying feature development specification files (`plan.md`, `requirements.md`, and `validation.md`), use the `planning-and-task-breakdown` skill.
- **Documentation and ADRs:** When writing documentation or Architecture Decision Records (ADRs), use the `documentation-and-adrs` skill.

---

## 2. Dependency and Documentation Verification

Ensure all libraries, packages, and frameworks are safe, compatible, and verified against official sources:

- **Security & Safety Inspection:** Before installing or adding any dependency to the project, use the Socket CLI (CLI command: `socket`) to inspect it and determine whether it is safe to install.
- **Official Documentation Verification:** Before using any framework or dependency, use Context7 CLI (CLI command: `ctx7`) and the `source-driven-development` skill to retrieve and verify its official documentation.
- **Grounded Implementation:** Base implementation decisions on verified, current documentation rather than assumptions, training-data guesswork, or outdated knowledge.

---

## 3. Requirements Clarification and Idea Refinement

Prevent ambiguity and misaligned development through structured questioning:

1. **Clarification:** If you are unsure about a requested feature, or need additional information to implement it correctly, use the `interview-me` skill to clarify the requirements.
2. **Refinement Offer:** After gathering the necessary information, explicitly ask the user whether they want to refine the idea further.
3. **Execution of Refinement:** If the user agrees, use the `idea-refine` skill before proceeding with implementation.
4. **No Assumptions:** Do not make significant assumptions about unclear requirements without first clarifying them.

---

## 4. Git and Development Workflow

For every new feature, bug fix, refactoring, or distinct piece of work that could modify the codebase, execute this 6-step lifecycle strictly:

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ 1. Create    │ ──> │ 2. Create    │ ──> │ 3. Implement │
│    Issue     │     │    Branch    │     │    & Test    │
└──────────────┘     └──────────────┘     └──────────────┘
                                                 │
                                                 ▼
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ 6. Delete    │ <── │ 5. Merge PR  │ <── │ 4. Create PR │
│    Branch    │     │    & Close   │     │    Linked    │
└──────────────┘     └──────────────┘     └──────────────┘
```

1. **Create an issue** describing the intended work and scope.
2. **Create a dedicated Git branch** for that issue.
3. **Implement the changes** and perform all necessary tests and verifications.
4. **Create a Pull Request (PR)** linked directly to the corresponding issue.
5. **Review, approval, and merge:** After successful review, approval, and merge, close the issue.
6. **Delete the completed branch** immediately after merging.

- **Conventions & Versioning:** Use the `git-workflow-and-versioning` skill for all Git operations, commit message conventions, and versioning.
- **Atomic Work:** Follow this workflow consistently for every qualifying task, rather than grouping unrelated changes into a single issue or branch.

---

## 5. Development Skills

Use the appropriate skill whenever performing the corresponding type of work:

| Task | Required Skill |
|---|---|
| Writing tests | `test-driven-development` |
| Building or modifying UI | `frontend-ui-engineering` |
| Implementing application logic | `api-and-interface-design` |
| Debugging or fixing errors | `debugging-and-error-recovery` |
| Writing CI/CD pipelines and automation | `ci-cd-and-automation` |
| Handling migrations or deprecations | `deprecation-and-migration` |
| Writing documentation and ADRs | `documentation-and-adrs` |
| Implementing logging, monitoring, or observability | `observability-and-instrumentation` |

### High-Stakes Application Logic
- If the application logic involves high-stakes functionality (e.g., safety-critical defect classification, irreversible hardware actions, data integrity, security validation), **suggest using the `doubt-driven-development` skill**.
- **Do not silently skip** this recommendation when the functionality has significant consequences if implemented incorrectly.

---

## 6. Critical Feature Development

When the user requests a critical feature, follow this mandatory 3-step sequence before writing code:

1. **Clarification:** Use the `interview-me` skill to thoroughly clarify the requirements, expectations, and potential implications.
2. **Architecture Decision Record:** Once the requirements are sufficiently clear, write an Architecture Decision Record (ADR) using the `documentation-and-adrs` skill.
3. **Establish Rationale:** Establish the architectural decisions and rationale before proceeding with implementation.

---

## 7. Feature Review and Quality Improvement

When the user asks to review an implemented feature, execute a sequential, gated review pipeline. **Each subsequent stage requires explicit user confirmation**:

```
[Feature Review Requested]
         │
         ▼
[Step 1: code-review-and-quality]
         │
         ▼
[Ask User: Proceed with code-simplification?]
    ├── Yes ──> [Step 2: code-simplification]
    └── No  ──> [Stop / Await direction]
                     │
                     ▼
            [Ask User: Proceed with performance-optimization?]
                ├── Yes ──> [Step 3: performance-optimization]
                └── No  ──> [Stop / Await direction]
                                 │
                                 ▼
                        [Ask User: Proceed with security-and-hardening?]
                            ├── Yes ──> [Step 4: security-and-hardening]
                            └── No  ──> [Stop / Await direction]
```

1. **Step 1:** Use the `code-review-and-quality` skill to review the implementation.
2. **Step 2:** After completing the review, ask the user whether they want to proceed with the `code-simplification` skill.
3. **Step 3:** After simplification, ask the user whether they want to proceed with the `performance-optimization` skill.
4. **Step 4:** Finally, ask the user whether they want to proceed with the `security-and-hardening` skill.

**Guardrail:** Ask for user confirmation before proceeding with each subsequent skill. Do not automatically execute all improvement stages without explicit approval.

---

## 8. Application Launch and Shipping

Before launching or deploying the application:

1. **Skill Invocation:** Use the `shipping-and-launch` skill.
2. **Checklist Preparation:** Prepare a comprehensive launch-readiness checklist covering all necessary requirements, operational checks, and verification steps.
3. **Presentation:** Present the checklist to the user.
4. **Go/No-Go Decision:** Provide a clear **Go/No-Go recommendation** based on the verification results.
5. **Contextual Adjustment:** Adjust the checklist according to the actual deployment target (e.g., Windows desktop installer, MSIX package, portable release) and distribution method.

**Guardrail:** Do not proceed with the launch until the user has reviewed the checklist and the Go/No-Go recommendation.

---

## 9. General Instructions

- **Mandatory Skill Activation:** Always use the relevant skill before performing its corresponding task.
- **Instruction Adherence:** Follow the skill's instructions thoroughly rather than merely referencing its name.
- **No False Claims:** Do not assume a skill has been executed just because it is mentioned in the instructions.
- **Tool Availability Transparency:** If a required skill or CLI tool (e.g., `socket`, `context7`) is unavailable, inform the user immediately instead of silently skipping the requirement or pretending it was executed.
- **Architectural Preservation:** Preserve the established project architecture, conventions, and existing decisions when implementing changes.
- **Proactive Clarification:** Ask for clarification whenever an important decision cannot be made confidently from the available project context.
