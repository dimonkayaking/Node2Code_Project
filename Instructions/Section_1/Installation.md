# 1. Установка и запуск плагина Node2Code

Добро пожаловать в Node2Code! Этот плагин позволяет создавать C# код для Unity двумя способами: классическим текстом во встроенном редакторе или с помощью визуального графа из блоков-нод.

---

## **Инструкция по установке плагина**

### **Шаг 1: Импорт плагина**

1. **Скопируйте файл** `Node2Code.unitypackage` в удобное место (например, на рабочий стол)

2. **Откройте Unity проект**, в который хотите установить плагин

3. **Импортируйте плагин**:
   - В меню Unity выберите: **Assets → Import Package → Custom Package...**
   - Или **нажмите правой кнопкой мыши** в окне Project → **Import Package → Custom Package...**
   - Выберите файл `Node2Code.unitypackage`
   - В открывшемся окне нажмите **Import**

---

### **Шаг 2: Установка GraphProcessor через Package Manager**

1. В Unity откройте: **Window → Package Manager**

2. Нажмите кнопку **+** (плюс) в левом верхнем углу

3. Выберите **Add package from git URL...**

4. Вставьте ссылку:
   ```
   https://github.com/Warwlock/NodeGraphProcessor.git
   ```

5. Нажмите **Instal**

---

### **Шаг 3: Установка Newtonsoft.Json через Package Manager**

1. В Package Manager снова нажмите **+**

2. Выберите **Add package by name...**

3. Введите:
   ```
   com.unity.nuget.newtonsoft-json
   ```

4. Нажмите **Instal**

---
![Import window](Import.png)

---

## Запуск плагина

После установки в верхнем меню Unity появится пункт **Tools → Node2Code**.

1. Нажмите **Tools → Node2Code**, чтобы открыть рабочее окно плагина.
2. Вы можете закрепить его в любом удобном месте интерфейса Unity (например, рядом с окном Scene или Inspector), просто перетащив вкладку мышью.

 При первом запуске плагин автоматически создаёт стартовую структуру: класс `Program` и статический метод `Main()`.

![Interface](Interface.png)
