---
type: entity
updated: 2026-08-31
status: draft
sources: [../sources/analysetool-repo-docs.md, ../sources/github-issues.md]
---

# Ollama

Провайдер AI по умолчанию для *другого* направления AI в этом репозитории — когда
плагин зовёт модель, а не агент зовёт плагин.

Зарегистрирован в `src/AnalyseTool.Tools/Ai/Infrastructure/AiProviderRegistry.cs` с id
`ollama`, отображаемым именем «Ollama (local)» и базовым адресом
`http://localhost:11434`.

Провайдеры OpenAI-совместимые: `BaseUrl` — корень, запросы чата идут на
`BaseUrl/chat/completions` через `OpenAiCompatibleChatClient.cs`. Пользовательские
провайдеры (OpenRouter, LM Studio, …) добавляются в рантайме и получают
сгенерированный id.

Потребители: `OllamaAnalyse`, `OllamaEditParameters`, `OllamaGetModels`,
`OllamaSuggestName`, `OllamaSuggestNames`, `OllamaSuggestTemplate`.

> [!warning] не проверено
> Какие модели используются на практике и как они ведут себя на промпте анализа
> параметров. Ничего не измерено — здесь место полевым заметкам и странице в
> `analyses/` со сравнением провайдеров.

## Локальность — не просто «дешёвый тариф»

Бэклог переосмысляет локальный вывод дважды, и оба переосмысления важнее деталей выше.

[#117](https://github.com/Nikola1Davydov/AnalyzeTool/issues/117) утверждает: для бюро с
запретом на выгрузку данных локальная модель — не бесплатный тариф, а **единственная
допустимая**. Тогда это условие входа в сегмент, а не экономия. В самом issue помечено
как рыночная гипотеза, требующая проверки, а не факт.

[#118](https://github.com/Nikola1Davydov/AnalyzeTool/issues/118) добавляет аргумент по
задержке: подсказка-призрак
([#45](https://github.com/Nikola1Davydov/AnalyzeTool/issues/45)) требует ответа за доли
секунды, поэтому обязана быть локальной независимо от того, что питает фонового агента.
Три яруса — мгновенно в интерфейсе, фоновая проактивность, интерактивный чат — и
локальность форсирована только на первом.

Неприятная половина: слабая модель в tool-calling-цикле **ломается, а не работает
хуже** ([#80](https://github.com/Nikola1Davydov/AnalyzeTool/issues/80)). Это и есть
аргумент в пользу
[`../concepts/deterministic-core.md`](../concepts/deterministic-core.md).

## Связанное

- [`../analyses/agent-hosting.md`](../analyses/agent-hosting.md) · [`../concepts/deterministic-core.md`](../concepts/deterministic-core.md)
- [`../overview.md`](../overview.md)
