---
name: add-command
description: >
  Add a pipeline command: the code that reads or changes the Revit model for a page or a ribbon button.
  USE FOR: a new command class in Features/, reading or changing the active document, transactions, progress and cancellation of long work, errors the caller understands, calling a command from a page, a button or an AI client (MCP).
  DO NOT USE FOR: the button itself (add-ribbon-button); the page and its window (add-page); middleware that applies to every command.
---

# Add a command

A command is a class in `Features/<slice>/` — like an ASP.NET Core controller. The registry finds it by reflection at
startup; the command runs on the thread pool and reaches the model only through `ActiveDocument`.

## Step 1: Write the command class

Without input:

```csharp
using AnalyseTool.Services.Documents;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Features.Families;

/// <summary>
///     Returns the families of the active document.
/// </summary>
[Command(Description = "Returns the name and category of every family in the active document.")]
public sealed class GetFamilies(ActiveDocument document) : ICommand<IReadOnlyList<FamilyItem>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<FamilyItem>> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync<IReadOnlyList<FamilyItem>>(
            revitDocument => new FilteredElementCollector(revitDocument)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Select(family => new FamilyItem(family.Id.Value, family.Name, family.FamilyCategory?.Name))
                .ToList(),
            context.CancellationToken);
    }
}

/// <summary>An item of <see cref="GetFamilies" />.</summary>
public sealed record FamilyItem(long Id, string Name, string? Category);
```

With input — `ICommand<TRequest, TResponse>`, and `ExecuteAsync(TRequest request, CommandContext context)`:

```csharp
public sealed record RenameFamiliesRequest(IReadOnlyList<long> Ids, string Prefix);

public sealed class RenameFamilies(ActiveDocument document) : ICommand<RenameFamiliesRequest, Empty>
```

* The command name is the class name without the `Command` suffix (`GetFamilies`), or `[Command("Name")]`. Names are
  unique; the registry reports duplicates and broken commands at startup.
* `[Command(Description = ...)]` says what the command does — for people and AI clients.
* Request and response are records; JSON is camelCase both ways. `Empty` is the response of a command without a result.
* A request whose parameters all have defaults (`record PageRequest(int Size = 10)`) may come without a payload and gets
  the defaults; with a required parameter a call without a payload is `bad_request`.
* Dependencies come through the constructor; a command is scoped to one call.

## Step 2: Reach the model through `ActiveDocument`

* `InvokeAsync(revitDocument => ...)` — work on the Revit thread with the document of this call; `InvokeUIAsync(uiDocument => ...)`
  for selection and views.
* Never `ActiveUIDocument` in `Features/` and `Services/`: if the user switches documents during the call, `ActiveDocument`
  fails with `document_changed` instead of touching another model. `build/Check-Boundaries.ps1` rejects it.
* Never use the Revit API outside the lambda: everything else runs on the thread pool.
* Return plain data (records, ids, strings), not Revit objects: they are valid only on the Revit thread.

Changing the model — a transaction inside the lambda:

```csharp
await document.InvokeAsync(revitDocument =>
{
    using var transaction = new Transaction(revitDocument, "Rename families");
    transaction.Start();
    // ...
    transaction.Commit();
}, context.CancellationToken);
```

## Step 3: Long work — batches, progress, cancellation

```csharp
await document.InvokeBatchedAsync(ids, 500, (revitDocument, batch) =>
{
    // one Revit call per batch
}, context.Progress, context.CancellationToken);
```

Between batches Revit gets control back, the page shows progress («N / M») and the user can cancel. Changing the model
in batches means one transaction — one undo step — per batch. Report progress yourself with
`context.Progress.Report(new ProgressInfo(fraction, message))`. Pass `context.CancellationToken` everywhere.

## Step 4: Errors the caller understands

```csharp
throw new CommandException("project_only", "Open a project, not a family.");
```

A `CommandException` reaches the caller as its code and message: a page shows the message, a button shows it in a task
dialog. Any other exception becomes `failed` and is logged as an error. Codes the caller already knows:
`CommandErrorCodes` (`not_found`, `bad_request`, `already_running`, `canceled`, `failed`) and `DocumentErrorCodes`
(`no_document`, `document_changed`).

## Step 5: Call the command

From a page — the hooks of `ClientApp/src/revit/hooks.ts` add the `Document` header for you:

```tsx
const families = useCommand<FamilyItem[]>("GetFamilies");
const rename = useCommandMutation<void, RenameFamiliesRequest>("RenameFamilies");
rename.mutate({ ids, prefix: "FAM_" });   // rename.progress, rename.cancel()
```

Add a mock answer to `ClientApp/src/mocks.ts` so the page works in a normal browser with `npm run dev`.

From a ribbon button — fire and forget, failures are shown to the user:

```csharp
Host.GetService<ICommandSender>().Send<RenameFamilies>(new RenameFamiliesRequest(ids, "FAM_"));
```

From an AI client (Claude Code, Cursor, VS Code) — mark the class with `[McpTool]` of `AnalyseTool.Hosting.Mcp`:

```csharp
[McpTool(ReadOnly = true)]
[Command(Description = "Returns the name and category of every family in the active document.")]
public sealed class GetFamilies(ActiveDocument document) : ICommand<IReadOnlyList<FamilyItem>>
```

* Only marked commands are visible to AI clients. The tool name is the command name; the description is required —
  the client picks tools by it, so say what the command does and what it returns.
* The request type must be a record with properties: its JSON schema is the tool's input. `[McpTool(ReadOnly = true)]`
  for a command that only reads, `Destructive = true` for one that deletes or overwrites.
* A `CommandException` reaches the client as a tool error `code: message`; the call has no `Document` header, so the
  document is the one active at the first access.

## Check

`dotnet run -c Release --project build` checks the boundaries and builds. In Revit: the command logs
`Command GetFamilies (webview2) succeeded in N ms`; switching documents during the call gives `document_changed`.
