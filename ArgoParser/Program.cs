using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Collections.Generic;

namespace ArgoParser
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // Режим работы: файл или папка
            string inputPath = args.Length > 0 ? args[0] : @"D:\Desktop\АРГО-ЖБ. Тест\ПОПОЛНЕННЫЙ БАНК";
            string outputPath = args.Length > 1 ? args[1] : null;

            if (Directory.Exists(inputPath))
            {
                ProcessDirectory(inputPath, outputPath);
            }
            else if (File.Exists(inputPath))
            {
                ProcessFile(inputPath, outputPath);
            }
            else
            {
                Console.WriteLine($"Путь не найден: {inputPath}");
            }

            Console.WriteLine("\nНажмите Enter...");
            Console.ReadLine();
        }

        static void ProcessDirectory(string dirPath, string outputDir)
        {
            Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║  ПАКЕТНАЯ ОБРАБОТКА ФАЙЛОВ АРГО                              ║");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"Папка: {dirPath}\n");

            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.Combine(dirPath, "JSON");
            }
            Directory.CreateDirectory(outputDir);

            // Ищем все файлы кроме служебных
            var skipExtensions = new[] { ".json", ".txt", ".doc", ".docx", ".pdf", ".exe", ".dll" };
            var files = Directory.GetFiles(dirPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => !skipExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f => f)
                .ToArray();

            Console.WriteLine($"Найдено файлов: {files.Length}\n");

            int success = 0, failed = 0;
            var errors = new List<string>();

            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                Console.Write($"  {fileName,-45} ");

                try
                {
                    var parser = new ArgoParser();
                    var doc = parser.Parse(file);

                    // Сохраняем с полным именем файла (заменяем точки на подчёркивания кроме расширения .json)
                    string safeName = fileName.Replace(".", "_");
                    string jsonPath = Path.Combine(outputDir, safeName + ".json");
                    SaveToJson(doc, jsonPath);

                    Console.WriteLine($"✓ {doc.GlobalParams.BeamCount} балок");
                    success++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ {ex.Message.Split('\n')[0]}");
                    errors.Add($"{fileName}: {ex.Message.Split('\n')[0]}");
                    failed++;
                }
            }

            Console.WriteLine($"\n══════════════════════════════════════════════════════════════");
            Console.WriteLine($"ИТОГО: {success} успешно, {failed} ошибок из {files.Length} файлов");
            Console.WriteLine($"JSON сохранены в: {outputDir}");

            if (errors.Count > 0 && errors.Count <= 20)
            {
                Console.WriteLine($"\nОШИБКИ:");
                foreach (var err in errors)
                {
                    Console.WriteLine($"  • {err}");
                }
            }
        }

        static void ProcessFile(string filePath, string outputPath)
        {
            Console.WriteLine($"Парсинг: {filePath}");
            Console.WriteLine(new string('=', 60));

            var parser = new ArgoParser();
            var doc = parser.Parse(filePath);

            PrintResult(doc);

            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = Path.ChangeExtension(filePath, ".json");
            }
            SaveToJson(doc, outputPath);
            Console.WriteLine($"\n✓ JSON: {outputPath}");
        }

        static void SaveToJson(ArgoDocument doc, string path)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string json = JsonSerializer.Serialize(doc, options);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        static void PrintResult(ArgoDocument doc)
        {
            Console.WriteLine($"\nРЕЗУЛЬТАТ:");
            Console.WriteLine($"  Комментарий: {string.Join(" | ", doc.Header.Comments)}");
            Console.WriteLine($"  Бетон: {doc.GlobalParams.ConcreteStrength}, Длина: {doc.GlobalParams.FullLength}");
            Console.WriteLine($"  Балок: {doc.GlobalParams.BeamCount}");

            foreach (var beam in doc.Beams)
            {
                Console.WriteLine($"  Балка #{beam.Number}: плита={beam.SlabReinforcement?.CalculatedBarsCount ?? 0}, " +
                    $"растян={beam.TensileBars.Count}, сжат={beam.CompressedBars.Count}, " +
                    $"отгибы={beam.Bends.Count}, хомуты={beam.StirrupSections.Count}");
            }
        }
    }
}