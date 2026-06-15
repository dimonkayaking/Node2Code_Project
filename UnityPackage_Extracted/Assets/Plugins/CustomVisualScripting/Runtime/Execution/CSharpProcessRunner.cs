using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace CustomVisualScripting.Runtime.Execution
{
    public sealed class CSharpProcessRunner : IDisposable
    {
        private Process _process;
        private string _workDir;
        private bool _disposed;

        public event Action<string, LogType> OnOutput;

        public bool IsRunning => _process != null && !_process.HasExited;

        public async Task<int> RunAsync(string userCode)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("C# процесс уже запущен.");
            }

            PrepareWorkspace(userCode ?? string.Empty);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "run --project Runner.csproj",
                WorkingDirectory = _workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;

            if (!_process.Start())
            {
                throw new InvalidOperationException("Не удалось запустить dotnet процесс.");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            while (!_process.HasExited)
            {
                await Task.Delay(50);
            }
            var exitCode = _process.ExitCode;
            if (exitCode != 0)
            {
                OnOutput?.Invoke($"[CSharpRunner] Процесс завершился с кодом {exitCode}.", LogType.Error);
            }
            CleanupWorkspace();
            return exitCode;
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            try
            {
                _process.Kill();
                OnOutput?.Invoke("[CSharpRunner] Выполнение принудительно остановлено.", LogType.Warning);
            }
            catch (Exception e)
            {
                OnOutput?.Invoke($"[CSharpRunner] Ошибка остановки: {e.Message}", LogType.Error);
            }
            finally
            {
                CleanupWorkspace();
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                OnOutput?.Invoke(e.Data, LogType.Log);
            }
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                OnOutput?.Invoke(e.Data, LogType.Error);
            }
        }

        private void PrepareWorkspace(string userCode)
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "CustomVisualScriptingRunner");
            Directory.CreateDirectory(baseDir);

            _workDir = Path.Combine(baseDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workDir);

            File.WriteAllText(Path.Combine(_workDir, "Runner.csproj"), BuildProjectFile(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(_workDir, "Program.cs"), BuildProgram(userCode), Encoding.UTF8);
        }

        private static string BuildProjectFile()
        {
            return @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";
        }

        private static string BuildProgram(string userCode)
        {
            // Шим Debug-класса — один и тот же для обоих вариантов обёртки.
            var debugShim = $@"// Шим UnityEngine.Debug для автономного C# runner'а: нода Debug.Log генерирует
// вызов Debug.Log(...), и чтобы этот код собирался вне Unity — даём локальный тип.
// Сообщения помечаются маркерами, чтобы редактор Unity перехватил их и сдублировал
// в Unity-консоль через UnityEngine.Debug.Log/LogWarning/LogError.
internal static class Debug
{{
    private const string LogMarker = ""{UnityDebugLogMarker}"";
    private const string WarningMarker = ""{UnityDebugLogWarningMarker}"";
    private const string ErrorMarker = ""{UnityDebugLogErrorMarker}"";

    public static void Log(object message) => Console.WriteLine($""{{LogMarker}}{{message}}"");
    public static void LogWarning(object message) => Console.WriteLine($""{{WarningMarker}}{{message}}"");
    public static void LogError(object message) => Console.Error.WriteLine($""{{ErrorMarker}}{{message}}"");
}}";

            // Шим минимального набора типов UnityEngine (Vector3, Mathf, Transform, GameObject, ...) —
            // позволяет сгенерированному коду с Unity-вызовами компилироваться и выполняться вне Unity.
            var unityShim = BuildUnityShim();

            // Если GenerateWithMethods уже создал class Program — используем его напрямую.
            // UTF8-кодировка инициализируется через [ModuleInitializer] до запуска Main.
            if ((userCode ?? "").TrimStart().StartsWith("class Program"))
            {
                return $@"using System;
using System.Text;
using System.Runtime.CompilerServices;

{userCode}

// Автоматически вызывается средой выполнения до Program.Main().
internal static class __VsRunnerInit
{{
    [ModuleInitializer]
    internal static void Init()
    {{
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
    }}
}}

{debugShim}

{unityShim}";
            }

            // ООП-режим (GenerateWithClasses): код — это одно или несколько объявлений
            // class XYZ { ... }, без своего Main. Подставляем драйвер class Program,
            // который создаёт экземпляры классов и по очереди вызывает их публичные
            // методы без параметров (как кнопка "Run" для тестового метода/класса).
            if (Regex.IsMatch((userCode ?? "").TrimStart(), @"^class\s+\w+"))
            {
                var entryCalls = BuildEntryCalls(userCode ?? "");
                return $@"using System;
using System.Text;
using System.Runtime.CompilerServices;

{userCode}

internal static class Program
{{
    private static void Main()
    {{
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        try
        {{
{Indent(entryCalls, 3)}
        }}
        catch (Exception ex)
        {{
            Console.Error.WriteLine($""[RuntimeError] {{ex.Message}}"");
            Console.Error.WriteLine(ex.StackTrace);
        }}
    }}
}}

{debugShim}

{unityShim}";
            }

            // Плоский код (нет пользовательских методов) — оборачиваем в Main как прежде.
            return $@"using System;
using System.Text;

internal static class Program
{{
    private static void Main(string[] args)
    {{
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        try
        {{
{Indent(userCode, 3)}
        }}
        catch (Exception ex)
        {{
            Console.Error.WriteLine($""[RuntimeError] {{ex.Message}}"");
            Console.Error.WriteLine(ex.StackTrace);
        }}
    }}
}}

{debugShim}

{unityShim}";
        }

        /// <summary>
        /// Строит вызовы публичных методов без параметров для драйвера Main() в ООП-режиме
        /// (<see cref="BuildProgram"/>, ветка "class XYZ {{ ... }}"). Идёт по тексту сгенерированного
        /// кода по порядку: при встрече "class X" запоминает текущий класс, при встрече
        /// "public [static] T Method()" — добавляет вызов (статический — "X.Method();",
        /// инстансный — через один общий экземпляр "__xInstance.Method();", создаваемый при
        /// первом обращении). Конструкторы (метод с именем класса) и Main пропускаются.
        /// </summary>
        private static string BuildEntryCalls(string userCode)
        {
            var sb = new StringBuilder();
            var instanceVars = new System.Collections.Generic.Dictionary<string, string>();

            var pattern = new Regex(
                @"(?m)^class\s+(\w+)(?:\s*:\s*\w+)?\s*$" +
                @"|^[ \t]*public\s+(static\s+)?[\w<>\[\],\.]+\s+(\w+)\s*\(\s*\)\s*(?:\r?\n\s*)?\{");

            string currentClass = null;
            foreach (Match m in pattern.Matches(userCode))
            {
                if (m.Groups[1].Success)
                {
                    currentClass = m.Groups[1].Value;
                    continue;
                }

                if (currentClass == null) continue;

                var isStatic   = m.Groups[2].Success;
                var methodName = m.Groups[3].Value;
                if (methodName == currentClass || methodName == "Main") continue;

                if (isStatic)
                {
                    sb.AppendLine($"{currentClass}.{methodName}();");
                }
                else
                {
                    if (!instanceVars.TryGetValue(currentClass, out var varName))
                    {
                        varName = "__" + currentClass.Substring(0, 1).ToLowerInvariant() + currentClass.Substring(1) + "Instance";
                        sb.AppendLine($"var {varName} = new {currentClass}();");
                        instanceVars[currentClass] = varName;
                    }
                    sb.AppendLine($"{varName}.{methodName}();");
                }
            }

            if (sb.Length == 0)
                sb.AppendLine("// Не найдено публичных методов без параметров для вызова.");

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Шим минимального набора типов UnityEngine (Vector3, Mathf, Transform, GameObject, Object,
        /// Input, Time, Random), достаточного для компиляции и выполнения кода, сгенерированного
        /// из Unity-нод (UnityVector3, UnityMethodCall, UnityFieldAccess, UnityFieldSet), вне Unity.
        /// </summary>
        private static string BuildUnityShim()
        {
            return @"// Шим минимального набора типов UnityEngine для автономного запуска вне Unity.
public struct Vector3
{
    public float x, y, z;

    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

    public static Vector3 zero    => new Vector3(0f, 0f, 0f);
    public static Vector3 up      => new Vector3(0f, 1f, 0f);
    public static Vector3 down    => new Vector3(0f, -1f, 0f);
    public static Vector3 left    => new Vector3(-1f, 0f, 0f);
    public static Vector3 right   => new Vector3(1f, 0f, 0f);
    public static Vector3 forward => new Vector3(0f, 0f, 1f);
    public static Vector3 back    => new Vector3(0f, 0f, -1f);

    public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
    public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);

    public float magnitude => (float)System.Math.Sqrt(x * x + y * y + z * z);

    public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        t = Mathf.Clamp01(t);
        return a + (b - a) * t;
    }

    public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;

    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)
    {
        var diff = target - current;
        var dist = diff.magnitude;
        if (dist <= maxDistanceDelta || dist == 0f)
            return target;
        return current + diff * (maxDistanceDelta / dist);
    }

    public override string ToString() => $""({x}, {y}, {z})"";
}

internal static class Mathf
{
    public const float PI = 3.14159274f;

    public static float Abs(float f) => System.Math.Abs(f);
    public static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
    public static float Clamp01(float value) => Clamp(value, 0f, 1f);
    public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    public static float Pow(float f, float p) => (float)System.Math.Pow(f, p);
    public static float Max(float a, float b) => System.Math.Max(a, b);
    public static float Min(float a, float b) => System.Math.Min(a, b);
    public static float Sqrt(float f) => (float)System.Math.Sqrt(f);
}

internal class Transform
{
    public Vector3 position = Vector3.zero;

    public void Translate(Vector3 translation) => position += translation;
    public void SetParent(Transform parent) { }
}

internal class GameObject
{
    public Transform transform = new Transform();

    public T GetComponent<T>() => default!;
    public void SetActive(bool value) { }
}

internal static class Object
{
    public static GameObject Instantiate(GameObject original) => original;
    public static void Destroy(GameObject obj) { }
}

internal static class Input
{
    public static float GetAxis(string axisName) => 0f;
    public static bool GetKeyDown(string key) => false;
}

internal static class Time
{
    public static float deltaTime => 0.016f;
}

internal static class Random
{
    private static readonly System.Random _rng = new System.Random();
    public static float Range(float min, float max) => (float)(min + _rng.NextDouble() * (max - min));
}";
        }

        /// <summary>Маркер начала строки stdout, помечающий вызов Debug.Log в сгенерированном коде.</summary>
        public const string UnityDebugLogMarker = "__VS_UNITY_DEBUG_LOG__::";

        /// <summary>Маркер Debug.LogWarning.</summary>
        public const string UnityDebugLogWarningMarker = "__VS_UNITY_DEBUG_WARNING__::";

        /// <summary>Маркер Debug.LogError.</summary>
        public const string UnityDebugLogErrorMarker = "__VS_UNITY_DEBUG_ERROR__::";

        private static string Indent(string text, int indentLevel)
        {
            var indent = new string(' ', indentLevel * 4);
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                if (line.Length == 0)
                {
                    sb.AppendLine();
                    continue;
                }
                sb.Append(indent).AppendLine(line);
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                Stop();
            }
            catch
            {
                // Ignore dispose cleanup errors.
            }

            if (_process != null)
            {
                _process.OutputDataReceived -= OnOutputDataReceived;
                _process.ErrorDataReceived -= OnErrorDataReceived;
                _process.Dispose();
                _process = null;
            }

            CleanupWorkspace();
        }

        private void CleanupWorkspace()
        {
            if (string.IsNullOrEmpty(_workDir) || !Directory.Exists(_workDir))
            {
                return;
            }

            try
            {
                Directory.Delete(_workDir, recursive: true);
                _workDir = null;
            }
            catch
            {
                // Ignore temp workspace cleanup errors.
            }
        }
    }
}
