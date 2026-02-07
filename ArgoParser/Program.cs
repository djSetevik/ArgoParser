using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace ArgoParser
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  КОНВЕРТЕР АРГО → PRSSM                                      ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Запрос пути
            Console.Write("Введите путь к файлу или папке АРГО: ");
            string inputPath = Console.ReadLine()?.Trim().Trim('"') ?? "";

            if (string.IsNullOrEmpty(inputPath))
            {
                Console.WriteLine("Путь не указан!");
                WaitForExit();
                return;
            }

            if (Directory.Exists(inputPath))
            {
                ProcessDirectory(inputPath);
            }
            else if (File.Exists(inputPath))
            {
                ProcessFile(inputPath);
            }
            else
            {
                Console.WriteLine($"Путь не найден: {inputPath}");
            }

            WaitForExit();
        }

        static void WaitForExit()
        {
            Console.WriteLine("\nНажмите Enter для выхода...");
            Console.ReadLine();
        }

        static void ProcessDirectory(string dirPath)
        {
            Console.WriteLine($"\nПапка: {dirPath}");

            string outputDir = Path.Combine(dirPath, "PRSSM");
            Directory.CreateDirectory(outputDir);

            // Фильтр файлов АРГО
            var files = Directory.GetFiles(dirPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => IsArgoFile(f))
                .OrderBy(f => f)
                .ToArray();

            Console.WriteLine($"Найдено файлов АРГО: {files.Length}\n");

            int success = 0, failed = 0;
            var errors = new List<string>();

            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                Console.Write($"  {fileName,-40} ");

                try
                {
                    var parser = new ArgoParser();
                    var doc = parser.Parse(file);

                    var converter = new ArgoToPrssmConverter();
                    var prssmDoc = converter.Convert(doc);

                    string outName = fileName.Replace(".", "_") + ".prssm";
                    string outPath = Path.Combine(outputDir, outName);

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    };
                    string json = JsonSerializer.Serialize(prssmDoc, options);
                    File.WriteAllText(outPath, json);

                    Console.WriteLine($"✓ {outName}");
                    success++;
                }
                catch (Exception ex)
                {
                    string errMsg = ex.Message.Length > 50 ? ex.Message.Substring(0, 50) + "..." : ex.Message;
                    Console.WriteLine($"✗ {errMsg}");
                    errors.Add($"{fileName}: {ex.Message}");
                    failed++;
                }
            }

            Console.WriteLine($"\n{'═',60}");
            Console.WriteLine($"ИТОГО: {success} успешно, {failed} ошибок из {files.Length} файлов");
            Console.WriteLine($"Результаты в: {outputDir}");

            if (errors.Count > 0 && errors.Count <= 10)
            {
                Console.WriteLine("\nОШИБКИ:");
                foreach (var err in errors)
                    Console.WriteLine($"  • {err}");
            }
        }

        static void ProcessFile(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            Console.WriteLine($"\nФайл: {fileName}");

            try
            {
                var parser = new ArgoParser();
                var doc = parser.Parse(filePath);

                Console.WriteLine($"\n--- Результат парсинга ---");
                Console.WriteLine($"  Бетон: {doc.GlobalParams.ConcreteStrength} МПа");
                Console.WriteLine($"  Длина: {doc.GlobalParams.FullLength} см");
                Console.WriteLine($"  Балок: {doc.GlobalParams.BeamCount}");

                foreach (var beam in doc.Beams)
                {
                    Console.WriteLine($"  Балка #{beam.Number}: контур={beam.CrossSectionContour.Count} точек, " +
                        $"растян={beam.TensileBars.Count}, сжат={beam.CompressedBars.Count}, " +
                        $"отгибы={beam.Bends.Count}, хомуты={beam.StirrupSections.Count}");
                }

                // Конвертация
                Console.WriteLine($"\n--- Конвертация в PRSSM ---");
                var converter = new ArgoToPrssmConverter();
                var prssmDoc = converter.Convert(doc);

                string dir = Path.GetDirectoryName(filePath) ?? "";
                string outName = Path.GetFileName(filePath).Replace(".", "_") + ".prssm";
                string outPath = Path.Combine(dir, outName);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                string json = JsonSerializer.Serialize(prssmDoc, options);
                File.WriteAllText(outPath, json);

                Console.WriteLine($"✓ Сохранено: {outPath}");

                // Краткая информация о результате
                foreach (var beam in prssmDoc.Beams)
                {
                    var part = beam.BeamParts.FirstOrDefault();
                    if (part != null)
                    {
                        Console.WriteLine($"  Балка #{beam.Id}: A={part.Section.Area:F0} мм², " +
                            $"арматура прод={beam.ReinforcementLongitudinals?.Count ?? 0}, " +
                            $"попер={beam.ReinforcementTransverses?.Count ?? 0}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n✗ ОШИБКА: {ex.Message}");
                Console.WriteLine($"\nСтек вызовов:\n{ex.StackTrace}");
            }
        }

        static bool IsArgoFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string name = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();

            // Пропускаем служебные расширения
            var skipExtensions = new[] {
                ".json", ".prssm", ".txt", ".doc", ".docx", ".pdf", ".exe", ".dll",
                ".hex", ".bat", ".cmd", ".html", ".htm", ".js", ".css", ".lic", ".sys",
                ".aal", ".log", ".tmp", ".bak", ".xml", ".ini", ".cfg", ".gru"
            };

            if (skipExtensions.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Расширения .01-.99 или с буквой (.02p, .05d)
            if (ext.Length >= 2 && ext.Length <= 4)
            {
                string extPart = ext.Substring(1);
                if (extPart.Length >= 2 && char.IsDigit(extPart[0]) && char.IsDigit(extPart[1]))
                    return true;
            }

            // Имя файла начинается с A0-A9, B0-B9, N0-N9, S0-S9, I0-I9
            if (name.Length >= 2 && "ABNSI".Contains(name[0]) && char.IsDigit(name[1]))
                return true;

            // .dat файлы могут быть АРГО
            if (ext == ".dat")
                return true;

            return false;
        }
    }
}
