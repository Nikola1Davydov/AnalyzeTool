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
import TreeTable from "primevue/treetable";
import Column from "primevue/column";
import ColumnGroup from "primevue/columngroup"; // optional
import Row from "primevue/row"; // optional
import ProgressBar from "primevue/progressbar";
import Slider from "primevue/slider";
import SelectButton from "primevue/selectbutton";

const app = createApp(App);

app.use(PrimeVue, {
  theme: {
    preset: Aura,
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

app.use(createPinia());
app.mount("#app");
