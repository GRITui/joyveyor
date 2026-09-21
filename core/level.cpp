// JoyVeyor core — #JVLG1 level format implementation.
#include "level.h"

#include <cctype>
#include <cstdio>
#include <cstring>

namespace jv {

namespace {

constexpr const char* kLevelHeader = "#JVLG1";
constexpr const char* kLayoutHeader = "#JVL1";

// Trim trailing whitespace / CR in place; returns the trimmed length.
int32_t trimLine(char* line, int32_t len) {
    while (len > 0 && (line[len - 1] == ' ' || line[len - 1] == '\t' || line[len - 1] == '\r'))
        line[--len] = '\0';
    return len;
}

bool parseInt(const char* tok, uint32_t& out) {
    if (!tok || !*tok) return false;
    char* end = nullptr;
    long v = std::strtol(tok, &end, 10);
    if (end == tok || v < 0) return false;
    out = static_cast<uint32_t>(v);
    return true;
}

}  // namespace

bool readFile(const std::string& path, std::string& out) {
    FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return false;
    std::fseek(f, 0, SEEK_END);
    long size = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);
    if (size < 0) { std::fclose(f); return false; }
    out.resize(static_cast<size_t>(size));
    const size_t got = std::fread(&out[0], 1, out.size(), f);
    std::fclose(f);
    if (got != out.size()) { out.clear(); return false; }
    return true;
}

bool applyOps(World& w, const std::vector<PlacementOp>& ops) {
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
        if (id == INVALID_ID) return false;
    }
    return true;
}

bool applyLevel(World& w, const Level& lv) {
    w.reset();
    return applyOps(w, lv.ops);
}

bool parsePlacementOps(const std::string& text, std::vector<PlacementOp>& out) {
    out.clear();
    const char* p = text.c_str();
    bool first = true;
    while (const char* nl = std::strchr(p, '\n')) {
        char line[128];
        size_t len = static_cast<size_t>(nl - p);
        if (len >= sizeof(line)) return false;
        std::memcpy(line, p, len);
        line[len] = '\0';
        p = nl + 1;
        len = static_cast<size_t>(trimLine(line, static_cast<int32_t>(len)));
        if (len == 0) continue;

        if (first) {
            first = false;
            if (std::strcmp(line, kLayoutHeader) != 0) return false;
            continue;
        }
        PlacementOp op;
        if (!parsePlacementLine(line, op)) return false;
        out.push_back(op);
    }
    return !first;  // header seen (ops may be empty for a caller's choice)
}

bool parseLevel(const std::string& text, Level& out) {
    Level lv;
    const char* p = text.c_str();
    bool first = true;
    bool sawGrid = false, sawTime = false, sawParTime = false,
         sawParPieces = false, sawBudget = false;

    while (const char* nl = std::strchr(p, '\n')) {
        char line[128];
        size_t len = static_cast<size_t>(nl - p);
        if (len >= sizeof(line)) return false;
        std::memcpy(line, p, len);
        line[len] = '\0';
        p = nl + 1;
        len = static_cast<size_t>(trimLine(line, static_cast<int32_t>(len)));
        if (len == 0) continue;

        if (first) {
            first = false;
            if (std::strcmp(line, kLevelHeader) != 0) return false;
            continue;
        }

        // Header field vs placement line: the first token is a known header
        // key (name/grid/time_limit/par_time/par_pieces/budget) → header;
        // otherwise it's a placement line (S/K/T/M/B, single-char tag).
        char probe[128];
        std::snprintf(probe, sizeof(probe), "%s", line);
        char* psave = nullptr;
        char* pkey = strtok_r(probe, " \t", &psave);
        const bool isHeaderKey =
            pkey && (std::strcmp(pkey, "name") == 0 || std::strcmp(pkey, "grid") == 0 ||
                     std::strcmp(pkey, "time_limit") == 0 || std::strcmp(pkey, "par_time") == 0 ||
                     std::strcmp(pkey, "par_pieces") == 0 || std::strcmp(pkey, "budget") == 0);
        if (isHeaderKey) {
            char* save = nullptr;
            char* key = strtok_r(line, " \t", &save);
            char* val = key ? strtok_r(nullptr, " \t", &save) : nullptr;
            if (!key || !val) return false;
            if (std::strcmp(key, "name") == 0) {
                lv.name = val;
            } else if (std::strcmp(key, "grid") == 0) {
                char* h = strtok_r(nullptr, " \t", &save);
                uint32_t gw, gh;
                if (!parseInt(val, gw) || !h || !parseInt(h, gh) || gw < 1 || gh < 1)
                    return false;
                lv.gridW = static_cast<int32_t>(gw);
                lv.gridH = static_cast<int32_t>(gh);
                sawGrid = true;
            } else if (std::strcmp(key, "time_limit") == 0) {
                uint32_t v;
                if (!parseInt(val, v) || v < 1) return false;
                lv.timeLimit = v;
                sawTime = true;
            } else if (std::strcmp(key, "par_time") == 0) {
                uint32_t v;
                if (!parseInt(val, v)) return false;
                lv.parTime = v;
                sawParTime = true;
            } else if (std::strcmp(key, "par_pieces") == 0) {
                uint32_t v;
                if (!parseInt(val, v)) return false;
                lv.parPieces = v;
                sawParPieces = true;
            } else if (std::strcmp(key, "budget") == 0) {
                uint32_t v;
                if (!parseInt(val, v)) return false;
                lv.budget = v;
                sawBudget = true;
            } else {
                return false;  // unknown header key
            }
            continue;
        }

        // Placement line (S/K/T/M/B) — same grammar as #JVL1.
        PlacementOp op;
        if (!parsePlacementLine(line, op)) return false;
        lv.ops.push_back(op);
    }
    if (first) return false;  // no header
    if (!sawGrid || !sawTime || !sawParTime || !sawParPieces || !sawBudget) return false;
    if (lv.ops.empty()) return false;  // a level needs at least one locked piece
    if (lv.parTime > lv.timeLimit) return false;  // par must be reachable in time

    out = lv;
    return true;
}

}  // namespace jv
