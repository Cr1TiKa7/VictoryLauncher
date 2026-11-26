//! Hand written for the tests, in the shape of AnyXboxX's crates/anyxboxx/src/run/settings.rs.
//! A mention in a comment must not count: pub const TUNED: &[(&str, &str)] = &[("VICTORY_FROM_COMMENT", "1")];

/// Applied on every normal launch.
pub const TUNED: &[(&str, &str)] = &[
    ("VICTORY_CARD_WINDOWS_MB", "1536"),
    // ("VICTORY_COMMENTED_OUT", "1"),
    ("VICTORY_VRAM_IDLE_MS", "8000,2000,1000"), // a trailing remark
    (
        "VICTORY_PIPELINE_WAIT_MS",
        "2500",
    ),
    ("VICTORY_OCEAN_READBACK", "2")
];

/// Optional: only with --quality.
pub const QUALITY: &[(&str, &str)] = &[
    ("VICTORY_CARD_WINDOWS_MB", "0"),
    ("VICTORY_ONLY_WITH_QUALITY", "256"),
];

/// Set before the tuned set.
pub const ALWAYS: &[(&str, &str)] = &[("VICTORY_DISCORD", "0"), ("VICTORY_OCEAN_READBACK", "1")];

pub const GAME_DIR: &str = "VICTORY_GAME_DIR";
pub const AUTO_A: &str = "VICTORY_AUTO_A";
