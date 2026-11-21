import { ProgressBar, Text } from "@fluentui/react-components";
import { useEffect, useState } from "react";
import { bridge } from "revit-bridge";
import { type ModelTestProgress, modelTestsProgressEvent } from "../../api/modelTests";

/**
 * The run of model tests going on now, whoever started it: null when none is. The host sends a step before every test and
 * one at the end; the end clears it.
 */
export function useRunProgress(): ModelTestProgress | null {
  const [progress, setProgress] = useState<ModelTestProgress | null>(null);
  useEffect(
    () =>
      bridge.on(modelTestsProgressEvent, (payload) => {
        const step = payload as ModelTestProgress;
        setProgress(step.testId === null || step.done >= step.total ? null : step);
      }),
    [],
  );
  return progress;
}

/** «12 / 42 · the test running now», with a bar of the whole run. */
export function RunProgress({ progress }: { progress: ModelTestProgress }) {
  return (
    <div className="mt-progress" role="status" aria-live="polite">
      <ProgressBar value={progress.total ? progress.done / progress.total : undefined} thickness="large" />
      <Text size={200} className="mt-progress-text">
        <b>
          {progress.done + 1} / {progress.total}
        </b>{" "}
        · {progress.test}
        {progress.suite && <span className="muted"> · {progress.suite}</span>}
      </Text>
    </div>
  );
}
