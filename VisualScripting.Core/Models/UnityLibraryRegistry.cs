#nullable enable
using System.Collections.Generic;

namespace VisualScripting.Core.Models
{
    /// <summary>Тип члена встроенного Unity-класса.</summary>
    public enum UnityMemberKind
    {
        Method,
        Field,
        Property,
        Operator,
        Constructor
    }

    /// <summary>Параметр метода/конструктора Unity-API.</summary>
    public class UnityParamInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";

        /// <summary>Значение по умолчанию (как строка C#-литерала), если параметр опционален. "" — обязателен.</summary>
        public string DefaultValue { get; set; } = "";
    }

    /// <summary>Описание одного члена (метода/поля/свойства) встроенного Unity-класса.</summary>
    public class UnityMemberInfo
    {
        public string Name { get; set; } = "";
        public UnityMemberKind Kind { get; set; } = UnityMemberKind.Method;

        /// <summary>Тип результата (для полей/свойств — тип значения; для void-методов — "void").</summary>
        public string ReturnType { get; set; } = "void";

        /// <summary>Статический член (Mathf.Abs, Vector3.zero) или член экземпляра (transform.position).</summary>
        public bool IsStatic { get; set; } = true;

        public List<UnityParamInfo> Parameters { get; set; } = new();

        /// <summary>Короткая человекочитаемая сигнатура для тултипов в UI.</summary>
        public string Signature { get; set; } = "";

        /// <summary>Произвольные заметки (особенности генерации кода, ограничения и т.п.).</summary>
        public string Notes { get; set; } = "";

        /// <summary>Входит в MVP-список тимлида ("Итоговый список обязательного").</summary>
        public bool Mvp { get; set; }
    }

    /// <summary>Описание встроенного Unity-класса (категории для панели "Unity" в Create Node).</summary>
    public class UnityClassInfo
    {
        /// <summary>Имя класса как в C# (Mathf, Vector3, GameObject, Transform, ...).</summary>
        public string ClassName { get; set; } = "";

        /// <summary>Имя для отображения в UI (по умолчанию совпадает с ClassName).</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>Категория верхнего уровня в панели "Unity" (для группировки в UI).</summary>
        public string Category { get; set; } = "";

        public List<UnityMemberInfo> Fields { get; set; } = new();
        public List<UnityMemberInfo> Methods { get; set; } = new();
    }

    /// <summary>
    /// Статический реестр встроенных Unity-классов/методов/полей, доступных в категории
    /// "Unity" панели создания нод. MVP-набор соответствует "Итоговому списку обязательного"
    /// от тимлида (без Start/Update).
    /// </summary>
    public static class UnityLibraryRegistry
    {
        public static readonly List<UnityClassInfo> Classes = new()
        {
            // ───────────────────────── Математика (Mathf) ─────────────────────────
            new UnityClassInfo
            {
                ClassName = "Mathf",
                DisplayName = "Mathf",
                Category = "Математика",
                Methods =
                {
                    new UnityMemberInfo
                    {
                        Name = "Abs", Kind = UnityMemberKind.Method, ReturnType = "float", IsStatic = true,
                        Parameters = { new UnityParamInfo { Name = "f", Type = "float" } },
                        Signature = "Mathf.Abs(float f)", Mvp = true
                    },
                    new UnityMemberInfo
                    {
                        Name = "Clamp", Kind = UnityMemberKind.Method, ReturnType = "float", IsStatic = true,
                        Parameters =
                        {
                            new UnityParamInfo { Name = "value", Type = "float" },
                            new UnityParamInfo { Name = "min", Type = "float" },
                            new UnityParamInfo { Name = "max", Type = "float" }
                        },
                        Signature = "Mathf.Clamp(float value, float min, float max)", Mvp = true
                    },
                    new UnityMemberInfo
                    {
                        Name = "Lerp", Kind = UnityMemberKind.Method, ReturnType = "float", IsStatic = true,
                        Parameters =
                        {
                            new UnityParamInfo { Name = "a", Type = "float" },
                            new UnityParamInfo { Name = "b", Type = "float" },
                            new UnityParamInfo { Name = "t", Type = "float" }
                        },
                        Signature = "Mathf.Lerp(float a, float b, float t)", Mvp = true
                    },
                    new UnityMemberInfo
                    {
                        Name = "Pow", Kind = UnityMemberKind.Method, ReturnType = "float", IsStatic = true,
                        Parameters =
                        {
                            new UnityParamInfo { Name = "f", Type = "float" },
                            new UnityParamInfo { Name = "p", Type = "float" }
                        },
                        Signature = "Mathf.Pow(float f, float p)",
                        Notes = "Запрошено Диманом Мурзаевым (возведение в степень).", Mvp = true
                    },
                    new UnityMemberInfo
                    {
                        Name = "Max", Kind = UnityMemberKind.Method, ReturnType = "float", IsStatic = true,
                        Parameters =
                        {
                            new UnityParamInfo { Name = "a", Type = "float" },
                            new UnityParamInfo { Name = "b", Type = "float" }
                        },
                        Signature = "Mathf.Max(float a, float b)",
                        Notes = "Уже реализовано отдельной нодой MathfMax (категория Math).", Mvp = false
                    },
                    new UnityMemberInfo
                    {
                        Name = "Min", Kind = UnityMemberKind.Method, ReturnType = "float", IsStatic = true,
                        Parameters =
                        {
                            new UnityParamInfo { Name = "a", Type = "float" },
                            new UnityParamInfo { Name = "b", Type = "float" }
                        },
                        Signature = "Mathf.Min(float a, float b)",
                        Notes = "Уже реализовано отдельной нодой MathfMin (категория Math).", Mvp = false
                    }
                }
            },

            // ───────────────────────────── Векторы ────────────────────────────────
            new UnityClassInfo
            {
                ClassName = "Vector2",
                DisplayName = "Vector2",
                Category = "Векторы",
                Fields =
                {
                    new UnityMemberInfo { Name = "zero",  Kind = UnityMemberKind.Field, ReturnType = "Vector2", IsStatic = true, Signature = "Vector2.zero",  Mvp = true },
                    new UnityMemberInfo { Name = "one",   Kind = UnityMemberKind.Field, ReturnType = "Vector2", IsStatic = true, Signature = "Vector2.one",   Mvp = true },
                    new UnityMemberInfo { Name = "up",    Kind = UnityMemberKind.Field, ReturnType = "Vector2", IsStatic = true, Signature = "Vector2.up",    Mvp = true },
                    new UnityMemberInfo { Name = "down",  Kind = UnityMemberKind.Field, ReturnType = "Vector2", IsStatic = true, Signature = "Vector2.down",  Mvp = true },
                    new UnityMemberInfo { Name = "left",  Kind = UnityMemberKind.Field, ReturnType = "Vector2", IsStatic = true, Signature = "Vector2.left",  Mvp = true },
                    new UnityMemberInfo { Name = "right", Kind = UnityMemberKind.Field, ReturnType = "Vector2", IsStatic = true, Signature = "Vector2.right", Mvp = true }
                }
            },

            new UnityClassInfo
            {
                ClassName = "Vector3",
                DisplayName = "Vector3",
                Category = "Векторы",
                Fields =
                {
                    new UnityMemberInfo { Name = "zero",    Kind = UnityMemberKind.Field, ReturnType = "Vector3", IsStatic = true, Signature = "Vector3.zero",    Mvp = true },
                    new UnityMemberInfo { Name = "up",      Kind = UnityMemberKind.Field, ReturnType = "Vector3", IsStatic = true, Signature = "Vector3.up",      Mvp = true },
                    new UnityMemberInfo { Name = "down",    Kind = UnityMemberKind.Field, ReturnType = "Vector3", IsStatic = true, Signature = "Vector3.down",    Mvp = true },
                    new UnityMemberInfo { Name = "left",    Kind = UnityMemberKind.Field, ReturnType = "Vector3", IsStatic = true, Signature = "Vector3.left",    Mvp = true },
                    new UnityMemberInfo { Name = "right",   Kind = UnityMemberKind.Field, ReturnType = "Vector3", IsStatic = true, Signature = "Vector3.right",   Mvp = true },
                    new UnityMemberInfo { Name = "forward", Kind = UnityMemberKind.Field, ReturnType = "Vector3", IsStatic = true, Signature = "Vector3.forward", Mvp = true },
                    new UnityMemberInfo { Name = "back",    Kind = UnityMemberKind.Field, ReturnType = "Vector3", IsStatic = true, Signature = "Vector3.back",    Mvp = true }
                },
                Methods =
                {
                    new UnityMemberInfo
                    {
                        Name = "Lerp", Kind = UnityMemberKind.Method, ReturnType = "Vector3", IsStatic = true,
                        Parameters =
                        {
                            new UnityParamInfo { Name = "a", Type = "Vector3" },
                            new UnityParamInfo { Name = "b", Type = "Vector3" },
                            new UnityParamInfo { Name = "t", Type = "float" }
                        },
                        Signature = "Vector3.Lerp(Vector3 a, Vector3 b, float t)",
                        Notes = "Запрошено Кириллом.", Mvp = true
                    },
                    new UnityMemberInfo
                    {
                        Name = "Distance", Kind = UnityMemberKind.Method, ReturnType = "float", IsStatic = true,
                        Parameters =
                        {
                            new UnityParamInfo { Name = "a", Type = "Vector3" },
                            new UnityParamInfo { Name = "b", Type = "Vector3" }
                        },
                        Signature = "Vector3.Distance(Vector3 a, Vector3 b)",
                        Notes = "Запрошено Кириллом.", Mvp = true
                    },
                    new UnityMemberInfo
                    {
                        Name = "MoveTowards", Kind = UnityMemberKind.Method, ReturnType = "Vector3", IsStatic = true,
                        Parameters =
                        {
                            new UnityParamInfo { Name = "current", Type = "Vector3" },
                            new UnityParamInfo { Name = "target", Type = "Vector3" },
                            new UnityParamInfo { Name = "maxDistanceDelta", Type = "float" }
                        },
                        Signature = "Vector3.MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)",
                        Notes = "Запрошено Кириллом.", Mvp = true
                    }
                }
            },

            // ───────────────────────── Quaternion ─────────────────────────────────
            new UnityClassInfo
            {
                ClassName = "Quaternion",
                DisplayName = "Quaternion",
                Category = "Векторы",
                Fields =
                {
                    new UnityMemberInfo { Name = "identity", Kind = UnityMemberKind.Field, ReturnType = "Quaternion", IsStatic = true, Signature = "Quaternion.identity", Mvp = true }
                },
                Methods =
                {
                    new UnityMemberInfo
                    {
                        Name = "Euler", Kind = UnityMemberKind.Method, ReturnType = "Quaternion", IsStatic = true,
                        Parameters =
                        {
                            new UnityParamInfo { Name = "x", Type = "float" },
                            new UnityParamInfo { Name = "y", Type = "float" },
                            new UnityParamInfo { Name = "z", Type = "float" }
                        },
                        Signature = "Quaternion.Euler(float x, float y, float z)", Mvp = true
                    }
                }
            },

            // ───────────────────────── Collider2D (Физика) ────────────────────────
            new UnityClassInfo
            {
                ClassName = "Collider2D",
                DisplayName = "Collider2D",
                Category = "Физика",
                // Члены добавляются по мере необходимости; сейчас достаточно регистрации типа
                Fields = { },
                Methods = { }
            },

            // ───────────────────────── Object (Создание/удаление) ─────────────────
            new UnityClassInfo
            {
                ClassName = "Object",
                DisplayName = "Object",
                Category = "Создание/удаление",
                Methods =
                {
                    new UnityMemberInfo
                    {
                        Name = "Instantiate", Kind = UnityMemberKind.Method, ReturnType = "GameObject", IsStatic = true,
                        Parameters = { new UnityParamInfo { Name = "original", Type = "GameObject" } },
                        Signature = "Object.Instantiate(GameObject original)", Mvp = true
                    },
                    new UnityMemberInfo
                    {
                        Name = "Instantiate", Kind = UnityMemberKind.Method, ReturnType = "GameObject", IsStatic = true,
                        Parameters =
                        {
                            new UnityParamInfo { Name = "original",  Type = "GameObject" },
                            new UnityParamInfo { Name = "position",  Type = "Vector3"    },
                            new UnityParamInfo { Name = "rotation",  Type = "Quaternion" }
                        },
                        Signature = "Object.Instantiate(GameObject original, Vector3 position, Quaternion rotation)", Mvp = true
                    },
                    new UnityMemberInfo
                    {
                        Name = "Destroy", Kind = UnityMemberKind.Method, ReturnType = "void", IsStatic = true,
                        Parameters = { new UnityParamInfo { Name = "obj", Type = "GameObject" } },
                        Signature = "Object.Destroy(GameObject obj)", Mvp = true
                    }
                }
            },

            // ───────────────────────── GameObject ──────────────────────────────────
            new UnityClassInfo
            {
                ClassName = "GameObject",
                DisplayName = "GameObject",
                Category = "Доступ к компонентам / Состояние объекта",
                Methods =
                {
                    new UnityMemberInfo
                    {
                        Name = "GetComponent", Kind = UnityMemberKind.Method, ReturnType = "Component", IsStatic = false,
                        Signature = "gameObject.GetComponent<T>()",
                        Notes = "Generic-метод — тип T выбирается в инспекторе ноды.", Mvp = true
                    },
                    new UnityMemberInfo
                    {
                        Name = "SetActive", Kind = UnityMemberKind.Method, ReturnType = "void", IsStatic = false,
                        Parameters = { new UnityParamInfo { Name = "value", Type = "bool" } },
                        Signature = "gameObject.SetActive(bool value)", Mvp = true
                    }
                }
            },

            // ───────────────────────── Transform (Позиционирование) ────────────────
            new UnityClassInfo
            {
                ClassName = "Transform",
                DisplayName = "Transform",
                Category = "Позиционирование",
                Fields =
                {
                    new UnityMemberInfo { Name = "position", Kind = UnityMemberKind.Property, ReturnType = "Vector3", IsStatic = false, Signature = "transform.position", Mvp = true }
                },
                Methods =
                {
                    new UnityMemberInfo
                    {
                        Name = "Translate", Kind = UnityMemberKind.Method, ReturnType = "void", IsStatic = false,
                        Parameters = { new UnityParamInfo { Name = "translation", Type = "Vector3" } },
                        Signature = "transform.Translate(Vector3 translation)", Mvp = true
                    },
                    new UnityMemberInfo
                    {
                        Name = "SetParent", Kind = UnityMemberKind.Method, ReturnType = "void", IsStatic = false,
                        Parameters = { new UnityParamInfo { Name = "parent", Type = "Transform" } },
                        Signature = "transform.SetParent(Transform parent)",
                        Notes = "Доп. метод из раздела «Дополнительные важные методы».", Mvp = false
                    }
                }
            },

            // ───────────────────────── Input (Управление) ──────────────────────────
            new UnityClassInfo
            {
                ClassName = "Input",
                DisplayName = "Input",
                Category = "Управление",
                Methods =
                {
                    new UnityMemberInfo
                    {
                        Name = "GetAxis", Kind = UnityMemberKind.Method, ReturnType = "float", IsStatic = true,
                        Parameters = { new UnityParamInfo { Name = "axisName", Type = "string" } },
                        Signature = "Input.GetAxis(string axisName)", Mvp = true
                    },
                    new UnityMemberInfo
                    {
                        Name = "GetKeyDown", Kind = UnityMemberKind.Method, ReturnType = "bool", IsStatic = true,
                        Parameters = { new UnityParamInfo { Name = "key", Type = "string" } },
                        Signature = "Input.GetKeyDown(KeyCode key)",
                        Notes = "KeyCode представлен строкой-именем клавиши в UI.", Mvp = true
                    }
                }
            },

            // 