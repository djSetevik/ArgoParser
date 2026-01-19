using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ArgoParser
{
    public class ArgoFileCode
    {
        [JsonPropertyName("ИмяФайла")]
        public string OriginalFileName { get; set; }

        [JsonPropertyName("КодНагрузки")]
        public char LoadCode { get; set; }

        [JsonPropertyName("НазваниеНагрузки")]
        public string LoadName { get; set; }

        [JsonPropertyName("ГодНагрузки")]
        public int? LoadYear { get; set; }

        [JsonPropertyName("КоличествоРёбер")]
        public int MainRibCount { get; set; }

        [JsonPropertyName("ПлитноеБезКонсолей")]
        public bool IsPlateWithoutConsoles => MainRibCount == 0;

        [JsonPropertyName("ДлинаПролёта")]
        public int SpanLength { get; set; }

        [JsonPropertyName("ПорядковыйНомер")]
        public int SerialNumber { get; set; }

        [JsonPropertyName("СуффиксТипа")]
        public char? TypeSuffix { get; set; }

        [JsonPropertyName("ОписаниеТипа")]
        public string TypeDescription { get; set; }

        public static ArgoFileCode Parse(string fileName)
        {
            var code = new ArgoFileCode { OriginalFileName = fileName };

            try
            {
                fileName = System.IO.Path.GetFileName(fileName);

                code.LoadCode = char.ToUpper(fileName[0]);
                switch (code.LoadCode)
                {
                    case 'A': code.LoadName = "1907"; code.LoadYear = 1907; break;
                    case 'B': code.LoadName = "1925"; code.LoadYear = 1925; break;
                    case 'N': code.LoadName = "1931"; code.LoadYear = 1931; break;
                    case 'S': code.LoadName = "1962"; code.LoadYear = 1962; break;
                    case 'I': code.LoadName = "Индивидуальное"; break;
                    default: code.LoadName = "Неизвестно"; break;
                }

                if (char.IsDigit(fileName[1]))
                    code.MainRibCount = fileName[1] - '0';

                int underscoreIdx = fileName.IndexOf('_');
                int dotIdx = fileName.IndexOf('.');
                if (underscoreIdx > 0 && dotIdx > underscoreIdx)
                {
                    string spanStr = fileName.Substring(underscoreIdx + 1, dotIdx - underscoreIdx - 1);
                    int.TryParse(spanStr, out int span);
                    code.SpanLength = span;
                }

                if (dotIdx > 0 && dotIdx < fileName.Length - 1)
                {
                    string afterDot = fileName.Substring(dotIdx + 1);
                    string numPart = "";
                    string letterPart = "";
                    foreach (char c in afterDot)
                    {
                        if (char.IsDigit(c)) numPart += c;
                        else if (char.IsLetter(c)) letterPart += c;
                    }

                    if (numPart.Length >= 2)
                    {
                        if (int.TryParse(numPart.Substring(0, 2), out int serial))
                            code.SerialNumber = serial;
                    }
                    else if (numPart.Length > 0)
                    {
                        if (int.TryParse(numPart, out int serial))
                            code.SerialNumber = serial;
                    }

                    if (letterPart.Length > 0)
                    {
                        code.TypeSuffix = char.ToLower(letterPart[letterPart.Length - 1]);
                        code.TypeDescription = GetTypeDescription(code.TypeSuffix.Value, code.MainRibCount);
                    }
                }
            }
            catch { }

            return code;
        }

        private static string GetTypeDescription(char suffix, int ribCount)
        {
            if (ribCount == 0)
            {
                switch (char.ToLower(suffix))
                {
                    case 'a': case 'b': case 'c': return "1 блок";
                    case 'd': case 'e': case 'f': return "2 блока";
                    case 'g': case 'h': case 'i': return "3 блока";
                    case 'j': case 'k': case 'l': return "4 блока";
                }
            }

            switch (char.ToLower(suffix))
            {
                case 'k': return "Короткие консоли";
                case 'd': return "Длинные консоли";
                case 'l': return "Левая длинная";
                case 'r': return "Правая длинная";
                case 'p': return "Симметричное";
                case 's': return "Стандартное";
                case 'z': return "Особое";
            }

            return suffix.ToString().ToUpper();
        }
    }

    public class ArgoDocument
    {
        [JsonPropertyName("ИсходныйФайл")]
        public string SourceFileName { get; set; }

        [JsonPropertyName("ШифрФайла")]
        public ArgoFileCode FileCode { get; set; }

        [JsonPropertyName("Заголовок")]
        public Header Header { get; set; }

        [JsonPropertyName("Параметры")]
        public GlobalParameters GlobalParams { get; set; }

        [JsonPropertyName("Балки")]
        public List<Beam> Beams { get; set; } = new List<Beam>();

        [JsonPropertyName("ЭкземпляровПечати")]
        public int PrintCopies { get; set; }

        [JsonPropertyName("Детализация")]
        public DetailedReinforcement DetailedReinforcement { get; set; }
    }

    public class Header
    {
        [JsonPropertyName("СтрокКомментариев")]
        public int CommentLineCount { get; set; }

        [JsonPropertyName("Комментарии")]
        public List<string> Comments { get; set; } = new List<string>();
    }

    public class GlobalParameters
    {
        [JsonPropertyName("УровеньПечати")]
        public double PrintLevel { get; set; }

        [JsonPropertyName("ПрочностьБетона")]
        public double ConcreteStrength { get; set; }

        // Типы арматуры разбиты на 4 отдельных поля
        [JsonPropertyName("ТипРастянутойАрматуры")]
        public double TensileReinforcementType { get; set; }

        [JsonPropertyName("ТипСжатойАрматуры")]
        public double CompressedReinforcementType { get; set; }

        [JsonPropertyName("ТипАрматурыПлиты")]
        public double SlabReinforcementType { get; set; }

        [JsonPropertyName("ТипХомутов")]
        public double StirrupType { get; set; }

        [JsonPropertyName("КоординатаОсиОпорнойЧасти1")]
        public double SupportAxis1 { get; set; }

        [JsonPropertyName("КоординатаОсиОпорнойЧасти2")]
        public double SupportAxis2 { get; set; }

        [JsonPropertyName("КоординатаВнутреннейГраниБалансираОЧ1")]
        public double InnerSupport1 { get; set; }

        [JsonPropertyName("КоординатаВнутреннейГраниБалансираОЧ2")]
        public double InnerSupport2 { get; set; }

        [JsonPropertyName("ПолнаяДлинаПС")]
        public double FullLength { get; set; }

        [JsonPropertyName("КоличествоБалок")]
        public int BeamCount { get; set; }

        [JsonPropertyName("КоординатыОсейБалок")]
        public List<double> BeamCoordinates { get; set; } = new List<double>();

        [JsonPropertyName("ВидБалласта")]
        public double BallastType { get; set; }

        [JsonPropertyName("ТипШпал")]
        public double SleeperType { get; set; }

        [JsonPropertyName("КоординатыОсиПути")]
        public List<double> TrackAxisZ { get; set; } = new List<double>();

        [JsonPropertyName("ПризнакНаличияДиафрагм")]
        public double DiaphragmPresence { get; set; }

        [JsonPropertyName("КонтурБалластнойПризмы")]
        public List<Point2D> BallastContour { get; set; } = new List<Point2D>();
    }

    public class Beam
    {
        [JsonPropertyName("Номер")]
        public int Number { get; set; }

        [JsonPropertyName("ИнтенсивностьНагрузкиОтТротуара")]
        public double SidewalkLoadIntensity { get; set; }

        [JsonPropertyName("КоординатаНагрузкиОтТротуара")]
        public double SidewalkLoadCoordinate { get; set; }

        [JsonPropertyName("ИнтенсивностьНагрузкиОтОграждения")]
        public double FenceLoadIntensity { get; set; }

        [JsonPropertyName("КоординатаНагрузкиОтОграждения")]
        public double FenceLoadCoordinate { get; set; }

        [JsonPropertyName("АрмированиеПлиты")]
        public SlabReinforcement SlabReinforcement { get; set; }

        [JsonPropertyName("СосредоточенныеСилы")]
        public List<ConcentratedForce> ConcentratedForces { get; set; } = new List<ConcentratedForce>();

        [JsonPropertyName("КоличествоРасчетныхСечений")]
        public int SectionCount { get; set; }

        [JsonPropertyName("КоординатыРасчетныхСечений")]
        public List<double> SectionCoordinates { get; set; } = new List<double>();

        [JsonPropertyName("НомераТочекСопряженияПлитыСБалкой")]
        public int[] SlabBeamJunction { get; set; } = new int[2];

        [JsonPropertyName("НомераТочекСопряженияПлитыСВутом")]
        public int[] SlabVuteJunction { get; set; } = new int[2];

        [JsonPropertyName("НомераТочекСопряженияБортаСПлитой")]
        public int[] BorderSlabJunction { get; set; } = new int[2];

        [JsonPropertyName("НомераТочекСопряженияБортаСПлитой2")]
        public int[] BorderSlabJunction2 { get; set; } = new int[2];

        [JsonPropertyName("ПродольныйРазрезСоСтороныМеньшейZ")]
        public double LongitudinalCutLower { get; set; }

        [JsonPropertyName("ПродольныйРазрезСоСтороныБольшейZ")]
        public double LongitudinalCutUpper { get; set; }

        [JsonPropertyName("КонтурПоперечногоСечения")]
        public List<Point2D> CrossSectionContour { get; set; } = new List<Point2D>();

        [JsonPropertyName("КоличествоИзменённыхТочек")]
        public int ChangedPointsCount { get; set; }

        [JsonPropertyName("НомераИзменённыхТочек")]
        public List<int> ChangedPointIndices { get; set; } = new List<int>();

        [JsonPropertyName("ИзменённыеТочки")]
        public List<Point2D> ChangedPoints { get; set; } = new List<Point2D>();

        [JsonPropertyName("Отгибы")]
        public List<BendReinforcement> Bends { get; set; } = new List<BendReinforcement>();

        [JsonPropertyName("Хомуты")]
        public List<StirrupSection> StirrupSections { get; set; } = new List<StirrupSection>();

        [JsonPropertyName("РастянутыеСтержни")]
        public List<TensileBar> TensileBars { get; set; } = new List<TensileBar>();

        [JsonPropertyName("СжатыеСтержни")]
        public List<CompressedBar> CompressedBars { get; set; } = new List<CompressedBar>();
    }

    public class ConcentratedForce
    {
        [JsonPropertyName("КоординатаX")]
        public double X { get; set; }

        [JsonPropertyName("Величина")]
        public double Value { get; set; }
    }

    public class SlabReinforcement
    {
        [JsonPropertyName("КоличествоРасчетныхСтержней")]
        public int CalculatedBarsCount { get; set; }

        [JsonPropertyName("Стержни")]
        public List<CalculatedBar> CalculatedBars { get; set; } = new List<CalculatedBar>();
    }

    public class CalculatedBar
    {
        [JsonPropertyName("КоличествоТочекПерегиба")]
        public int BendPointsCount { get; set; }

        [JsonPropertyName("ПлощадьПоперечногоСечения")]
        public double Area { get; set; }

        [JsonPropertyName("ТочкиПерегиба")]
        public List<Point2D> BendPoints { get; set; } = new List<Point2D>();
    }

    public class BendReinforcement
    {
        [JsonPropertyName("ПлощадьПоперечногоСечения")]
        public double Area { get; set; }

        [JsonPropertyName("КоординатаВерхнегоКонца")]
        public double UpperCoordinate { get; set; }

        [JsonPropertyName("КоординатаНижнегоКонца")]
        public double LowerCoordinate { get; set; }

        [JsonPropertyName("ПриращениеВерхнее")]
        public double DeltaUpper { get; set; }

        [JsonPropertyName("ПриращениеНижнее")]
        public double DeltaLower { get; set; }
    }

    public class StirrupSection
    {
        [JsonPropertyName("КоординатаXКонцаУчастка")]
        public double EndX { get; set; }

        [JsonPropertyName("ПлощадьХомута")]
        public double Area { get; set; }

        [JsonPropertyName("ШагХомутов")]
        public double Step { get; set; }
    }

    public class TensileBar
    {
        [JsonPropertyName("Xmin")]
        public double XMin { get; set; }

        [JsonPropertyName("Xmax")]
        public double XMax { get; set; }

        [JsonPropertyName("ПриращениеНижнее")]
        public double DeltaLower { get; set; }

        [JsonPropertyName("ПлощадьПоперечногоСечения")]
        public double Area { get; set; }
    }

    public class CompressedBar
    {
        [JsonPropertyName("Xmin")]
        public double XMin { get; set; }

        [JsonPropertyName("Xmax")]
        public double XMax { get; set; }

        [JsonPropertyName("ПриращениеВерхнее")]
        public double DeltaUpper { get; set; }

        [JsonPropertyName("ПлощадьПоперечногоСечения")]
        public double Area { get; set; }
    }

    public class DetailedReinforcement
    {
        [JsonPropertyName("Балки")]
        public List<BeamDetailed> BeamDetails { get; set; } = new List<BeamDetailed>();
    }

    public class BeamDetailed
    {
        [JsonPropertyName("НомерБалки")]
        public int BeamNumber { get; set; }

        [JsonPropertyName("РастянутыеСтержни")]
        public List<DetailedBarGroup> TensileBars { get; set; } = new List<DetailedBarGroup>();

        [JsonPropertyName("СжатыеСтержни")]
        public List<DetailedBarGroup> CompressedBars { get; set; } = new List<DetailedBarGroup>();

        [JsonPropertyName("СтержниПлиты")]
        public List<PlateBarInfo> PlateBars { get; set; } = new List<PlateBarInfo>();
    }

    public class DetailedBarGroup
    {
        [JsonPropertyName("Количество")]
        public int Count { get; set; }

        [JsonPropertyName("Диаметр")]
        public double Diameter { get; set; }

        [JsonPropertyName("КоординатыZ")]
        public List<double> ZCoordinates { get; set; } = new List<double>();
    }

    public class PlateBarInfo
    {
        [JsonPropertyName("Диаметр")]
        public double Diameter { get; set; }

        [JsonPropertyName("Шаг")]
        public double Step { get; set; }
    }

    public struct Point2D
    {
        [JsonPropertyName("Z")]
        public double Z { get; set; }

        [JsonPropertyName("Y")]
        public double Y { get; set; }

        public Point2D(double z, double y) { Z = z; Y = y; }
    }
}