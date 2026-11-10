# Domain Model & Interface Contracts: Phase 2

This document defines the core domain entities, value objects, enumerations, input/output boundary contracts, repository interfaces, and relationships for the PCB defect detection system, designed in accordance with `api-and-interface-design`.

```mermaid
classDiagram
    direction TB

    %% Enums
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

    %% Value Objects
    class BoundingBox {
        <<value object>>
        +float X
        +float Y
        +float Width
        +float Height
        +Validate() bool
        +Clamp() BoundingBox
        +FromPixels(int x, int y, int w, int h, int imgW, int imgH)$ BoundingBox
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

    class DefectKnowledgeBase {
        <<static knowledge base>>
        +IReadOnlyDictionary~DefectClass, DefectClassInfo~ All$
        +GetInfo(DefectClass defectClass)$ DefectClassInfo
        +GetByCode(string code)$ DefectClassInfo
        +GetByIndex(int classId)$ DefectClassInfo
    }

    %% Entities & Aggregates
    class DetectedDefect {
        <<entity>>
        +Guid Id
        +Guid InspectionId
        +string ClassCode
        +float Confidence
        +BoundingBox BoundingBox
        +string Explanation
        +DefectSeverity Severity
        +string RootCause
        +string Impact
        +string Action
    }

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

    %% Input / Output Boundary Contracts (API & Interface Design)
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

    %% Repository Interfaces
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

    %% Relationships
    InspectionRecord "1" *-- "0..*" DetectedDefect : contains
    InspectionRecord --> InspectionStatus : derives
    InspectionRecord --> DetectionMode : conducted via
    DetectedDefect *-- "1" BoundingBox : located by
    DetectedDefect ..> DefectKnowledgeBase : resolves metadata via
    DefectKnowledgeBase o-- "9" DefectClassInfo : registers
    DefectClassInfo --> DefectClass : identifies
    DefectClassInfo --> DefectSeverity : default severity

    CreateInspectionCommand "1" *-- "0..*" CreateDefectInput : carries
    IInspectionRepository ..> InspectionRecord : manages
    IInspectionRepository ..> InspectionQueryFilter : filters by
    IInspectionRepository ..> PagedResult~T~ : returns
    IDefectRepository ..> DetectedDefect : queries
```

---

## Architectural & Interface Design Principles (Per `api-and-interface-design`)

### 1. Contract-First & Input/Output Boundary Separation
- **Input Contracts (`CreateInspectionCommand`, `CreateDefectInput`):** Callers submit minimal inputs. System fields (`Id`, `CreatedAtUtc`, `UpdatedAtUtc`, computed `Status`, `DefectCount`) are maintained by domain aggregate roots.
- **Query Contracts (`InspectionQueryFilter`, `PagedResult<T>`):** All collection queries are explicitly paginated with bounded defaults (`PageSize` default = 20, max = 100) to protect memory and query performance.
- **Output Types (`InspectionRecord`, `DetectedDefect`):** Immutable view models exposed to presentation, dashboard, and export layers.

### 2. Normalized Entities with Static Knowledge Base Delegation
- **`DetectedDefect`:** Stores only variable per-detection data (`Id`, `InspectionId`, `ClassCode`, `Confidence`, `BoundingBox`).
- **Centralized Static Source (`DefectKnowledgeBase`):** All descriptive metadata (`Explanation`, `Severity`, `RootCause`, `Impact`, `Action`) is maintained in a centralized static source (`DEFECT_KB`), preventing text duplication across database rows while providing instant, type-safe lookup.
### 3. Immutability & Value Object Encapsulation
- **`BoundingBox`:** Implements strict validation ensuring coordinates remain normalized within `[0.0, 1.0]`. Provides factory methods (`FromPixels`, `FromNormalized`) and safe clamping utilities to guarantee valid bounding geometry at system edges.
- **`InspectionRecord`:** Status is an immutable computed property (`DefectCount == 0 ? Pass : Fail`), preventing status desynchronization bugs.
