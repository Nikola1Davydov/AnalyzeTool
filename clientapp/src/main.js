import "./assets/main.css";

import { createPinia } from "pinia";
import { createApp } from "vue";
import PrimeVue from "primevue/config";
import Aura from "@primeuix/themes/aura";
import InputText from "primevue/inputtext";
import IconField from "primevue/iconfield";
import InputIcon from "primevue/inputicon";
import Button from "primevue/button";
import Select from "primevue/select";
import App from "./App.vue";
import router from "./router";
import TreeTable from "primevue/treetable";
import Column from "primevue/column";
import ColumnGroup from "primevue/columngroup"; // optional
import Row from "primevue/row"; // optional
import ProgressBar from "primevue/progressbar";
import Slider from "primevue/slider";
import SelectButton from "primevue/selectbutton";
import Drawer from "primevue/drawer";
import Panel from "primevue/panel";

import { definePreset } from "@primeuix/themes";

const app = createApp(App);
const stylePreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: "{blue.50}",
      100: "{blue.100}",
      200: "{blue.200}",
      300: "{blue.300}",
      400: "{blue.400}",
      500: "{blue.500}",
      600: "{blue.600}",
      700: "{blue.700}",
      800: "{blue.800}",
      900: "{blue.900}",
      950: "{blue.950}",
    },
    colorScheme: {
      dark: {
        surface: {
          0: "{neutral.0}",
          50: "{neutral.50}",
          100: "{neutral.100}",
          200: "{neutral.200}",
          300: "{neutral.300}",
          400: "{neutral.400}",
          500: "{neutral.500}",
          600: "{neutral.600}",
          700: "{neutral.700}",
          800: "{neutral.800}",
          900: "{neutral.900}",
          950: "{neutral.950}",
        },
      },
    },
  },
});
app.use(PrimeVue, {
  theme: {
    preset: stylePreset,
    options: {
      darkModeSelector: ".my-app-dark",
    },
  },
});
app.component("InputText", InputText);
app.component("Select", Select);
app.component("Button", Button);
app.component("IconField", IconField);
app.component("InputIcon", InputIcon);
app.component("TreeTable", TreeTable);
app.component("Column", Column);
app.component("ColumnGroup", ColumnGroup);
app.component("SelectButton", SelectButton);
app.component("Row", Row);
app.component("ProgressBar", ProgressBar);
app.component("Slider", Slider);
app.component("Drawer", Drawer);
app.component("Panel", Panel);

app.use(createPinia());
app.use(router).mount("#app");
