import type { Bar } from "./report";

// A horizontal bar chart in plain SVG: no canvas, so it prints sharp and needs no chart library.

const FILLED = "#16a34a";
const EMPTY = "#fda4af";
const VALUE = "#3b82f6";

const ROW = 16;
const LABEL_WIDTH = 190;
const WIDTH = 640;
const AXIS = 18;

function truncate(text: string, length = 34) {
  return text.length > length ? `${text.slice(0, length - 1)}…` : text;
}

export function BarChart({ bars, stacked }: { bars: Bar[]; stacked: boolean }) {
  const max = Math.max(1, ...bars.map((bar) => bar.value + (stacked ? (bar.rest ?? 0) : 0)));
  const plot = WIDTH - LABEL_WIDTH - 40;
  const height = bars.length * ROW + AXIS + (stacked ? 18 : 0);
  const scale = (value: number) => (value / max) * plot;
  const ticks = [0, 0.25, 0.5, 0.75, 1].map((fraction) => Math.round(max * fraction));

  return (
    <svg className="bar-chart" viewBox={`0 0 ${WIDTH} ${height}`} role="img">
      {ticks.map((tick, index) => (
        <g key={index}>
          <line
            x1={LABEL_WIDTH + scale(tick)}
            x2={LABEL_WIDTH + scale(tick)}
            y1={0}
            y2={bars.length * ROW}
            stroke="#e2e8f0"
          />
          <text x={LABEL_WIDTH + scale(tick)} y={bars.length * ROW + 12} fontSize={9} textAnchor="middle" fill="#64748b">
            {tick}
          </text>
        </g>
      ))}
      {bars.map((bar, index) => {
        const y = index * ROW;
        return (
          <g key={index}>
            <title>
              {bar.label}: {bar.value}
              {stacked ? ` filled, ${bar.rest ?? 0} empty` : ""}
            </title>
            <text x={LABEL_WIDTH - 6} y={y + ROW / 2 + 3} fontSize={9} textAnchor="end" fill="#334155">
              {truncate(bar.label || "(empty)")}
            </text>
            <rect x={LABEL_WIDTH} y={y + 2} width={scale(bar.value)} height={ROW - 4} fill={stacked ? FILLED : VALUE} rx={1.5} />
            {stacked && (
              <rect
                x={LABEL_WIDTH + scale(bar.value)}
                y={y + 2}
                width={scale(bar.rest ?? 0)}
                height={ROW - 4}
                fill={EMPTY}
                rx={1.5}
              />
            )}
            <text
              x={LABEL_WIDTH + scale(bar.value + (stacked ? (bar.rest ?? 0) : 0)) + 4}
              y={y + ROW / 2 + 3}
              fontSize={9}
              fill="#334155"
            >
              {stacked ? `${bar.value}/${bar.value + (bar.rest ?? 0)}` : bar.value}
            </text>
          </g>
        );
      })}
      {stacked && (
        <g transform={`translate(${LABEL_WIDTH}, ${bars.length * ROW + AXIS + 4})`}>
          <rect width={10} height={8} fill={FILLED} />
          <text x={14} y={8} fontSize={9} fill="#334155">
            Filled
          </text>
          <rect x={60} width={10} height={8} fill={EMPTY} />
          <text x={74} y={8} fontSize={9} fill="#334155">
            Empty
          </text>
        </g>
      )}
    </svg>
  );
}
