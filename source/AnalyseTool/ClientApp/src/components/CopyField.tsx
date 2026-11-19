import { Button, Tooltip } from "@fluentui/react-components";
import { Checkmark16Regular, Copy16Regular } from "@fluentui/react-icons";
import { useState } from "react";

async function copyText(text: string) {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    // An older WebView without clipboard permission: the hidden text area way
    const area = document.createElement("textarea");
    area.value = text;
    document.body.appendChild(area);
    area.select();
    document.execCommand("copy");
    area.remove();
  }
}

/** A command line or a path in a code box, with a copy button. */
export function CopyField({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="copy-field">
      <pre>{text}</pre>
      <Tooltip content={copied ? "Copied" : "Copy"} relationship="label">
        <Button
          appearance="subtle"
          size="small"
          icon={copied ? <Checkmark16Regular /> : <Copy16Regular />}
          onClick={async () => {
            await copyText(text);
            setCopied(true);
            setTimeout(() => setCopied(false), 1500);
          }}
        />
      </Tooltip>
    </div>
  );
}
