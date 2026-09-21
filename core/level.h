// JoyVeyor core — #JVLG1 level format (v1.0, jv-design-gameplay §Level Format).
//
// A level is the *setup* of a round: a metadata header + locked pieces
// (pre-placed, not movable/deletable — a game-layer flag, not a sim change).
// Player pieces are built in-game within `budget`; they are NOT stored here.
//
// Format (text):
//   #JVLG1
//   name       <free text>
//   grid       <W> <H>
//   time_limit <ticks>      # 30 Hz ticks (30 s = 900)
//   par_time   <ticks>      # 2★ condition
//   par_pieces <count>      # 3★ condition (player pieces)
//   budget     <count>      # max player-placed pieces
//   <placement lines>       # S/K/T/M/B, same grammar as #JVL1 (S carries period)
//
// parseLevel() is atomic: parse + validate everything, then commit. Malformed
// input leaves the level unchanged and returns false.
#pragma once
#include "save.h"
#include "world.h"

#include <string>
#include <vector>

namespace jv {

struct Level {
    std::string name;
    int32_t gridW = 0;
    int32_t gridH = 0;
    uint32_t timeLimit = 0;    // ticks
    uint32_t parTime = 0;      // ticks (2★)
    uint32_t parPieces = 0;    // player pieces (3★)
    uint32_t budget = 0;       // max player-placed pieces
    std::vector<PlacementOp> ops;  // locked pieces, file order (nodes then belts)
};

// Parse a #JVLG1 level string. Returns false on malformed input (out unchanged).
bool parseLevel(const std::string& text, Level& out);

// Place every op in file order onto `w` (no reset). Returns false if any
// placement is rejected.
bool applyOps(World& w, const std::vector<PlacementOp>& ops);

// Apply a parsed level to a fresh world: reset + place every locked piece.
bool applyLevel(World& w, const Level& lv);

// Parse placement lines (S/K/T/M/B) out of a #JVL1/#JVLG1 text body, skipping
// the header + blank lines. Used for reference-solution (.sol) files. Returns
// false on malformed input.
bool parsePlacementOps(const std::string& text, std::vector<PlacementOp>& out);

// Read a whole file into a string. Returns false if unreadable.
bool readFile(const std::string& path, std::string& out);

}  // namespace jv
