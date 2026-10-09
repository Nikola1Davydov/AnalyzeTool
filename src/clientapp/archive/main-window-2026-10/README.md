# Archived: the main window before October 2026

The main AnalyseTool window as it was until 2026-10-09: the infinite canvas of chart and table cards
(`view/InfiniteCanvas`), the Parameter Empty Check and Parameter Value Check pages, their filter bar,
the drawer menu (`layout/Sidebar.vue`) and the stores behind them.

It was replaced by the shared parameter table (`src/view/SharedParameters`) and the printable A4 report
(`src/view/Report`). Kept for reference only: nothing here is built or routed — the folder sits outside
`src/`, so Vite never sees it, and its `@/…` imports point at files that are gone or moved.

The backend commands these pages used (`GetCategoriesInRevit`, `GetDataByCategoryName`,
`SetDataToParameters`, `SelectionInRevit`, `IsolationInRevit`) are still there — they are MCP tools as well.
