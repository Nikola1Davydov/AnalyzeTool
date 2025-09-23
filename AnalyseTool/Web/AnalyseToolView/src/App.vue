<script setup>
import { ref } from "vue";

const selectedCity = ref();
const cities = ref([
    { name: 'New York', code: 'NY' },
    { name: 'Rome', code: 'RM' },
    { name: 'London', code: 'LDN' },
    { name: 'Istanbul', code: 'IST' },
    { name: 'Paris', code: 'PRS' }
]);
</script>

<template>
  <div >
    <div class="card">
      <Select />
      <IconField class="IconField">
          <InputIcon />
          <InputText v-model="value1" placeholder="Search" />
      </IconField>
      <Button label="Send" @click="userdata" />
    </div>
    <div >
      <TreeTable tableStyle="min-width: 50rem">
          <Column field="parameter" header="Parameter" expander></Column>
          <Column field="count" header="Count"></Column>
          <Column field="parameterEmpty" header="Parameter Empty"></Column>
          <Column field="parameterFilled" header="Parameter Filled"></Column>
          <Column field="parameterFilledPercent" header="Parameter Filled in %"></Column>
      </TreeTable>
    </div>
  </div>

</template>

<style scoped>
.card {
  display: flex;
  align-items: center;
  width: 100%;
  gap: 0.5rem;
}
</style>
