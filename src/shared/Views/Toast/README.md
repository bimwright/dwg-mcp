# Toast source provenance

Visual implementation adapted from `bimwright/rvt-mcp` (Apache-2.0), commit
`2856e36e46163ef73579e6ee9c7d5c9bb193bb43`, `src/shared/Views/Toast/`:
McpToastWindow, McpToastManager, McpToastTheme, McpToastViewModel,
ToastThumbnailLoader, ToolNameFormatter and ToolActivityClassifier.

The theme, typography, card layout, animations, stack limits and timing are retained.
DWG adaptations: non-activating AutoCAD-owned windows, immediate close during fades,
owner movement, bounded file reads, BMP thumbnails, DWG command categories and safe
content mapping. AutoCAD Idle/ribbon integration, preferences, generation-scoped queue
and request completion boundary are local implementations. No RVT binary is required.

`ToastRuntime` is the only AutoCAD UI integration. Worker threads may only publish
DTOs through its queue. `McpToastManager` and windows must stay on the host UI dispatcher.
The server must not reference this UI directory.
