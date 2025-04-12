# UEArchitecture

| Unreal-like Concept | Purpose |
|---------------------|---------|
| **GameInstance**    | Holds global data that persists across scenes, such as login info, settings, or user progress. Acts as a singleton. |
| **PlayerState**     | Stores player-specific data that persists during the game session, like name, score, stats, etc. Can be network-synced. |
| **GameEvents**      | Central hub for broadcasting and listening to global events across systems (e.g., match start, UI updates, powerups). Helps decouple logic. |
| **SceneRoot**       | Scene-level controller that combines GameMode rules and LevelBlueprint-like visual logic. Manages level-specific logic and initialization. |
