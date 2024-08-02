# AnalyseTool Plugin for Revit

> Powerful tool to analyze and export parameter data in Revit projects

[![github release version](https://img.shields.io/github/v/release/Nikola1Davydov/AnalyzeTool.svg?include_prereleases)](https://github.com/Nikola1Davydov/AnalyzeTool/releases/latest) [![license](https://img.shields.io/github/license/nhn/tui.editor.svg)](https://github.com/Nikola1Davydov/AnalyzeTool/blob/master/LICENSE) ![Static Badge](https://img.shields.io/badge/revitVersion-2023--2025-orange) [![LINKEDIN](https://img.shields.io/badge/LINKEDIN-ff1414)](https://linkedin.com/in/nikolai-davydov-4359bba1)

## Overview

![AnalyseTool Screenshot](img/Overview.png)

The AnalyseTool Plugin is a powerful tool for Autodesk Revit that allows users to analyze and export parameter data of elements within a Revit project. With this plugin, you can easily export data to CSV formats, making it easier to manage and share project information.
## Installation

1. **build it**
2. **place dll in folder**: C:\ProgramData\Autodesk\Revit\Addins\202X

## Requirements

- Autodesk Revit 2023 or higher
- .NET Framework 4.8 or higher

## Features

- **Parameter Analysis**: Analyze parameters of elements in your Revit project.
- **Export to CSV**: Export analyzed data to a CSV file.
- **Flexible Filtering**: Filter parameters based on various criteria.
- **Category Grouping**: Group parameters by categories for better organization.

![Filter in AnalyzeTool](img/filter.png)




## Usage

### Analyzing Parameters

1. **Open Revit Project**: Open your project in Revit.
2. **Launch AnalyseTool**: Go to `Add-Ins` > `AnalyseTool` to launch the plugin.
3. **View Parameters**: The plugin will display a list of parameters for elements in your project.
4. **Filter Parameters**: Use the filter box to search for specific parameters.

### Exporting Data

#### Export to CSV

1. Click the `Export to CSV` button.
2. Choose the location to save the CSV file.
3. Click `Save`.

## Development

### Prerequisites

- Visual Studio 2019 or higher
- .NET Framework 4.8
- EPPlus
- iText7
- CsvHelper

### Building the Plugin

1. Clone the repository:
   ```sh
   git clone https://github.com/your-repo/AnalyseTool.git



<!-- MARKDOWN LINKS & IMAGES -->
[issues-shield]: https://img.shields.io/github/issues/othneildrew/Best-README-Template.svg?style=flat-square
[issues-url]: https://github.com/Nikola1Davydov/AnalyzeTool/issues
[license-shield]: https://img.shields.io/github/license/othneildrew/Best-README-Template.svg?style=flat-square
[license-url]: https://github.com/Nikola1Davydov/AnalyzeTool/blob/main/LICENSE
[linkedin-shield]: https://img.shields.io/badge/-LinkedIn-black.svg?style=flat-square&logo=linkedin&colorB=555
[linkedin-url]: <a href = "https://www.linkedin.com/in/SEU_LINKEDIN_AQUI-4b872715a/" target="_blank">