# Обращение к команде: парсер, граф и плагин (MVP)

Привет, команда!

Ниже — **актуальное состояние** после перехода парсера на **Roslyn** и согласования контракта графа с тимлидом. **Sprint 2 (4–16 апреля 2026):** циклы, встроенные методы, порты потока, исправление сохранения JSON с `Vector2`.

---

## Обновление от 16.06.2026 — поддержка Unity API (Этапы 0–9)

**Контекст:** реализована полная поддержка вызовов Unity API (`Vector3`, `Mathf`, `Transform`, `GameObject`, `Time`, `Input`, `Random`, `Debug`, `Object`) в парсере, генераторе и визуальном редакторе — то, что в предыдущей редакции этого документа было помечено как "вне скоупа".

| Этап | Что сделано |
|------|-------------|
| **0. Синхронизация `NodeType`** | Зафиксированы и синхронизированы между Core и Unity-копией существующие типы нод, устранены мелкие несоответствия перед началом работы. |
| **1. `UnityLibraryRegistry`** | Новый реестр (`VisualScripting.Core/Models/UnityLibraryRegistry.cs`, зеркало в Unity-копии) с описанием членов (`UnityMemberInfo`/`UnityClassInfo`) встроенных классов **Mathf, Vector3, Object, GameObject, Transform, Input, Time, Random, Debug** — методы, поля/свойства, статичность, сигнатуры, параметры по умолчанию. |
| **2. Система типов** | Расширена поддержка `Vector3` и ссылочных Unity-типов (`Transform`, `GameObject`, …) как значений графа (`ValueType`), включая узлы-литералы `Vector3`. |
| **3. Generic-ноды Unity API** | Добавлены `NodeType.UnityMethodCall` (вызов метода/статической функции), `UnityFieldAccess` (чтение поля/свойства), `UnityFieldSet` (запись поля/свойства), `UnityVector3` (литерал Vector3). Поля `NodeData`: `Value`=ClassName, `MemberName`=метод/поле, `ValueType`=тип результата, `OwnerExpression`=выражение-получатель (`transform`, `this.target`, …), `VariableName`=присваиваемая переменная. |
| **4. Генератор кода** | `SimpleCodeGenerator` генерирует корректные C#-вызовы для всех трёх generic-нод на основе `UnityLibraryRegistry` (`BuildUnityMethodCallExpr`, `BuildUnityFieldAccessExpr`, `EmitUnityFieldSet`) — например `Vector3.MoveTowards(...)`, `transform.position = ...`, `Mathf.Abs(x)`. |
| **5. Парсер код → граф** | `RoslynCodeParser` распознаёт вызовы и обращения к полям/свойствам Unity API через `UnityLibraryRegistry` (`TryResolveUnityReceiver`/`TryResolveUnityFieldAccess`/`TryResolveUnityMethodCall`) и строит generic Unity-ноды; `KnownUnityReceiverTypes` позволяет резолвить `transform.*`/`gameObject.*` независимо от объявленного типа поля. |
| **6. Моки UnityEngine** | Для прогона сгенерированного кода вне Unity (через `CSharpProcessRunner`) добавлены заглушки типов `UnityEngine` (`Vector3`, `Mathf`, `Transform`, `GameObject`, `Debug`, `Time`, …), чтобы код с Unity API компилировался и исполнялся в standalone-раннере. |
| **7. UI: категория "Unity"** | В `NodeToolbarView` добавлена отдельная категория "Unity" со списком классов/методов из `UnityLibraryRegistry`; попутно исправлен баг round-trip (см. ниже). |
| **8. Фикс round-trip + тесты** | Исправлен `RoslynCodeParser` (`VisitLocalDeclaration` и простое присваивание): `UnityMethodCall`/`UnityFieldAccess` теперь распознаются как "готовые значения" и не оборачиваются в нулевые литералы-заглушки. Подтверждено round-trip тестами (`DiagUnityRoundTripTest`, обновлённый `GeneratorTests`) и 5 ручными сценариями в Unity (`Vector3.MoveTowards`, `Vector3.zero/up` + `Time.deltaTime`, переприсвоение, цепочка `Mathf`). |
| **9. Поля класса `Transform`/`GameObject`** | `MapValueType` (Unity-копия `RoslynCodeParser.cs`) распознаёт ссылочные Unity-типы через `UnityLibraryRegistry.GetClass` и не сводит их к `"int"` — поля класса вида `public Transform transform;` теперь парсятся и генерируются с правильным типом. В `FieldEditPopup` добавлены типы `Vector3`/`Transform`/`GameObject` (для ссылочных типов поле "начальное значение" недоступно). |

**Файлы:** `VisualScripting.Core/Models/UnityLibraryRegistry.cs` (+ зеркало), `NodeType.cs`, `NodeData.cs`, `RoslynCodeParser.cs`, `SimpleCodeGenerator.cs` (Core + `UnityPackage_Extracted/.../Core/`), `Editor/Nodes/Views/NodeToolbarView.cs`, `Editor/Classes/FieldEditPopup.cs`.

**Известное расхождение:** Unity-копия `RoslynCodeParser.cs` содержит дополнительную подсистему разбора классов/методов (`ParseAndDiscoverClassMethod`/`ParseMethodBodyGraph`), которой пока нет в Core-копии — синхронизация запланирована отдельным этапом.

---

## Обновление от 09.04.2026 — Sprint 2 финальная доработка (Backend 2, Егор)

**Контекст:** полная переработка логики переменных по ТЗ тимлида. Избавление от нод `VariableSet`/`VariableDeclaration`, фиксы парсера и локали float.

| Область | Что сделано |
|--------|-----------|
| **Очистка контракта** | Удалены ноды `VariableSet`, `VariableDeclaration`, `VariableGet`, `VariableAssignment` из `NodeType` во всём проекте (Core + Unity). |
| **Логика переменных** | `RoslynCodeParser` и `SimpleCodeGenerator` полностью переписаны: вместо создания специальных "нод переменных", теперь для объявлений и присваиваний (например, `int x = 20;`) парсер создаёт обычный литерал или ноду операции, прописывая ей свойство `VariableName = "x"`. Если значение берётся из другой ноды (`x = y`), создаётся литерал-заглушка с входом `inputValue`. Это решает проблемы с удалением нод и визуальным "мусором". |
| **Баг float (локаль)** | Везде добавлен `CultureInfo.InvariantCulture` (`FloatNode.cs`, `VisualScriptingWindow.cs`). Это решает ошибку, когда `44.555` не парсилось при русской локали ОС. |
| **Нода `Console.WriteLine`** | Добавлено открытое поле `messageText` на самой ноде. Теперь можно вписать текст прямо в ноду без обязательного подключения провода. |
| **Упрощение UI литералов** | У нод литералов (Int, Float, Bool, String) скрыты системные поля (добавлен `[HideInInspector]`), заголовок (`name`) возвращает лаконичные значения (например, `Int: 20` или `x = 20`), как и просил тимлид. |
| **Кнопка `Run`** | В `VisualScriptingWindow.cs` и `GraphRunner.cs` полностью исправлен запуск графа. Теперь `GraphRunner` отслеживает цепочку `execIn/execOut`, заполняет локальный словарь переменных по их `VariableName` и корректно выполняет ветвления `if`, циклы и методы. |
| **Восстановление связей** | В `VisualScriptingWindow.cs` убрано ошибочное игнорирование `execIn`/`execOut` при десериализации рёбер, порты ищутся по `fieldName` или `displayName`, поэтому "the edge can't be properly connected" больше не возникает. |
| **Тесты** | Добавлено 15 новых тестов на все изменённые сценарии. Итого 42/42 тестов проходят успешно (`dotnet test`). |

---

## Обновление от 04.04.2026 — Sprint 2 (Backend 1, до предзащиты)

| Область | Изменение |
|--------|-----------|
| **Сохранение графа** | Исправлена ошибка Newtonsoft `Self referencing loop … Vector2.normalized`: [`Vector2JsonConverter`](UnityPackage_Extracted/Assets/Plugins/CustomVisualScripting/Integration/Vector2JsonConverter.cs) + `ReferenceLoopHandling.Ignore` в [`GraphSaver`](UnityPackage_Extracted/Assets/Plugins/CustomVisualScripting/Integration/GraphSaver.cs). |
| **`NodeType`** | Добавлены: `FlowFor`, `FlowWhile`, `ConsoleWriteLine`, `IntParse`, `FloatParse`, `ToStringConvert`, `MathfAbs`, `MathfMax`, `MathfMin` (Core + Unity `Core/Models` синхронизированы). |
| **Парсер** | `for` / `while` с портами `execIn`/`execOut`, `init`/`condition`/`increment`/`body` (for), `condition`/`body` (while). Составные присваивания `+=`…`%=`, пре/пост `++`/`--`. Вызовы: `Console.WriteLine`, `int.Parse` / `float.Parse`, `.ToString()`, `Mathf.Abs`/`Max`/`Min`. В обёртку парсера добавлена **заглушка `Mathf`**, чтобы разбор работал вне Unity. |
| **Генератор** | `SimpleCodeGenerator`: `for`/`while`, `Console.WriteLine`, встроенные методы; `System.Math` для Abs/Max/Min в генерируемом C#; корни потока — любая первая инструкция без входящего `execIn` (в т.ч. одиночный `Console.WriteLine`). Составные операции в коде дают эквивалент (`a += 2` → `a = a + 2`). |
| **Runtime (Unity)** | `NodeExecutor`: `ConsoleWriteLine` → `Debug.Log`, `IntParse`/`FloatParse`/`ToStringConvert`, `Mathf*`. `GraphRunner` пропускает `FlowFor`/`FlowWhile` (как `FlowIf`), без полноценного исполнения циклов в рантайме. |
| **Интеграция / редактор** | `GraphConverter`, `GraphSerializer` — цвета и подписи для новых типов. |
| **Тесты** | [`GeneratorTests.cs`](VisualScripting.Tests/GeneratorTests.cs) — **27** тестов (`dotnet test` зелёный). |

---

## Обновление от 28.03.2026 — что сделано (Backend 1 + интеграция)

| Область | Изменение |
|--------|-----------|
| **Парсер** | `RoslynCodeParser` переписан на **Microsoft.CodeAnalysis** (обход AST, без Regex и без Shunting Yard). |
| **Модель** | В `NodeData` добавлено поле **`VariableName`**. Парсер **не заполняет** `ExecutionFlow` / `InputConnections` — только **`Edges`** с именами портов по контракту. |
| **`NodeType`** | Добавлены: `MathModulo`, `CompareNotEqual`, `CompareGreaterOrEqual`, `CompareLessOrEqual`, `LogicalAnd`, `LogicalOr`, `LogicalNot`, `FlowElse`. Enum в Core и в `UnityPackage_Extracted` **синхронизирован**. |
| **Рёбра** | Единая схема портов: математика `inputA` / `inputB` / `output`; сравнения и логика (`&&`, ИЛИ, `!`) — `left` / `right` / `result` или `input` / `result` для `!`; `If` — `condition`, поток `true` / `false`; выполнение — `execIn` / `execOut`. |
| **Присваивание** | Результат — **один узел операции** с `VariableName` (без отдельного узла объявления для `z` в `int z = x + y`). Ссылки на переменные — **прямые рёбра** к узлу-источнику, **без VariableGet**. |
| **if / else if / else** | Цепочка **`FlowIf`** по ветке `false`, финальный **`FlowElse`**; тела — рёбра потока между инструкциями. |
| **Editor-ноды** | `CustomBaseNode` наследует **`BaseExecutionNode`** (есть `execIn` / `execOut`). Обновлены порты у математики и литералов. Добавлены **7 новых нод** + **`ElseNode`**. **`IfNode`** переведён на потоковую модель (`condition` + выходы `true` / `false`). |
| **Интеграция** | `GraphConverter` / `GraphSerializer` — цвета и имена для новых типов. `ParserBridge` логирует число нод и рёбер после парса. |
| **Генератор** | `SimpleCodeGenerator.GenerateCode` — **MVP-подмножество**: литералы с именем + бинарная математика по `Edges` (для простого roundtrip в тестах). |
| **Тесты** | Удалён устаревший `ParserCodegenTests` (ожидал другой парсер и Unity-скрипты). Добавлен **`RoslynParserMvpTests`** (5 тестов), `dotnet test` зелёный. |
| **Runtime** | `NodeExecutor` переведён на **`switch` по `NodeType`**, чтение входов из **`Edges`** (плюс legacy `InputConnections`). `GraphRunner` передаёт граф в executor. |

**Файлы «источник правды» в репозитории:** `VisualScripting.Core/` (парсер, модели, генератор, тесты); зеркало под Unity — `UnityPackage_Extracted/Assets/Plugins/CustomVisualScripting/`.

---

## Контракт портов (кратко, для Backend 2 и Fullstack)

| Тип узла | Входы | Выходы |
|----------|--------|--------|
| Литералы | — | `output` |
| Math (Add, Subtract, Multiply, Divide, Modulo) | `inputA`, `inputB` | `output` |
| Compare (Equal, Greater, Less, NotEqual, >=, <=) | `left`, `right` | `result` |
| And / Or | `left`, `right` | `result` |
| Not | `input` | `result` |
| If | `execIn`, `condition` | `true`, `false` (ветки потока); далее по цепочке `execOut` у листьевых инструкций |
| Else | `execIn` | `execOut` |
| **For** | `execIn`, данные: `init`, `condition`, `increment` | `body` (поток в тело), `execOut` (после цикла) |
| **While** | `execIn`, `condition` | `body`, `execOut` |
| **Console.WriteLine** | `execIn`, `message` | `execOut` |
| **int.Parse / float.Parse** | `input` | `output` |
| **ToString** | `input` | `output` |
| **Mathf.Abs** | `input` | `output` |
| **Mathf.Max / Min** | `inputA`, `inputB` | `output` |
| VariableSet | `execIn`, `value` | `execOut` |
| Прочие (Unity, переменные) | см. существующие ноды | — |

`VariableName`: непустой только у литерала с объявлением и у **корневого** узла результата присваивания; у промежуточных операций — пустая строка.

---

## Скоуп парсера (актуально после Sprint 2)

**Поддерживается:**

- Объявления `int x = …`, `float`, `string`, `bool` **с инициализатором** и **без инициализатора** (`int x;`).
- Простое присваивание `z = …` и **составные** `+=`, `-=`, `*=`, `/=`, `%=`.
- Префиксный/постфиксный `++` / `--` (как цепочка `Math` + `VariableSet`).
- Арифметика: `+ - * / %`, скобки, приоритеты из Roslyn.
- Сравнения: `== != > < >= <=`.
- Логика: `&&`, `||`, `!`.
- `if` / `else if` / `else`, вложенные `if`.
- Циклы **`for`**, **`while`** (в т.ч. вложенные в блоки).
- Вызовы: **`Console.WriteLine(...)`**, **`int.Parse`**, **`float.Parse`**, **`.ToString()`**, **`Mathf.Abs` / `Max` / `Min`** (через заглушку `Mathf` в обёртке парсера).
- **Unity API** (см. раздел «Обновление от 16.06.2026»): `Vector3` (литералы, статика, методы вроде `MoveTowards`), `Mathf`, `Transform`/`GameObject` (включая поля класса и `transform.position`/`gameObject.*`), `Time`, `Input`, `Random`, `Debug.Log`, `Object` — через `UnityLibraryRegistry` и generic-ноды `UnityMethodCall`/`UnityFieldAccess`/`UnityFieldSet`/`UnityVector3`.

**Пока не поддерживается или ограничено:**

- Массивы, дженерики, `switch`, `return`, async — по-прежнему вне скоупа.
- **Рантайм:** ветвление и циклы в `GraphRunner` не исполняются по полной семантике C# (узлы потока пропускаются или упрощённо); для демо опираться на **генератор кода** и тесты.
- Unity API в Core-копии `RoslynCodeParser.cs` пока не покрывает разбор пользовательских классов/методов (`ParseAndDiscoverClassMethod`/`ParseMethodBodyGraph`) — эта подсистема есть только в Unity-копии, синхронизация в планах.

Оператор степени в стиле Python **не входит**.

---

## Что НЕ входит в текущий скоуп (напоминание)

- Массивы, коллекции, обобщённые типы
- `switch`, `return`, корутины, async
- Полное пошаговое исполнение циклов/ветвлений в `GraphRunner` без доработки Backend 2

---

## Пример кода для ручной проверки парсера в редакторе

```csharp
int x = 10;
int y = 20;
int z = x + y * 2 % 3;
bool flag = true;
if (x >= y && z != 0 || !flag)
{
    z = x + y;
}
else
{
    z = x - y;
}
```

Ожидается: ноды литералов, `Multiply` → `Modulo` → `Add`, цепочка логики к `FlowIf`, ветка `FlowElse`, рёбра с портами из таблицы выше.

---

## Задачи по ролям (обновлено)

### Backend 1 (парсер / мост) — статус

Sprint 2 (ТЗ 4–16 апреля): циклы, встроенные методы, порты потока в рёбрах, исправление JSON/`Vector2`, тесты расширены. Дальше — согласование с Backend 2 / Fullstack по **визуальным** нодам `For`/`While`/`Console.WriteLine` и по исполнению циклов в рантайме.

### Backend 2 (генератор / рантайм)

- Ориентироваться на **`Edges`** и **`VariableName`**, а не на `ExecutionFlow` от парсера.
- Расширять `SimpleCodeGenerator` под `if`, сравнения и логику по мере необходимости; согласовать порядок эмиссии инструкций с топологией графа.

### Fullstack / визуальный граф (GraphProcessor)

- Подключить отображение новых типов нод и проводов с **именами портов из контракта**.
- Доработать **`GraphSerializer`**: сериализация/десериализация **`Edges`** (сейчас в коде помечено TODO).

### Тимлид / сценарий демо

- Для записи демо использовать **фрагменты из раздела «Пример кода»** или согласовать новый целевой скрипт после расширения парсера под Unity.

---

## Вызов API (без изменений по смыслу)

```csharp
// Код → граф
var result = ParserBridge.Parse(codeText);
if (result.HasErrors) {
    foreach (var err in result.Errors)
        Debug.LogError(err);
} else {
    // загрузка result.Graph в UI / GraphView
}

// Граф → код (MVP генератора — ограниченный подмножество)
var generator = new SimpleCodeGenerator();
string code = generator.GenerateCode(graph);
```

---

## Тесты (актуально)

| Файл / класс | Количество | Назначение |
|----------------|------------|------------|
| `GeneratorTests.cs` | 27 | Арифметика, `%`, сравнения, логика, `if`/`else if`/`else`, объявление без инициализатора, `for` + составное присваивание в теле, `while` + декремент, `Console.WriteLine`, `int`/`float`.Parse, `ToString`, `+=`, `Mathf` Abs/Max/Min, roundtrip через `SimpleCodeGenerator`. |

Запуск: из корня репозитория `dotnet test VisualScripting.Tests/VisualScripting.Tests.csproj`.

---

## Структура в репозитории

```
VisualScripting.Core/           ← парсер, модели, SimpleCodeGenerator, тесты (ссылка из .Tests)
UnityPackage_Extracted/Assets/Plugins/CustomVisualScripting/
├── Core/                      ← зеркало моделей и RoslynCodeParser (исходники; в Unity нужны ещё DLL Roslyn)
├── Editor/Nodes/              ← визуальные ноды (GraphProcessor)
├── Integration/               ← ParserBridge, GraphConverter, GraphSaver (+ Vector2JsonConverter)
└── Runtime/Execution/         ← NodeExecutor, GraphRunner
```

**Точка внимания при merge:** `NodeType.cs`, `NodeData.cs`, `GraphData.cs`, `RoslynCodeParser.cs` — правки только по согласованному контракту.

---

Если нужна короткая выжимка для чата команды — переслать раздел **«Обновление от 04.04.2026 — Sprint 2»** и таблицу **«Контракт портов»**.
