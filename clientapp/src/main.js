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
import ContextMenu from "primevue/contextmenu";
import Tag from "primevue/tag";
import Checkbox from "primevue/checkbox";
import AutoComplete from "primevue/autocomplete";

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
app.component("ContextMenu", ContextMenu);
app.component("Tag", Tag);
app.component("Checkbox", Checkbox);
app.component("AutoComplete", AutoComplete);

app.use(createPinia());
app.use(router).mount("#app");
