# План синхронизации Core/Unity копий RoslynCodeParser.cs

## Контекст

`VisualScripting.Core/Parsers/RoslynCodeParser.cs` (Core-копия, ~2462 строки)  
`UnityPackage_Extracted/.../Core/Parsers/RoslynCodeParser.cs` (Unity-копия, ~2962 строки + `RoslynCodeParser.GraphHelpers.cs`)

Unity-копия содержит крупную подсистему разбора **пользовательских классов и методов**, которая полностью отсутствует в Core.  
Core-копия — "MVP"-ветка: циклы, if/else, математика, Unity API (после Этапов 0-9), но без класс-/метод-парсинга.

---

## Расхождения по категориям

### A. Класс/метод-парсинг (Unity-only)
| Метод | Строки (Unity) | Описание |
|-------|---------------|----------|
| `BuildMethodInfoSignature` | ~324 | Строит сигнатуру `MethodInfo` из `MethodDeclarationSyntax` |
| `ParseAndDiscoverClassMethod` | ~355 | Разбирает тело метода пользовательского класса → добавляет в `_discoveredMethods` |
| `ExtractClassFieldsFromParent` | ~373 | Достаёт поля класса для инжекции `FieldRef`-нод в тело метода |
| `MapValueType` | ~395 | Сопоставляет C#-тип → тип графа; в Core аналог — `MapDeclaredType` (без UnityLibraryRegistry-catch-all) |
| `InferVarType` | ~421 | Инферит тип `var`-переменной из инициализатора |
| `ParseMethodBodyGraph` | ~771 | Разбирает тело метода в отдельный `GraphData` (с `FieldRef`-нодами для полей класса) |
| `ExtractLocalFunctionInfo` | ~881 | Извлекает `MethodInfo` из локальной функции |

### B. `return`-оператор (Unity-only)
| Метод | Строки (Unity) | Описание |
|-------|---------------|----------|
| `ShouldBreakFlowAfter` | ~697 | Определяет, прерывать ли поток после return/пустой ветки |
| `VisitReturnStatement` | ~738 | Строит `ReturnValue`-ноду и связывает с flow |

В Core `return` не разбирается совсем.

### C. `Debug.Log` (Unity-only)
| Метод | Строки (Unity) | Описание |
|-------|---------------|----------|
| `IsDebugLog` | ~1263 | Определяет, является ли вызов `Debug.Log(...)` |
| `VisitDebugLog` | ~1279 | Строит `DebugLog`-ноду |
| `VisitMessagePrintInvocation` | ~1289 | Общая логика для `Console.WriteLine` + `Debug.Log` |

В Core есть только `VisitConsoleWriteLine` (без `Debug.Log`).

### D. Запись в пользовательское поле — FieldSet (Unity-only)
| Метод | Строки (Unity) | Описание |
|-------|---------------|----------|
| `IsFieldSymbol` | ~1402 | Проверяет, является ли идентификатор полем класса |
| `EmitFieldSet` | ~1407 | Строит `FieldSet`-ноду для присваивания в поле |

### E. Вызовы пользовательских методов (Unity-only)
| Метод | Строки (Unity) | Описание |
|-------|---------------|----------|
| `CreateMethodCallNode` | ~2422 | Создаёт `MethodCall`-ноду для вызова пользовательского метода |
| `CreatePassthroughExpressionNode` | ~2593 | Passthrough-литерал для произвольного выражения |

### F. Граф/рёбра (расхождение в существующих методах)
Unity-копия вынесла `AddEdge`/`GetDataOutPort`/`FindNodeByIdInTree` в `RoslynCodeParser.GraphHelpers.cs` и добавила:
- `PortIds.Normalize` в `AddEdge` (нормализация имён портов)
- `SupportsExecOut`/`SupportsExecIn` — guard-проверки перед добавлением рёбер
- Поддержка в `GetDataOutPort`/`SupportsExecOut/In` типов: `MethodCall`, `MethodParam`, `FieldRef`, `FieldSet`, `ReturnValue`, `DebugLog`

В Core `AddEdge` — простое добавление без нормализации/guard'ов.

---

## Предложение по этапам (когда понадобится)

**Этап A** — `AddEdge`/`GetDataOutPort`/`GraphHelpers.cs`:  
Синхронизировать Core's `AddEdge` с Unity-версией (`PortIds.Normalize` + `SupportsExecOut/In`), обновить `GetDataOutPort` для новых типов нод. Вынести в отдельный файл `RoslynCodeParser.GraphHelpers.cs` (опционально). Это фундамент для остального.

**Этап B** — `Debug.Log`:  
Обобщить `VisitConsoleWriteLine` → `VisitMessagePrintInvocation`, добавить `IsDebugLog`/`VisitDebugLog`. Низкий риск, не влияет на существующие тесты.

**Этап C** — `return`-оператор:  
`ShouldBreakFlowAfter` + `VisitReturnStatement`. Средний риск (изменяет flow-логику if/else).

**Этап D** — `FieldSet` (пользовательские поля):  
`IsFieldSymbol` + `EmitFieldSet`. Зависит от Этапа A.

**Этап E** — Класс/метод-парсинг:  
Весь блок A (выше): `ParseAndDiscoverClassMethod`, `ParseMethodBodyGraph`, `ExtractClassFieldsFromParent`, `MapValueType`/`InferVarType`, `BuildMethodInfoSignature`, `ExtractLocalFunctionInfo`. Крупнейший блок, зависит от Этапов A+D. После этого `dotnet test` сможет покрывать round-trip для классов/методов.

**Этап F** — `CreateMethodCallNode` + `CreatePassthroughExpressionNode`:  
Финальный блок — вызовы пользовательских методов из тела других методов.

---

## Оценка

| Этап | ~строк | Риск | Зависит от |
|------|--------|------|------------|
| A. AddEdge/GraphHelpers | ~50 | низкий | — |
| B. Debug.Log | ~60 | низкий | — |
| C. return | ~80 | средний | A |
| D. FieldSet | ~50 | низкий | A |
| E. Класс/метод-парсинг | ~400 | высокий | A, D |
| F. CreateMethodCallNode | ~100 | средний | E |

**Итого:** ~740 строк, 6 этапов. Рекомендуется делать строго последовательно, с `dotnet test` после каждого.
