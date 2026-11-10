# Domain Model & Interface Contracts: Phase 2

This document defines the domain models, value objects, static knowledge base, and application boundary contracts for the PCB defect detection system. It is structured into three focused, purpose-built diagrams following the **Single Responsibility Principle** and `api-and-interface-design`.

---

## 1. Core Inspection Aggregate

The Inspection Aggregate encapsulates the execution and capture lifecycle of a PCB inspection event.

```mermaid
classDiagram
    direction TB

    class InspectionRecord {
        <<aggregate root>>
        +Guid Id
        +string Name
        +DateTime CreatedAtUtc
        +DateTime UpdatedAtUtc
        +DetectionMode Mode
        +int DefectCount
        +InspectionStatus Status
        +string ImagePath
        +IReadOnlyList~DetectedDefect~ Defects
        +WithUpdatedName(string name) InspectionRecord
    }

    class DetectedDefect {
        <<entity>>
        +Guid Id
        +Guid InspectionId
        +string ClassCode
        +float Confidence
        +BoundingBox BoundingBox
    }

    class BoundingBox {
        <<value object>>
        +float X
        +float Y
        +float Width
        +float Height
        +Validate() bool
        +Clamp() BoundingBox
        +FromPixels(int x, int y, int w, int h, int imgW, int imgH)$ BoundingBox
        +FromNormalized(float x, float y, float w, float h)$ BoundingBox
    }

    class InspectionStatus {
        <<enumeration>>
        Pass
        Fail
    }

    class DetectionMode {
        <<enumeration>>
        SingleFile
        BatchFolder
        LiveCamera
    }

    InspectionRecord "1" *-- "0..*" DetectedDefect : contains
    InspectionRecord --> InspectionStatus : derives
    InspectionRecord --> DetectionMode : conducted via
    DetectedDefect *-- "1" BoundingBox : located by
```

### Invariants & Responsibilities
- **`InspectionRecord` (Aggregate Root):** Maintains aggregate integrity. `Status` is an immutable derived property (`DefectCount == 0 ? Pass : Fail`), preventing status desynchronization bugs.
- **`DetectedDefect` (Entity):** Stores per-detection bounding coordinates and model confidence. Diagnostic properties (`Explanation`, `Severity`, `RootCause`, `Impact`, `Action`) are delegated dynamically to the static `DefectKnowledgeBase`.
- **`BoundingBox` (Value Object):** Enforces spatial validation (`[0.0, 1.0]`) and provides factory methods for pixel-to-normalized coordinate conversion.

---

## 2. Defect Classification & Static Knowledge Base

Encapsulates the 9 defect classes and static diagnostic guidance (`DEFECT_KB`), providing instant, type-safe lookups without database text duplication.

```mermaid
classDiagram
    direction TB

    class DefectKnowledgeBase {
        <<static source>>
        +IReadOnlyDictionary~DefectClass, DefectClassInfo~ All$
        +GetInfo(DefectClass defectClass)$ DefectClassInfo
        +GetByCode(string code)$ DefectClassInfo
        +GetByIndex(int classId)$ DefectClassInfo
    }

    class DefectClassInfo {
        <<value object>>
        +DefectClass Class
        +int ClassId
        +string Code
        +string DisplayName
        +string Explanation
        +DefectSeverity Severity
        +string RootCause
        +string Impact
        +string Action
    }

    class DefectClass {
        <<enumeration>>
        Short = 0
        Spur = 1
        SpuriousCopper = 2
        Open = 3
        MouseBite = 4
        HoleBreakout = 5
        ConductorScratch = 6
        ConductorForeignObject = 7
        BaseMaterialForeignObject = 8
    }

    class DefectSeverity {
        <<enumeration>>
        LowMedium
        Medium
        MediumHigh
        High
        Critical
    }

    DefectKnowledgeBase o-- "9" DefectClassInfo : registers
    DefectClassInfo --> DefectClass : identifies
    DefectClassInfo --> DefectSeverity : defines
```

### Knowledge Base Properties
- **`DefectClass` Enum:** Integer values `0` through `8` matching model tensor output indices (`CLASS_NAMES`).
- **`DefectSeverity` Enum:** Standardized severity grading (`Critical`, `High`, `Medium-High`, `Medium`, `Low-Medium`).
- **`DefectKnowledgeBase`:** Centralized, immutable reference source mapping each class code to full diagnostic guidance.

---

## 3. Application Boundaries & Repository Contracts

Separates external input commands and paginated query results from internal domain aggregates per `api-and-interface-design`.

```mermaid
classDiagram
    direction TB

    class CreateInspectionCommand {
        <<boundary input>>
        +string Name
        +DateTime CreatedAtUtc
        +DetectionMode Mode
        +string ImagePath
        +IReadOnlyList~CreateDefectInput~ Defects
    }

    class CreateDefectInput {
        <<boundary input>>
        +string ClassCode
        +float Confidence
        +BoundingBox BoundingBox
    }

    class InspectionQueryFilter {
        <<boundary input>>
        +DateTime? StartDateUtc
        +DateTime? EndDateUtc
        +InspectionStatus? Status
        +DetectionMode? Mode
        +int PageNumber
        +int PageSize
        +string? SortBy
        +bool SortDescending
    }

    class PagedResult~T~ {
        <<boundary output>>
        +IReadOnlyList~T~ Items
        +int PageNumber
        +int PageSize
        +int TotalItems
        +int TotalPages
        +bool HasNextPage
        +bool HasPreviousPage
    }

    class IInspectionRepository {
        <<interface>>
        +InsertAsync(InspectionRecord record, CancellationToken ct) Task~Guid~
        +GetByIdAsync(Guid id, CancellationToken ct) Task~InspectionRecord?~
        +GetPagedAsync(InspectionQueryFilter filter, CancellationToken ct) Task~PagedResult~InspectionRecord~~
        +UpdateNameAsync(Guid id, string name, CancellationToken ct) Task~bool~
        +DeleteAsync(Guid id, CancellationToken ct) Task~bool~
    }

    class IDefectRepository {
        <<interface>>
        +GetByInspectionIdAsync(Guid inspectionId, CancellationToken ct) Task~IReadOnlyList~DetectedDefect~~
        +GetClassDistributionAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct) Task~IReadOnlyDictionary~string, int~~
    }

    CreateInspectionCommand "1" *-- "0..*" CreateDefectInput : carries
    IInspectionRepository ..> InspectionQueryFilter : filters by
    IInspectionRepository ..> PagedResult~T~ : returns
```

### Boundary & Interface Rules
- **Input Isolation:** Callers provide operational values (`CreateInspectionCommand`). Database-generated keys and derived statuses are managed by aggregate roots.
- **Bounded Pagination:** `InspectionQueryFilter` bounds page requests (`PageSize` default 20, max 100) to protect database memory and query latency.
- **Contract-First Persistence:** Repositories expose explicit async contracts returning nullable entities (`GetByIdAsync`) or structured paged results (`GetPagedAsync`).
