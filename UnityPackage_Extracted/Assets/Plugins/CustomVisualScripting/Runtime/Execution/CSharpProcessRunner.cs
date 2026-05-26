using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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

{debugShim}";
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

{debugShim}";
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
