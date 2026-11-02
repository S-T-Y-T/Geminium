# Генератор дизайн-документа

Отсюда собираются `Docs/Geminium_GDD.docx` и `Docs/Geminium_GDD.pdf`.

## Обновить документ

Из корня проекта:

```
powershell -ExecutionPolicy Bypass -File Docs/src/update-docs.ps1
```

Скрипт рисует схемы, собирает DOCX, затем открывает его в Word: Word обновляет оглавление и сохраняет итоговые DOCX и PDF. При первом запуске скрипт сам ставит npm-пакеты.

Нужны Node.js и Microsoft Word. На этой машине стоит Word 2007: он не умеет перезаписывать файл через SaveAs, поэтому скрипт сохраняет во временную папку и потом копирует.

## Где что лежит

- `build.js` — весь текст документа: разделы, таблицы, формулы, журнал решений (Приложение А). Вверху файла — `VERSION` и `DATE`.
- `diagrams.js` — схемы. Каждая схема — функция в объекте `diagrams`, рисует SVG и сохраняет PNG в `img/`.
- `shots/` — снимки из Unity (хранятся в git). Снимок цеха делается при закрытом редакторе Unity, без `-nographics`:
  ```
  Unity.exe -batchmode -projectPath <проект> -executeMethod Geminium.EditorTools.GeminiumTools.CaptureFloorFromCommandLine -geminiumMonths 6 [-geminiumCompany 0] [-geminiumSpaghetti] -geminiumOut Docs/src/shots/floor_layout.png -quit
  ```
- `img/` и `out/` — промежуточные файлы, в git не попадают.

## Как править

- **Текст:** правьте нужный раздел в `build.js`. Помощники: `H1/H2/H3` — заголовки, `P` — абзац, `B` — маркированный список, `N` — нумерованный, `T` — таблица, `F` — блок формул, `Note` — выноска, `Fig` — схема из `img/`, `Shot` — снимок из `shots/`. В тексте `**жирный**` и `~~курсив~~`.
- **Формулы:** строка в блоке `F` должна быть не длиннее ~80 символов, иначе Word перенесёт её.
- **Новое решение:** добавьте строку в Приложение А и обновите затронутые разделы.
- **Новая схема:** добавьте функцию в `diagrams.js` и вставьте её в `build.js` через `Fig('имя', 'подпись')`.
- **Новая версия:** поднимите `VERSION` и `DATE` в `build.js`.
