# Сборка и проверки

Команды выполняются из корня репозитория. Для статусов оружия см.
[FrostMelee/docs/STATUS.md](FrostMelee/docs/STATUS.md).

## Что требуется

1. Установленный **tModLoader** с `tMLMod.targets` и библиотеками.
2. Совместимый с этой установкой **.NET SDK** (runtime сам по себе недостаточен).
   Проекты ранее использовали net8.0; конкретную совместимую версию SDK/tModLoader
   нужно зафиксировать после успешной сборки. Не считать старые `obj` доказательством.
3. Для генерации масок и Python-тестов — Python 3 и Pillow из `tools/requirements.txt`.

В текущей среде агента .NET SDK и tModLoader не установлены. Загрузка SDK и
release-архива tModLoader не удалась из-за сети. Инструкции ниже — способ сборки
при наличии зависимостей, а не заявление об успешной сборке здесь.

## Сборка C#

Общий `tModLoader.targets` использует свойство `TModLoaderPath`. По умолчанию:
`C:\Program Files (x86)\Steam\steamapps\common\tModLoader`.
Путь должен указывать на каталог с `tMLMod.targets`, не на исходники мода.

### Windows / PowerShell

```powershell
dotnet --info
$tml = 'C:\Program Files (x86)\Steam\steamapps\common\tModLoader'
Test-Path "$tml\tMLMod.targets"
dotnet build FrostMelee/FrostMelee.csproj "-p:TModLoaderPath=$tml"
dotnet build ReaperMelee/ReaperMelee.csproj "-p:TModLoaderPath=$tml"
dotnet build WoodSwordsOverhaul/WoodSwordsOverhaul.csproj "-p:TModLoaderPath=$tml"
```

Если `Test-Path` вернул `False`, укажите реальное расположение tModLoader.

### Linux / macOS

```sh
dotnet --info
dotnet build FrostMelee/FrostMelee.csproj -p:TModLoaderPath="/path/to/tModLoader"
dotnet build ReaperMelee/ReaperMelee.csproj -p:TModLoaderPath="/path/to/tModLoader"
dotnet build WoodSwordsOverhaul/WoodSwordsOverhaul.csproj -p:TModLoaderPath="/path/to/tModLoader"
```

Можно вместо параметра использовать переменную окружения `TModLoaderPath`.
Проект `HardcoreMelee/HardcoreMelee.csproj` не является общей сборкой всей серии:
каталог сейчас содержит главным образом дизайн-документы. Собирать три игровых
мода следует отдельно. Упаковка/перезагрузка мода в игре выполняется через
инструменты разработки tModLoader; успешный `dotnet build` ещё не игровой тест.

## Маски попаданий FrostMelee

Маски запекаются из исходных PNG в `WeaponCollisionMasks.Generated.cs`.
После изменения исходного спрайта их нужно пересоздать: PNG сам по себе
не обновляет геометрию в сгенерированном файле.

### Windows / PowerShell

```powershell
py -3 -m venv .venv
.venv\Scripts\python.exe -m pip install -r tools/requirements.txt
.venv\Scripts\python.exe tools/generate_collision_masks.py
.venv\Scripts\python.exe tools/generate_collision_masks.py --check
.venv\Scripts\python.exe -m unittest discover -s tests -v
```

### Linux / macOS

```sh
python3 -m venv .venv
.venv/bin/python -m pip install -r tools/requirements.txt
.venv/bin/python tools/generate_collision_masks.py
.venv/bin/python tools/generate_collision_masks.py --check
.venv/bin/python -m unittest discover -s tests -v
```

Для проверки без изменения файлов пропустите команду генерации без `--check`.
Тесты проверяют актуальность, порог alpha, биты и соответствие масок пикселям,
а также отсутствие GPU-зависимостей в построителе. `test_snowball_contracts.py`
дополнительно проверяет контракты контроллера цепов v8 (серверный ввод,
размеры пакетов, стадии, геометрию, рецепты, ресурс и локализацию), а также
независимые математические модели периода, зазора и урона. Эти тесты **не исполняют C#**, не
проверяют игровые столкновения, баланс, локализацию или сетевой обмен.
`test_north_pole_contracts.py` добавляет 11 контрактов исходников Северного полюса
и 5 независимых моделей заряда/промахов/размера/баффа; эти проверки тоже не
исполняют C#. Игровой чеклист: [уникальные.md](FrostMelee/docs/уникальные.md).

## Минимальная игровая регрессия

Записать версию tModLoader, SHA коммита, включённые моды и режим игры.

- [ ] Сборка и загрузка каждого из трёх модов без ошибок.
- [ ] Меч/копьё: взмах, тычки, встреча, смена слота, бросок и подбор.
- [ ] Разные экземпляры одного оружия не блокируют друг друга после подбора.
- [ ] Геометрия совпадает с видимым лезвием; обычный/замороженный спрайты.
- [ ] Амарок: курсор, наводка, круг, лёд, перчатка/сумка и возврат.
- [ ] Бумеранг: лёгкий, размах, тяжёлый, подбор, препятствия и вода.
- [ ] Айсберг: прямое попадание, глыбы, раскол, тычок, встреча и глазурь.
- [ ] RU/EN, индикаторы и настройки; с Weapon Out Lite и без него.
- [ ] Повтор на dedicated server с двумя клиентами; смерть, выход и поздний вход.

Подробные сетевые сценарии — [NETWORK_STABILIZATION.md](NETWORK_STABILIZATION.md).
Результат записывать отдельно от реализации: «написано» не равно «проверено».
