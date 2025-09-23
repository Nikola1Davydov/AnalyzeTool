// import './assets/main.css'

import { createApp } from 'vue'
import PrimeVue from "primevue/config";
import Aura from '@primeuix/themes/aura';
import InputText from 'primevue/inputtext';
import IconField from 'primevue/iconfield';
import InputIcon from 'primevue/inputicon';
import Button from 'primevue/button';
import Select from 'primevue/select';
import App from './App.vue'
import TreeTable from 'primevue/treetable';
import Column from 'primevue/column';
import ColumnGroup from 'primevue/columngroup';   // optional
import Row from 'primevue/row';                   // optional

const app = createApp(App);

app.use(PrimeVue, 
    {theme: {
        preset: Aura,
    }
});
app.component('InputText', InputText);
app.component('Select', Select);
app.component('Button', Button);
app.component('IconField', IconField);
app.component('InputIcon', InputIcon);
app.component('TreeTable', TreeTable);
app.component('Column', Column);
app.component('ColumnGroup', ColumnGroup);
app.component('Row', Row);

app.mount('#app');
