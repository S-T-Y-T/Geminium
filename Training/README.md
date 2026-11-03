# Обучение ботов (ML-Agents)

Здесь всё, что нужно, чтобы заново обучить нейросеть ботов (GDD 8.2, TODO 3.5). Обученная модель уже лежит в проекте: `Assets/Geminium/ML/Models/GeminiumBot.onnx`.

## Как устроено

1. **Суррогатная модель цеха** — быстрые формулы вместо поминутного цеха. Её откалибровали по прогонам полного цеха: Unity, меню **Geminium → Обучение → Калибровать суррогат**. Числа записаны в `GameDatabase.asset`, раздел `Surrogate`.
2. **Обучение PPO на суррогате.**
   - Три компании-архетипа учатся одной общей политикой и играют против её копий (self-play).
   - Шаг — игровой месяц, эпизод — 36 месяцев.
   - Награда — прирост капитализации за месяц; банкротство — штраф.
3. **Дообучение на полном цехе.** Короткое, потому что месяц полного цеха считается в тысячи раз дольше.
4. **Экспорт в ONNX и запуск в игре** через Inference Engine (`OnnxBotPolicy`). Наблюдения и действия у суррогата и полного цеха одинаковые (`Core/Lab/BotPolicy.cs`), поэтому политика переносится без изменений.

## Один раз: Python для тренера

Тренеру `mlagents` нужен Python 3.10 (на машине стоит 3.13). Окружение ставится через `uv` в папку с латинским путём — torch на Windows не любит кириллицу в пути:

```powershell
uv python install 3.10.12
uv venv --python 3.10.12 $env:USERPROFILE\.geminium\mlagents
uv pip install --python $env:USERPROFILE\.geminium\mlagents\Scripts\python.exe "mlagents==1.1.0" "torch==2.2.2" "setuptools<81"
```

## Обучить заново

Все команды — из корня проекта, редактор Unity закрыт.

1. Собрать среду обучения (Windows-приложение со сценой `Training.unity`):
   ```powershell
   & "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe" -batchmode -nographics -projectPath . -executeMethod Geminium.EditorTools.TrainingTools.BuildFromCommandLine -quit
   ```
   Получится `%USERPROFILE%\.geminium\env\GeminiumTraining.exe`. Путь нарочно латиницей: сетевая библиотека ML-Agents (gRPC) не загружается из папки с кириллицей в пути, а проект лежит в «БМИ\симулятор». То же самое делает меню **Geminium → Обучение → Собрать среду обучения**.
2. Обучение на суррогате (32 мира одновременно):
   ```powershell
   $ml = "$env:USERPROFILE\.geminium\mlagents\Scripts"
   & "$ml\mlagents-learn.exe" Training\geminium_ppo.yaml --env="$env:USERPROFILE\.geminium\env\GeminiumTraining.exe" --run-id=bots_v1 --results-dir="$env:USERPROFILE\.geminium\results" --env-args -geminiumArenas 32
   ```
3. Дообучение на полном цехе (4 мира):
   ```powershell
   & "$ml\mlagents-learn.exe" Training\geminium_finetune.yaml --env="$env:USERPROFILE\.geminium\env\GeminiumTraining.exe" --run-id=bots_v1_full --initialize-from=bots_v1 --results-dir="$env:USERPROFILE\.geminium\results" --env-args -geminiumMode full -geminiumArenas 4
   ```
4. Скопировать модель в проект:
   ```powershell
   Copy-Item "$env:USERPROFILE\.geminium\results\bots_v1_full\GeminiumBot.onnx" Assets\Geminium\ML\Models\GeminiumBot.onnx
   ```
5. Сравнить ботов. Есть три режима:
   - все скриптовые;
   - все под нейросетью;
   - нейросеть только у игрока.

   Запуск — меню **Geminium → Обучение → Сравнить ботов** или:
   ```powershell
   & "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe" -batchmode -nographics -projectPath . -executeMethod Geminium.EditorTools.TrainingTools.CompareFromCommandLine -geminiumSeeds 5 -geminiumMonths 36 -quit
   ```
   Результат — `Reports\bots_compare.csv`.

Графики обучения: `& "$ml\tensorboard.exe" --logdir "$env:USERPROFILE\.geminium\results"`, затем в браузере `http://localhost:6006`.

## Hugging Face Hub

Версии моделей хранятся на Hugging Face Hub (GDD 8.2). Публикует автор со своим токеном:

```powershell
& "$ml\huggingface-cli.exe" login
& "$ml\mlagents-push-to-hf.exe" --run-id=bots_v1_full --local-dir="$env:USERPROFILE\.geminium\results\bots_v1_full" --repo-id="<имя>/geminium-bots" --commit-message="Боты Geminium, обучение на суррогате + дообучение на полном цехе"
```

## Файлы

| Файл | Что это |
|---|---|
| `geminium_ppo.yaml` | настройки PPO для обучения на суррогате |
| `geminium_finetune.yaml` | настройки короткого дообучения на полном цехе |
| `Assets/Geminium/ML/` | агент, площадки обучения, менеджер обучения, ONNX-политика |
| `Assets/Geminium/Core/Lab/Surrogate/` | суррогатная модель, калибровка, суррогатный мир |
| `Assets/Geminium/Editor/TrainingTools.cs` | калибровка, сборка среды, сравнение ботов |
