// JoyVeyor core — level save/load (README §3.4, Phase 4).
#include "save.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

namespace jv {

namespace {

constexpr const char* kHeader = "#JVL1";

bool parseDir(int32_t v, Dir& out) {
    if (v < 0 || v > 3) return false;
    out = static_cast<Dir>(v);
    return true;
}


}  // namespace

// Parse one line into an Op. Returns false on malformed input.
bool parsePlacementLine(const char* line, PlacementOp& op) {
    char buf[128];
    std::snprintf(buf, sizeof(buf), "%s", line);
    char* save = nullptr;
    char* tok = strtok_r(buf, " \t", &save);
    if (!tok) return false;
    op.tag = tok[0];
    if (tok[1] != '\0') return false;  // tag must be a single char

    int32_t n = 0;
    int32_t vals[5] = {-1, -1, -1, -1, -1};
    for (int32_t i = 0; i < 5; ++i) {
        tok = strtok_r(nullptr, " \t", &save);
        if (!tok) continue;
        vals[i] = std::atoi(tok);
        ++n;
    }

    switch (op.tag) {
        case 'S': {  // S x y [period]  (period omitted = 15, backward compatible)
            if (n < 2 || n > 3 || vals[0] < 0 || vals[1] < 0) return false;
            op.a = vals[0]; op.b = vals[1];
            op.c = (n == 3) ? vals[2] : 15;
            if (op.c < 1) return false;
            return true;
        }
        case 'K': {  // K x y cap
            if (n != 3 || vals[0] < 0 || vals[1] < 0 || vals[2] < 1) return false;
            op.a = vals[0]; op.b = vals[1]; op.c = vals[2];
            return true;
        }
        case 'T': {  // T x y a b
            if (n != 4 || vals[0] < 0 || vals[1] < 0) return false;
            Dir da, db;
            if (!parseDir(vals[2], da) || !parseDir(vals[3], db) || da == db) return false;
            op.a = vals[0]; op.b = vals[1]; op.c = vals[2]; op.d = vals[3];
            return true;
        }
        case 'M': {  // M x y a b o
            if (n != 5 || vals[0] < 0 || vals[1] < 0) return false;
            Dir da, db, do_;
            if (!parseDir(vals[2], da) || !parseDir(vals[3], db) || !parseDir(vals[4], do_))
                return false;
            if (da == db || do_ == da || do_ == db) return false;
            op.a = vals[0]; op.b = vals[1]; op.c = vals[2]; op.d = vals[3]; op.e = vals[4];
            return true;
        }
        case 'B': {  // B x y dir len
            if (n != 4 || vals[0] < 0 || vals[1] < 0) return false;
            Dir dd;
            if (!parseDir(vals[2], dd) || vals[3] < 1) return false;
            op.a = vals[0]; op.b = vals[1]; op.c = vals[2]; op.d = vals[3];
            return true;
        }
        default:
            return false;
    }
}

std::string saveLayout(const World& w) {
    std::string s;
    s.reserve(1024);
    s += kHeader;
    s += '\n';
    // Nodes first (dense-id ascending), then belts.
    for (int32_t i = 0; i < w.nodeCount(); ++i) {
        const Node& n = w.node(i);
        char line[96];
        switch (n.kind) {
            case NodeKind::Source:
                std::snprintf(line, sizeof(line), "S %d %d %u\n", n.cell.x, n.cell.y,
                              n.spawnPeriod);
                break;
            case NodeKind::Sink:
                std::snprintf(line, sizeof(line), "K %d %d %u\n", n.cell.x, n.cell.y,
                              n.storageCapacity);
                break;
            case NodeKind::Splitter:
                std::snprintf(line, sizeof(line), "T %d %d %d %d\n", n.cell.x, n.cell.y,
                              n.outDirs[0], n.outDirs[1]);
                break;
            case NodeKind::Merger:
                std::snprintf(line, sizeof(line), "M %d %d %d %d %d\n", n.cell.x, n.cell.y,
                              n.inDirs[0], n.inDirs[1], n.outDirs[0]);
                break;
            default:
                continue;  // Junction is never created; skip defensively.
        }
        s += line;
    }
    for (int32_t i = 0; i < w.beltCount(); ++i) {
        const Belt& b = w.belt(i);
        char line[96];
        std::snprintf(line, sizeof(line), "B %d %d %d %d\n", b.cellOrigin.x, b.cellOrigin.y,
                      b.dir, b.lenCells);
        s += line;
    }
    return s;
}

bool loadLayout(World& w, const std::string& text) {
    // 1) Parse + validate every line first (atomic: world untouched on error).
    std::vector<PlacementOp> ops;
    ops.reserve(64);
    const char* p = text.c_str();
    bool first = true;
    while (const char* nl = std::strchr(p, '\n')) {
        // Line is [p, nl).
        char line[128];
        size_t len = static_cast<size_t>(nl - p);
        if (len == 0 || len >= sizeof(line)) return false;
        std::memcpy(line, p, len);
        line[len] = '\0';
        p = nl + 1;

        // Trim trailing whitespace / CR.
        while (len > 0 && (line[len - 1] == ' ' || line[len - 1] == '\t' || line[len - 1] == '\r'))
            line[--len] = '\0';
        if (len == 0) continue;  // blank line

        if (first) {
            first = false;
            if (std::strcmp(line, kHeader) != 0) return false;
            continue;
        }
        PlacementOp op;
        if (!parsePlacementLine(line, op)) return false;
        ops.push_back(op);
    }
    if (first) return false;  // no header → empty/invalid

    // 2) Apply to a fresh world.
    w.reset();
    for (const PlacementOp& op : ops) {
        uint32_t id;
        switch (op.tag) {
            case 'S':
                id = w.placeSource(GridCell{op.a, op.b}, static_cast<uint16_t>(op.c));
                break;
            case 'K':
                id = w.placeSink(GridCell{op.a, op.b}, static_cast<uint16_t>(op.c));
                break;
            case 'T':
                id = w.placeSplitter(GridCell{op.a, op.b}, static_cast<Dir>(op.c),
                                     static_cast<Dir>(op.d));
                break;
            case 'M':
                id = w.placeMerger(GridCell{op.a, op.b}, static_cast<Dir>(op.c),
                                   static_cast<Dir>(op.d), static_cast<Dir>(op.e));
                break;
            case 'B':
                id = w.placeBelt(op.a, op.b, static_cast<Dir>(op.c), op.d);
                break;
            default:
                return false;
        }
        if (id == INVALID_ID) return false;  // shouldn't happen for a valid save
    }
    return true;
}

}  // namespace jv
