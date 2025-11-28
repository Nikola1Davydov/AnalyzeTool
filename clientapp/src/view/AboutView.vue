<script setup lang="ts">
import { Panel } from "primevue";
</script>

<template>
  <div class="p-4">
    <Panel header="About the Project " class="mb-4">
      This plugin is an open-source tool designed to make working with Revit parameters easier:
      collect data quickly, analyze it, and export it in a clean format. It started as a personal
      productivity tool, but now it’s available to everyone — not just to use, but to shape and
      improve.
    </Panel>
    <Panel header="Why Open Source?" class="mb-4">
      Open code is an invitation to collaborate. No one should have to start from scratch when they
      could build on something that already works. If you see a way to improve this tool, you can do
      so. This isn’t a closed product — it’s a platform that grows with you.
    </Panel>
    <Panel header="How to Contribute" class="mb-4">
      You’re welcome to: <br />- add new features <br />- fix bugs <br />- improve the UI <br />-
      write or refine documentation <br />- propose ideas and suggestions <br />
      Every contribution matters. Even small improvements are valuable.
    </Panel>
    <Panel header="Getting Started" class="mb-4">
      The source code is available on GitHub:
      <a
        href="https://github.com/Nikola1Davydov/AnalyzeTool"
        target="_blank"
        class="text-blue-600"
        rel="noopener noreferrer"
        >https://github.com/Nikola1Davydov/AnalyzeTool</a
      >
      <br />
      Pull requests, issues, and discussions are highly appreciated. If you need help navigating the
      codebase or want ideas for your first contribution, feel free to get in touch. We’re building
      a tool that helps people work with Revit data more efficiently — and we’re doing it together.

      <br />
      <br />

      <b>To report issues or share ideas, visit:</b>
      <a
        href="https://github.com/Nikola1Davydov/AnalyzeTool/issues"
        target="_blank"
        class="text-blue-600"
        rel="noopener noreferrer"
        >https://github.com/Nikola1Davydov/AnalyzeTool/issues</a
      >
      <br />
    </Panel>
  </div>
</template>
