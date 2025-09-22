<template>
  <div>
    <canvas ref="chart"></canvas>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { Chart, PieController, ArcElement, Tooltip, Legend } from 'chart.js'

// Регистрируем нужные модули Chart.js
Chart.register(PieController, ArcElement, Tooltip, Legend)

const chart = ref(null)
let chartInstance = null

onMounted(() => {
  // слушаем сообщения от C#
  window.chrome?.webview?.addEventListener("message", e => {
    const data = JSON.parse(e.data)

    const labels = data.map(d => d.value)
    const counts = data.map(d => d.count)

    if (chartInstance) {
      chartInstance.destroy()
    }

    chartInstance = new Chart(chart.value, {
      type: 'pie',
      data: {
        labels,
        datasets: [{
          data: counts,
          backgroundColor: [
            '#FF6384',
            '#36A2EB',
            '#FFCE56',
            '#4BC0C0',
            '#9966FF',
            '#FF9F40'
          ]
        }]
      },
      options: {
        onClick: (evt, elements) => {
          if (elements.length > 0) {
            const i = elements[0].index
            const clickedLabel = labels[i]
            // отправляем обратно в C#
            window.chrome?.webview?.postMessage({ selectedValue: clickedLabel })
          }
        }
      }
    })
  })
})
</script>
