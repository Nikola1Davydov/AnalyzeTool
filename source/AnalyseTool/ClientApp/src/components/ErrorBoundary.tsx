import { Component, type ErrorInfo, type ReactNode } from "react";

type State = { error: Error | null; stack: string | null };

/**
 * A page that fails while it renders shows what failed — with a way to reload — instead of a blank white window: React
 * empties the page on such an error, and inside Revit nobody sees the console.
 */
export class ErrorBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { error: null, stack: null };

  static getDerivedStateFromError(error: Error): State {
    return { error, stack: null };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(error, info.componentStack);
    this.setState({ stack: info.componentStack ?? null });
  }

  render() {
    const { error, stack } = this.state;
    if (!error) return this.props.children;
    return (
      <div className="crash">
        <h2>This page failed</h2>
        <p>Something in AnalyseTool went wrong while showing it. Reload it; if it fails again, send this text with «Report a bug».</p>
        <pre>{`${error.name}: ${error.message}\n${(error.stack ?? "").split("\n").slice(1, 6).join("\n")}${stack ? `\n\nIn:${stack.split("\n").slice(0, 8).join("\n")}` : ""}`}</pre>
        <button onClick={() => location.reload()}>Reload</button>{" "}
        <button onClick={() => this.setState({ error: null, stack: null })}>Try again</button>
      </div>
    );
  }
}
