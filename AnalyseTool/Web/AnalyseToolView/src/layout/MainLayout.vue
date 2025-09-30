<script setup>
import TopPanel from "@/components/TopPanel.vue";
import BodyTable from "@/components/BodyTable.vue";
import ParameterFilledEmptyChart from "@/components/ParameterFilledEmptyChart.vue";
import Cart from "@/components/Cart.vue";
</script>

<template>
  <div class="layout-main">
    <div class="p-5 gap-5">
      <TopPanel />
      <Cart title="Element Count by Category">
        <ParameterFilledEmptyChart />
      </Cart>
      <BodyTable />
    </div>
  </div>
</template>
