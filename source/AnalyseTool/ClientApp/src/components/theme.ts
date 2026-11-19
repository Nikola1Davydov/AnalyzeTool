import { type BrandVariants, createDarkTheme, createLightTheme, type Theme } from "@fluentui/react-components";

// The look of the dock pane (pages/dock/pane.css) for every window: the system colours of iOS — the blue accent, grouped grey
// backgrounds with white cards, hairline separators — rounder corners and Segoe UI Variable. Fluent UI keeps its components;
// only its tokens change, so every page follows without being rewritten.

/** The blue of iOS (#007aff light, #0a84ff dark) as a Fluent brand ramp: 80 is the light accent, 100 the dark one. */
const blue: BrandVariants = {
  10: "#000d1f",
  20: "#00193a",
  30: "#002655",
  40: "#003370",
  50: "#00408b",
  60: "#004ea6",
  70: "#005fcc",
  80: "#007aff",
  90: "#1a86ff",
  100: "#0a84ff",
  110: "#3d9bff",
  120: "#5eacff",
  130: "#80beff",
  140: "#a1cfff",
  150: "#c2e0ff",
  160: "#e3f0ff",
};

const shared: Partial<Theme> = {
  fontFamilyBase: '"Segoe UI Variable Text", "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif',
  borderRadiusSmall: "6px",
  borderRadiusMedium: "8px",
  borderRadiusLarge: "12px",
  borderRadiusXLarge: "14px",
};

export const lightTheme: Theme = {
  ...createLightTheme(blue),
  ...shared,
  colorNeutralForeground1: "#000000",
  colorNeutralForeground2: "#3c3c43",
  colorNeutralForeground3: "#8a8a8e",
  colorNeutralForeground4: "#aeaeb2",
  colorNeutralBackground1: "#ffffff",
  colorNeutralBackground2: "#f2f2f7",
  colorNeutralBackground3: "#e9e9ee",
  colorNeutralBackground4: "#e5e5ea",
  colorNeutralBackground5: "#dcdce1",
  colorNeutralStroke1: "#c6c6c8",
  colorNeutralStroke2: "#e5e5ea",
  colorNeutralStroke3: "#efeff4",
  colorSubtleBackgroundHover: "rgb(120 120 128 / 12%)",
  colorSubtleBackgroundPressed: "rgb(120 120 128 / 20%)",
};

export const darkTheme: Theme = {
  ...createDarkTheme(blue),
  ...shared,
  colorBrandForeground1: blue[110],
  colorBrandForegroundLink: blue[110],
  colorNeutralForeground1: "#ffffff",
  colorNeutralForeground2: "rgb(235 235 245 / 80%)",
  colorNeutralForeground3: "rgb(235 235 245 / 55%)",
  colorNeutralForeground4: "rgb(235 235 245 / 35%)",
  colorNeutralBackground1: "#1c1c1e",
  colorNeutralBackground2: "#000000",
  colorNeutralBackground3: "#2c2c2e",
  colorNeutralBackground4: "#2c2c2e",
  colorNeutralBackground5: "#3a3a3c",
  colorNeutralStroke1: "#48484a",
  colorNeutralStroke2: "#38383a",
  colorNeutralStroke3: "#2c2c2e",
  colorSubtleBackgroundHover: "rgb(120 120 128 / 24%)",
  colorSubtleBackgroundPressed: "rgb(120 120 128 / 36%)",
};
