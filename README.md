# JoyVeyor — Conveyor Logistics System

**Production development plan & technical roadmap for a conveyor-belt logistics network in a building/factory game.**

Status: Planning → Phase 1 · Engine: TBD (core designed engine-agnostic C++) · Simulation: deterministic fixed-tick

---

## 1. System Boundaries & Scope

### In Scope
- Grid-aligned **and** splined conveyor belt pathfinding and connection logic
- Item transport mechanics (throughput rate, lane capacity, movement speed, state machine)
- Splitting and Merging nodes (1:N, N:1 routing, ratio management, round-robin, priority queuing)
- Visual representation and rendering optimization (GPU instancing for items and belts)
- Save/Load serialization format for grid nodes, connections, and item states
- Bottleneck, backpressure, and deadlock detection/handling algorithms

### Out of Scope (deferred to future system modules)
- 3D fluid dynamics or pressurized pipe logistics
- NPC/Worker interacting directly with moving belts
- Dynamic physics collisions between items on belts (items follow **deterministic** spline/node paths)
- Economy, crafting recipes, and consumer machine UI (strictly logistics transport)
- Wireless, portal, or long-distance teleporter transport mechanics

---

## 2. System Architecture

### 2.1 Core Data Model

Design principles:
- **Structure-of-Arrays (SoA) pools** for hot data (items): cache-friendly, deterministic iteration, trivially parallelizable later.
- **Dense 32-bit IDs** with generation counters (handle-reuse safe) into flat pools. **No raw pointers** in any serializable structure.
- **Engine-agnostic core**: simulation is pure C++ with zero engine dependencies; the renderer consumes read-only snapshots each tick.

```cpp
// ---- Item (hot path, SoA pool) ----
struct ItemInstance {
    uint32_t id;
    uint16_t itemType;      // index into ItemCatalog (size, render type)
    uint32_t beltId;        // INVALID_BELT while queued at a node
    float    arcPos;        // arc-length position along current path (world units)
    uint8_t  state;         // Moving | HeldBackpressure | QueuedAtNode | Delivering
    uint32_t routeHint;     // intended downstream belt/node (assigned by splitter)
};

// ---- Belt ----
struct Belt {
    uint32_t id;
    PathKind kind;          // GridStraight | GridElbow | Spline
    // Grid: cellOrigin(int32 x,y) + dir (4 cardinal) + length
    // Spline: control points (fixed array, max 8) + precomputed arc-length table
    float    length;        // precomputed total path length
    float    speed;         // world units / sec
    uint8_t  capacity;      // max simultaneous items on this belt (lane capacity)
    uint32_t inNode;        // entry node id
    uint32_t outNode;       // exit node id
    uint32_t nextBelt;      // downstream belt id (INVALID = line end)
    uint32_t networkId;     // connected-component id
    uint16_t occupancy;     // current item count
    // Occupancy kept as arcPos-sorted array; items move uniformly so
    // relative order is preserved — no per-tick resort needed.
};

// ---- Node ----
struct Node {
    uint32_t id;
    NodeKind kind;          // Source | Sink | Splitter | Merger | Junction
    GridCell cell;
    // Source / Sink
    uint16_t storageCapacity;
    uint16_t storageCount;
    uint16_t spawnTimer;    // Source: fixed-tick countdown to next spawn
    // Splitter (1:N, N <= 4)
    uint8_t  outputCount;
    uint32_t outputs[4];    // downstream belt/node ids
    uint8_t  routingMode;   // RoundRobin | Ratio | Priority
    uint8_t  ratios[4];     // normalized weights for Ratio mode
    uint32_t rrCursor;      // round-robin cursor
    // Merger (N:1, N <= 4)
    uint8_t  inputCount;
    uint32_t inputs[4];
    // Bounded FIFO for items waiting at the node (backpressure queue)
    FixedQueue<uint32_t, 64> queue;
};

// ---- Network (connected component) ----
struct Network {
    uint32_t id;
    uint32_t beltCount, nodeCount, itemCount;
    bool     hasCycle;      // incremental cycle detection on connect/disconnect
    uint32_t cycleBeltIds[16];
    uint32_t jamTicks;      // consecutive zero-progress ticks (deadlock detector)
};
```

**Invariants (each enforced by unit tests):**
1. **Conservation** — `totalItems == spawned - delivered`; items are never destroyed.
2. **No overlap** — on any belt, `arcPos[i+1] - arcPos[i] >= minGap`.
3. **Capacity** — `belt.occupancy <= belt.capacity` and `node.queue.size <= queue.capacity`.
4. **Determinism** — same save + same inputs ⇒ bit-identical state at tick N (fixed iteration order, no unordered containers in the sim path).

### 2.2 Tick Management Strategy

- **Fixed simulation tick: 30 Hz (33.33 ms)**, decoupled from rendering via a time accumulator. The renderer interpolates item positions between tick N and N+1 for smooth 60+ FPS visuals.
- **Sub-stepping**: if `speed * dt > maxStep` (where `maxStep = 0.5 × min cell size`), the tick is subdivided into k sub-steps so no item ever moves more than half a cell per step — prevents tunneling through nodes/junctions.
- **Deterministic per-tick order** (fixed, always the same):
  1. Sinks consume (deliver queued items)
  2. Nodes dispatch (splitters assign `routeHint`, mergers arbitrate)
  3. Belts move items **in reverse topological order** (downstream first) so backpressure resolves in a single pass
  4. Sources spawn
  5. Network bookkeeping (occupancy counters, jam counters)
- **Spatial batching**: world partitioned into **16×16 cell chunks**. Each chunk carries an `active` flag (moving items, queued items, or a source that can spawn). Inactive chunks are skipped entirely.
- **Threading**: MVP is **single-threaded** (determinism + simplicity). Later option: job-system parallelism over chunks in dependency waves; determinism preserved by fixed chunk order within a wave.

### 2.3 Rendering Strategy (60+ FPS @ 1,000+ items)

- **Items — GPU instancing**: one mesh per item render type (MVP: 1–2 types). Per-instance data = transform (3×4 floats) + color. **One draw call per (item type, visible chunk)** → 1,000 items across ~16 chunks ≈ ≤ 32 draw calls.
  - CPU cost is transform upload: write straight from the SoA item pool into a dynamic instance buffer (ring buffer, dynamic-usage GPU buffer), only for visible chunks.
- **Belts — instanced segments**: one instanced mesh per belt style per chunk; belt motion is a **scrolling UV offset in the shader** (time-based, zero per-instance data).
- **Culling**: chunk-level frustum culling; invisible chunks skip both instance-buffer uploads and belt draw calls.
- **Budgets**: sim ≤ 8 ms/frame · render CPU ≤ 4 ms/frame · ≤ 50 draw calls for the visible network · stable 60 FPS @ 1,000 items (stretch: 10,000).

---

## 3. Edge Cases & Performance Handling

### 3.1 Loop Cycles (circular conveyor deadlocks)
- **Static detection**: on every connection change (place/remove/rotate), incrementally recompute the affected connected component and run iterative DFS cycle detection (O(V+E)) on the belt graph. Closed loop with no sink ⇒ **placement-time warning**.
- **Runtime detection**: per network, `jamTicks` increments when zero items moved during a tick while `itemCount > 0`; at threshold (e.g. 120 ticks ≈ 4 s) the network is flagged **deadlocked** → UI highlight + optional pause.
- **Handling (deterministic only — never random item removal)**: highlight the loop, pause the network, editor "jam relief" tool for manual item removal. Prevention at placement time is the primary strategy.

### 3.2 Backpressure (item reaches a full belt)
- **Entry gating**: an item at the end of belt A may advance onto belt B only if (a) `B.occupancy < B.capacity` **and** (b) an entry gap exists at B's start (`firstItem.arcPos >= minGap`).
- **Intra-belt gating**: an item may advance only if `arcPos + step <= nextItem.arcPos - minGap` (or `<= length` when it is the last item and the downstream entry is free).
- **Result**: a full belt halts upstream items **sequentially** at `minGap` spacing — no overlap, no disappearance, no teleporting. Held items enter `HeldBackpressure` and resume FIFO the moment a gap opens (no overtaking).
- **Node queues**: bounded FIFO at splitters/mergers; when a queue is full, the input belt's entry gate closes → backpressure propagates upstream automatically.

### 3.3 Chunking / Unloading (off-screen, far-away belts)
- **Simulation**: chunks with no items and no queued items **sleep** (skipped). Sim data always resident (small struct memory) — no sim unloading in MVP.
- **Rendering**: only visible chunks upload instance data and issue draw calls; distant belt meshes are culled.
- **Post-MVP option**: GPU-resource unloading for very distant chunks. Sim state is **never** unloaded (save integrity).

---

## 4. Execution Roadmap

| Phase | Focus | Exit Criteria |
|---|---|---|
| **1 — Math Model & Tick Logic** | Headless sim: grid paths, arc-length parameterization, fixed-tick loop, item state machine, backpressure | Unit tests: travel distance = speed × time exactly; deterministic backpressure halt; item conservation holds |
| **2 — Placement & Grid Mechanics** | Snap-to-grid placement (4 cardinal), connection validation, Source/Sink, Splitter (1:N round-robin), Merger (2:1) | Full MVP placement flow in editor: chest → belt → chest, with split & merge |
| **3 — Visuals & Item Instancing** | GPU instancing for items & belts, belt animation, chunk culling, LOD | 1,000 items @ stable 60 FPS; ≤ 50 draw calls |
| **4 — Serialization & Load Testing** | Save/Load format, round-trip tests, benchmark harness, profiling, soak tests | Save/Load bit-exact; 10-min soak @ 1,000 items with no drift/leaks |

---

## 5. MVP Checkpoint (Milestone 1)

| Feature | MVP Requirement Specification |
|---|---|
| Grid Placement | Snap-to-grid placement of straight conveyor segments (4 cardinal directions) |
| Basic Connectivity | Ability to link Node A (Source/Chest) → Belt → Node B (Sink/Storage) |
| Deterministic Travel | Items move at a fixed velocity along a single lane without clipping or stacking errors |
| Backpressure Logic | When Node B is full, items halt sequentially along the belt without disappearing or overlapping |
| Basic Split/Merge | A 1-to-2 Splitter (Round-Robin) and a 2-to-1 Merger operating on fixed ticks |
| Performance Target | Stable 60 FPS with 1,000 active items moving simultaneously in a test environment |

**Acceptance checklist** (tracked in the `[MVP]` issue):
- [ ] Straight belt placeable on grid in all 4 cardinal directions; invalid connections rejected
- [ ] Source chest spawns at fixed rate; items travel to sink chest at fixed velocity
- [ ] 1,000 items: zero overlap, zero clipping, zero item loss (conservation counter)
- [ ] Sink filled → whole line halts sequentially at `minGap`; resumes on sink drain
- [ ] 1→2 splitter alternates round-robin on fixed ticks; 2→1 merger merges without overlap
- [ ] 60 FPS sustained in benchmark harness with 1,000 active items

---

## 6. Open Decisions
1. **Engine**: Unity vs Unreal vs custom C++ — core sim is engine-agnostic C++; renderer binds to the chosen engine. *(Decision needed before Phase 3.)*
2. **Item model**: single item type in MVP vs small catalog (affects instancing layout).
3. **Save format**: binary (fast, compact) vs JSON (debuggable) — recommendation: binary + JSON debug dumper.

## 7. Planned Repository Structure
```
joyveyor/
├── core/          # engine-agnostic simulation (C++): paths, tick, items, nodes, networks
├── render/        # instanced rendering layer (engine binding)
├── editor/        # placement tools, grid UI
├── save/          # serialization
├── tests/         # unit + benchmark harness
└── docs/          # this plan, ADRs
```

