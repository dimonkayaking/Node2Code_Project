using System;
using System.Collections.Generic;
using System.Linq;
using GraphProcessor;
using UnityEngine;
using UnityEngine.UIElements;
using CustomVisualScripting.Editor.Classes;
using CustomVisualScripting.Editor.Methods;
using CustomVisualScripting.Editor.Nodes.Methods;
using CustomVisualScripting.Editor.Windows;

namespace CustomVisualScripting.Editor.Nodes.Views
{
    /// <summary>
    /// Отображение <see cref="ClassNode"/>: зелёный заголовок + панель полей и методов.
    ///
    /// Пользователь видит все поля (type name) и все методы класса прямо на ноде.
    /// Кнопки: [+ Поле], [+ Метод], [✎ переименовать], [✕ удалить поле], [✎ открыть метод].
    /// </summary>
    [NodeCustomEditor(typeof(ClassNode))]
    public class ClassNodeView : BaseNodeView
    {
        private static readonly Color ClassHeaderColor  = new Color(0.10f, 0.42f, 0.20f, 0.80f);
        private static readonly Color SectionColor      = new Color(0.28f, 0.28f, 0.28f, 1f);
        private static readonly Color FieldTypeColor    = new Color(0.55f, 0.82f, 1.00f);
        private static readonly Color FieldNameColor    = new Color(0.92f, 0.92f, 0.92f);
        private static readonly Color MethodSigColor    = new Color(0.75f, 1.00f, 0.82f);
        private static readonly Color RemoveBtnColor    = new Color(1.00f, 0.40f, 0.40f);
        private static readonly Color InheritanceColor  = new Color(1.00f, 0.85f, 0.40f);

        private ClassNode _node;

        // ─── Enable / Disable ─────────────────────────────────────────────────

        public override void Enable()
        {
            base.Enable();
            _node = nodeTarget as ClassNode;
            if (_node == null) return;

            // Автосоздание ClassDefinition при добавлении ноды через контекстное меню
            if (string.IsNullOrEmpty(_node.ClassId))
                AutoCreateClass();

            _node.RefreshFromRegistry();

            SetupHeader();
            expanded = true;
            BuildContent();

            ClassRegistry.OnChanged  += OnAnyRegistryChanged;
            MethodRegistry.OnChanged += OnAnyRegistryChanged;
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ClassRegistry.OnChanged  -= OnAnyRegistryChanged;
                MethodRegistry.OnChanged -= OnAnyRegistryChanged;
            });
        }

        // ─── Header ──────────────────────────────────────────────────────────

        private void SetupHeader()
        {
            title = string.IsNullOrWhiteSpace(_node.ClassName) ? "Class" : _node.ClassName;
            titleContainer.style.backgroundColor = ClassHeaderColor;

            // Кнопка переименования
            var renameBtn = new Button(OnRenameClicked) { text = "✎" };
            renameBtn.AddToClassList("node-subspace-link");
            renameBtn.style.marginLeft  = 4;
            renameBtn.style.marginRight = 2;
            titleContainer.Add(renameBtn);
        }

        // ─── Content ─────────────────────────────────────────────────────────

        // ─── Публичный метод принудительного обновления ─────────────────────────
        // Вызывается напрямую из VisualScriptingWindow при изменении реестров.

        public void RefreshContent()
        {
            if (_node == null) return;
            extensionContainer.Clear();
            BuildContent();
            expanded = true;

            var def = ClassRegistry.GetById(_node.ClassId);
            if (def != null)
            {
                _node.ClassName = def.Name;
                title = def.Name;
            }
        }

        private void OnAnyRegistryChanged() => RefreshContent();

        private void BuildContent()
        {
            style.minWidth = 240f;

            var def = ClassRegistry.GetById(_node.ClassId);
            var fields  = def?.Fields ?? new List<FieldDefinition>();
            var methods = MethodRegistry.Methods
                .Where(m => string.Equals(m.ClassId, _node.ClassId, StringComparison.Ordinal))
                .ToList();

            // ── Наследование ─────────────────────────────────────────────────
            extensionContainer.Add(MakeSectionHeader("Наследование", InheritanceColor));
            extensionContainer.Add(MakeInheritanceRow(def));

            // ── Поля ─────────────────────────────────────────────────────────
            extensionContainer.Add(MakeSectionHeader("Поля", FieldTypeColor));

            foreach (var field in fields)
                extensionContainer.Add(MakeFieldRow(field));

            var addFieldBtn = MakeAddButton("+ Поле", OnAddFieldClicked);
            extensionContainer.Add(addFieldBtn);

            // ── Методы ───────────────────────────────────────────────────────
            extensionContainer.Add(MakeSectionHeader("Методы", MethodSigColor));

            foreach (var method in methods)
                extensionContainer.Add(MakeMethodRow(method));

            var addMethodBtn = MakeAddButton("+ Метод", OnAddMethodClicked);
            extensionContainer.Add(addMethodBtn);
        }

        // ─── Row builders ────────────────────────────────────────────────────

        /// <summary>
        /// Строит строку наследования: "extends ParentName [✎] [✕]"
        /// или кнопку "Установить родителя" если родителя нет.
        /// </summary>
        private VisualElement MakeInheritanceRow(ClassDefinition def)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.paddingLeft   = 8;
            row.style.paddingRight  = 4;
            row.style.paddingTop    = 3;
            row.style.paddingBottom = 3;

            var parentDef = string.IsNullOrEmpty(def?.BaseClassId)
                ? null
                : ClassRegistry.GetById(def.BaseClassId);

            if (parentDef != null)
            {
                // Пользовательский родительский класс
                var label = new Label($"extends  {parentDef.Name}");
                label.style.color                   = InheritanceColor;
                label.style.fontSize                = 10;
                label.style.unityFontStyleAndWeight = FontStyle.Italic;
                label.style.flexGrow                = 1;

                var editBtn = new Button(OnChangeParentClicked) { text = "✎" };
                editBtn.style.fontSize   = 9;
                editBtn.style.width      = 20;
                editBtn.style.height     = 18;
                editBtn.style.marginLeft = 2;

                var clearBtn = new Button(OnClearParentClicked) { text = "✕" };
                clearBtn.style.fontSize = 9;
                clearBtn.style.color    = RemoveBtnColor;
                clearBtn.style.width    = 20;
                clearBtn.style.height   = 18;

                row.Add(label);
                row.Add(editBtn);
                row.Add(clearBtn);
            }
            else if (def?.InheritsMonoBehaviour == true)
            {
                // MonoBehaviour (по умолчанию)
                var label = new Label("extends  MonoBehaviour");
                label.style.color                   = InheritanceColor;
                label.style.fontSize                = 10;
                label.style.unityFontStyleAndWeight = FontStyle.Italic;
                label.style.flexGrow                = 1;

                var editBtn = new Button(OnChangeParentClicked) { text = "✎" };
                editBtn.tooltip          = "Редактировать класс";
                editBtn.style.fontSize   = 9;
                editBtn.style.width      = 20;
                editBtn.style.height     = 18;
                editBtn.style.marginLeft = 2;

                var clearBtn = new Button(OnClearMonoBehaviourClicked) { text = "✕" };
                clearBtn.tooltip    = "Отключить наследование MonoBehaviour";
                clearBtn.style.fontSize = 9;
                clearBtn.style.color    = RemoveBtnColor;
                clearBtn.style.width    = 20;
                clearBtn.style.height   = 18;

                row.Add(label);
                row.Add(editBtn);
                row.Add(clearBtn);
            }
            else
            {
                // Нет родителя
                var setBtn = new Button(OnChangeParentClicked) { text = "+ Установить родителя" };
                setBtn.style.fontSize     = 10;
                setBtn.style.marginLeft   = 0;
                setBtn.style.marginRight  = 6;
                setBtn.style.marginTop    = 1;
                setBtn.style.marginBottom = 1;
                setBtn.style.flexGrow     = 1;
                row.Add(setBtn);
            }

            return row;
        }

        private static VisualElement MakeSectionHeader(string text, Color accent)
        {
            var el = new VisualElement();
            el.style.backgroundColor  = SectionColor;
            el.style.paddingLeft      = 6;
            el.style.paddingTop       = 3;
            el.style.paddingBottom    = 3;
            el.style.marginTop        = 4;

            var lbl = new Label(text);
            lbl.style.color                   = accent;
            lbl.style.fontSize                = 10;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            el.Add(lbl);
            return el;
        }

        private VisualElement MakeFieldRow(FieldDefinition field)
        {
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.alignItems     = Align.Center;
            row.style.paddingLeft    = 8;
            row.style.paddingRight   = 4;
            row.style.paddingTop     = 2;
            row.style.paddingBottom  = 2;

            var modifierStr = (field.IsPublic ? "+" : "-") + (field.IsStatic ? "S " : " ");
            var typeLabel = new Label(modifierStr + field.Type);
            typeLabel.tooltip        = $"{(field.IsPublic ? "public" : "private")}{(field.IsStatic ? " static" : "")} {field.Type}";
            typeLabel.style.color    = FieldTypeColor;
            typeLabel.style.fontSize = 10;
            typeLabel.style.minWidth = 56f;

            // Показываем "name = defaultValue" если задано начальное значение
            var displayName = string.IsNullOrEmpty(field.DefaultValue)
                ? field.Name
                : $"{field.Name} = {field.DefaultValue}";
            var nameLabel = new Label(displayName);
            nameLabel.style.color    = FieldNameColor;
            nameLabel.style.fontSize = 10;
            nameLabel.style.flexGrow = 1;

            // Кнопка редактирования поля
            var editBtn = new Button(() => OnEditFieldClicked(field)) { text = "✎" };
            editBtn.style.fontSize   = 9;
            editBtn.style.width      = 20;
            editBtn.style.height     = 18;
            editBtn.style.marginLeft = 2;

            // Кнопка удаления поля
            var removeBtn = new Button(() => OnRemoveFieldClicked(field.Id)) { text = "✕" };
            removeBtn.style.fontSize = 9;
            removeBtn.style.color    = RemoveBtnColor;
            removeBtn.style.width    = 20;
            removeBtn.style.height   = 18;

            row.Add(typeLabel);
            row.Add(nameLabel);
            row.Add(editBtn);
            row.Add(removeBtn);
            return row;
        }

        private VisualElement MakeMethodRow(MethodDefinition method)
        {
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.alignItems     = Align.Center;
            row.style.paddingLeft    = 8;
            row.style.paddingRight   = 4;
            row.style.paddingTop     = 2;
            row.style.paddingBottom  = 2;

            var sigLabel = new Label(method.Signature());
            sigLabel.style.color    = MethodSigColor;
            sigLabel.style.fontSize = 10;
            sigLabel.style.flexGrow = 1;

            var openBtn = new Button(() => OnOpenMethodClicked(method.Id)) { text = "✎" };
            openBtn.AddToClassList("node-subspace-link");
            openBtn.style.marginLeft = 4;

            row.Add(sigLabel);
            row.Add(openBtn);
            return row;
        }

        private static VisualElement MakeAddButton(string text, Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            btn.style.marginLeft   = 6;
            btn.style.marginRight  = 6;
            btn.style.marginTop    = 2;
            btn.style.marginBottom = 4;
            btn.style.fontSize     = 10;
            return btn;
        }

        // ─── Handlers ────────────────────────────────────────────────────────

        private void OnChangeParentClicked()
        {
            var def = ClassRegistry.GetById(_node.ClassId);
            if (def == null) return;
            CreateClassPopup.ShowEdit(def, updated => ClassRegistry.Update(updated));
        }

        private void OnClearParentClicked()
        {
            var def = ClassRegistry.GetById(_node.ClassId);
            if (def == null) return;
            def.BaseClassId = "";
            ClassRegistry.Update(def);
        }

        private void OnClearMonoBehaviourClicked()
        {
            var def = ClassRegistry.GetById(_node.ClassId);
            if (def == null) return;
            def.InheritsMonoBehaviour = false;
            ClassRegistry.Update(def);
        }

        private void OnRenameClicked()
        {
            var def = ClassRegistry.GetById(_node.ClassId);
            if (def == null) return;
            CreateClassPopup.ShowEdit(def, updated =>
            {
                ClassRegistry.Update(updated);
                VisualScriptingWindow.ActiveWindow?.RefreshClassTabTitle(updated.Id, updated.Name);
            });
        }

        private void OnAddFieldClicked()
        {
            FieldEditPopup.ShowCreate(_node.ClassId, newField =>
            {
                var def = ClassRegistry.GetById(_node.ClassId);
                if (def == null) return;
                def.Fields ??= new List<FieldDefinition>();
                def.Fields.Add(newField);
                ClassRegistry.Update(def);
            });
        }

        private void OnEditFieldClicked(FieldDefinition field)
        {
            var def = ClassRegistry.GetById(_node.ClassId);
            if (def == null) return;
            FieldEditPopup.ShowEdit(field, _ => ClassRegistry.Update(def));
        }

        private void OnRemoveFieldClicked(string fieldId)
        {
            var def = ClassRegistry.GetById(_node.ClassId);
            if (def == null) return;
            def.Fields?.RemoveAll(f => string.Equals(f.Id, fieldId, StringComparison.Ordinal));
            ClassRegistry.Update(def);
        }

        private void OnAddMethodClicked()
        {
            CreateMethodPopup.ShowCreate(newDef =>
            {
                // ClassId уже выставлен внутри попапа — пользователь выбирает класс
                MethodRegistry.Add(newDef);
            }, preselectedClassId: _node.ClassId);
        }

        private static void OnOpenMethodClicked(string methodId)
        {
            VisualScriptingWindow.ActiveWindow?.OpenMethodTab(methodId);
        }

        // ─── Автосоздание ClassDefinition ─────────────────────────────────

        private void AutoCreateClass()
        {
            var def = new ClassDefinition { Name = "MyClass" };
            ClassRegistry.Add(def);
            _node.ClassId   = def.Id;
            _node.ClassName = def.Name;
        }
    }
}
