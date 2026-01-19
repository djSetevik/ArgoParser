using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using System.Globalization;

namespace ArgoParser
{
    public class ArgoParser
    {
        private List<string> _tokens;
        private int _tokenIndex;
        private string[] _rawLines;

        public ArgoDocument Parse(string filePath)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var cp866 = Encoding.GetEncoding(866);
            var text = File.ReadAllText(filePath, cp866);

            _rawLines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            _tokens = new List<string>();
            _tokenIndex = 0;

            int commentCount = int.Parse(_rawLines[0].Trim());

            for (int i = commentCount + 1; i < _rawLines.Length; i++)
            {
                var parts = _rawLines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                _tokens.AddRange(parts);
            }

            Console.WriteLine($"Файл загружен: {_rawLines.Length} строк, {_tokens.Count} токенов (после {commentCount} комментариев)");

            var doc = new ArgoDocument();

            doc.SourceFileName = Path.GetFileName(filePath);
            doc.FileCode = ArgoFileCode.Parse(filePath);

            doc.Header = new Header { CommentLineCount = commentCount };
            for (int i = 1; i <= commentCount && i < _rawLines.Length; i++)
            {
                doc.Header.Comments.Add(_rawLines[i]);
            }
            Console.WriteLine($"Комментарии: {string.Join(" | ", doc.Header.Comments)}");

            Console.WriteLine("\n=== Парсинг глобальных параметров ===");
            doc.GlobalParams = ParseGlobalParameters();

            for (int i = 0; i < doc.GlobalParams.BeamCount; i++)
            {
                Console.WriteLine($"\n=== Парсинг балки #{i + 1} ===");
                doc.Beams.Add(ParseBeam(i + 1, doc.GlobalParams.BeamCount, doc.FileCode));
            }

            if (HasMoreTokens())
            {
                doc.PrintCopies = ReadInt();
                Console.WriteLine($"\nКоличество экземпляров печати: {doc.PrintCopies}");

                if (HasMoreTokens())
                {
                    doc.DetailedReinforcement = ParseDetailedReinforcement(doc.Beams);
                }
            }

            return doc;
        }

        private GlobalParameters ParseGlobalParameters()
        {
            var gp = new GlobalParameters();

            gp.PrintLevel = ReadDouble();
            gp.ConcreteStrength = ReadDouble();
            Console.WriteLine($"  Прочность бетона: {gp.ConcreteStrength}");

            // 4 типа арматуры
            gp.TensileReinforcementType = ReadDouble();
            gp.CompressedReinforcementType = ReadDouble();
            gp.SlabReinforcementType = ReadDouble();
            gp.StirrupType = ReadDouble();

            gp.SupportAxis1 = ReadDouble();
            gp.SupportAxis2 = ReadDouble();
            gp.InnerSupport1 = ReadDouble();
            gp.InnerSupport2 = ReadDouble();
            gp.FullLength = ReadDouble();
            Console.WriteLine($"  Полная длина: {gp.FullLength}");

            gp.BeamCount = ReadInt();
            Console.WriteLine($"  Количество балок: {gp.BeamCount}");

            for (int i = 0; i < gp.BeamCount; i++)
                gp.BeamCoordinates.Add(ReadDouble());

            gp.BallastType = ReadDouble();
            gp.SleeperType = ReadDouble();

            gp.TrackAxisZ.Add(ReadDouble());
            gp.TrackAxisZ.Add(ReadDouble());

            if (gp.BeamCount > 1)
            {
                gp.DiaphragmPresence = ReadDouble();
            }

            int ballastPointCount = ReadInt();
            Console.WriteLine($"  Точек контура балласта: {ballastPointCount}");

            for (int i = 0; i < ballastPointCount; i++)
            {
                double z = ReadDouble();
                double y = ReadDouble();
                gp.BallastContour.Add(new Point2D(z, y));
            }

            return gp;
        }

        private Beam ParseBeam(int beamNumber, int totalBeams, ArgoFileCode fileCode)
        {
            var beam = new Beam { Number = beamNumber };

            int ribCount = fileCode?.MainRibCount ?? 0;

            bool is8Points = (totalBeams == 1);
            bool isEdgeBeam = (beamNumber == 1 || beamNumber == totalBeams);
            bool isInnerBeam = (totalBeams > 2) && !isEdgeBeam;
            bool hasLoads = isEdgeBeam || (totalBeams <= 2);

            if (hasLoads)
            {
                beam.SidewalkLoadIntensity = ReadDouble();
                beam.SidewalkLoadCoordinate = ReadDouble();
                beam.FenceLoadIntensity = ReadDouble();
                beam.FenceLoadCoordinate = ReadDouble();
                Console.WriteLine($"  Нагрузки: [{beam.SidewalkLoadIntensity}, {beam.SidewalkLoadCoordinate}, {beam.FenceLoadIntensity}, {beam.FenceLoadCoordinate}]");
            }
            else
            {
                Console.WriteLine($"  Нагрузки: пропущены (внутренняя балка)");
            }

            beam.SlabReinforcement = ParseSlabReinforcement();

            int concentratedForceCount = ReadInt();
            Console.WriteLine($"  Сосредоточенных сил: {concentratedForceCount}");
            for (int i = 0; i < concentratedForceCount; i++)
            {
                double x = ReadDouble();
                double force = ReadDouble();
                beam.ConcentratedForces.Add(new ConcentratedForce { X = x, Value = force });
            }

            beam.SectionCount = ReadInt();
            Console.WriteLine($"  Расчетных сечений: {beam.SectionCount}");
            for (int i = 0; i < beam.SectionCount; i++)
                beam.SectionCoordinates.Add(ReadDouble());

            beam.SlabBeamJunction[0] = ReadInt();
            beam.SlabBeamJunction[1] = ReadInt();
            beam.SlabVuteJunction[0] = ReadInt();
            beam.SlabVuteJunction[1] = ReadInt();

            if (!isInnerBeam)
            {
                beam.BorderSlabJunction[0] = ReadInt();
                beam.BorderSlabJunction[1] = ReadInt();
            }

            if (is8Points)
            {
                beam.BorderSlabJunction2[0] = ReadInt();
                beam.BorderSlabJunction2[1] = ReadInt();
            }

            beam.LongitudinalCutLower = ReadDouble();
            beam.LongitudinalCutUpper = ReadDouble();

            int crossSectionCount = ReadInt();
            Console.WriteLine($"  Точек контура сечения: {crossSectionCount}");
            for (int i = 0; i < crossSectionCount; i++)
            {
                double z = ReadDouble();
                double y = ReadDouble();
                beam.CrossSectionContour.Add(new Point2D(z, y));
            }

            beam.ChangedPointsCount = ReadInt();
            Console.WriteLine($"  Измененных точек: {beam.ChangedPointsCount}");
            if (beam.ChangedPointsCount > 0)
            {
                for (int i = 0; i < beam.ChangedPointsCount; i++)
                    beam.ChangedPointIndices.Add(ReadInt());
                for (int i = 0; i < beam.ChangedPointsCount; i++)
                {
                    double z = ReadDouble();
                    double y = ReadDouble();
                    beam.ChangedPoints.Add(new Point2D(z, y));
                }
            }

            int bendCount = ReadInt();
            Console.WriteLine($"  Отгибов: {bendCount}");
            for (int i = 0; i < bendCount; i++)
            {
                beam.Bends.Add(new BendReinforcement
                {
                    Area = ReadDouble(),
                    UpperCoordinate = ReadDouble(),
                    LowerCoordinate = ReadDouble(),
                    DeltaUpper = ReadDouble(),
                    DeltaLower = ReadDouble()
                });
            }

            int stirrupCount = ReadInt();
            Console.WriteLine($"  Секций хомутов: {stirrupCount}");
            for (int i = 0; i < stirrupCount; i++)
            {
                beam.StirrupSections.Add(new StirrupSection
                {
                    EndX = ReadDouble(),
                    Area = ReadDouble(),
                    Step = ReadDouble()
                });
            }

            int tensileCount = ReadInt();
            Console.WriteLine($"  Растянутых стержней: {tensileCount}");
            for (int i = 0; i < tensileCount; i++)
            {
                beam.TensileBars.Add(new TensileBar
                {
                    XMin = ReadDouble(),
                    XMax = ReadDouble(),
                    DeltaLower = ReadDouble(),
                    Area = ReadDouble()
                });
            }

            int compressedCount = ReadInt();
            Console.WriteLine($"  Сжатых стержней: {compressedCount}");
            for (int i = 0; i < compressedCount; i++)
            {
                beam.CompressedBars.Add(new CompressedBar
                {
                    XMin = ReadDouble(),
                    XMax = ReadDouble(),
                    DeltaUpper = ReadDouble(),
                    Area = ReadDouble()
                });
            }

            return beam;
        }

        private SlabReinforcement ParseSlabReinforcement()
        {
            var slab = new SlabReinforcement();
            slab.CalculatedBarsCount = ReadInt();
            Console.WriteLine($"  Расчетных стержней плиты: {slab.CalculatedBarsCount}");

            if (slab.CalculatedBarsCount == 0)
                return slab;

            var bendPointCounts = new List<int>();
            for (int i = 0; i < slab.CalculatedBarsCount; i++)
                bendPointCounts.Add(ReadInt());

            var areas = new List<double>();
            for (int i = 0; i < slab.CalculatedBarsCount; i++)
                areas.Add(ReadDouble());

            for (int i = 0; i < slab.CalculatedBarsCount; i++)
            {
                var bar = new CalculatedBar
                {
                    BendPointsCount = bendPointCounts[i],
                    Area = areas[i]
                };

                for (int j = 0; j < bar.BendPointsCount; j++)
                {
                    double z = ReadDouble();
                    double y = ReadDouble();
                    bar.BendPoints.Add(new Point2D(z, y));
                }

                slab.CalculatedBars.Add(bar);
            }

            return slab;
        }

        private DetailedReinforcement ParseDetailedReinforcement(List<Beam> beams)
        {
            Console.WriteLine("\n=== Парсинг детализации ===");
            var detailed = new DetailedReinforcement();

            foreach (var beam in beams)
            {
                Console.WriteLine($"\n  Детализация балки #{beam.Number}");
                var bd = new BeamDetailed { BeamNumber = beam.Number };

                for (int i = 0; i < beam.TensileBars.Count; i++)
                {
                    if (!HasMoreTokens()) break;
                    int count = ReadInt();
                    if (count <= 0) { _tokenIndex--; break; }

                    double diameter = ReadDouble();
                    var group = new DetailedBarGroup { Count = count, Diameter = diameter };
                    for (int j = 0; j < count; j++)
                        group.ZCoordinates.Add(ReadDouble());
                    bd.TensileBars.Add(group);
                    Console.WriteLine($"    Растянутые #{i + 1}: {count} × Ø{diameter}");
                }

                for (int i = 0; i < beam.CompressedBars.Count; i++)
                {
                    if (!HasMoreTokens()) break;
                    int count = ReadInt();
                    if (count <= 0) { _tokenIndex--; break; }

                    double diameter = ReadDouble();
                    var group = new DetailedBarGroup { Count = count, Diameter = diameter };
                    for (int j = 0; j < count; j++)
                        group.ZCoordinates.Add(ReadDouble());
                    bd.CompressedBars.Add(group);
                    Console.WriteLine($"    Сжатые #{i + 1}: {count} × Ø{diameter}");
                }

                if (beam.SlabReinforcement?.CalculatedBarsCount > 0)
                {
                    for (int i = 0; i < beam.SlabReinforcement.CalculatedBarsCount; i++)
                    {
                        if (!HasMoreTokens()) break;
                        double d = ReadDouble();
                        double s = ReadDouble();
                        bd.PlateBars.Add(new PlateBarInfo { Diameter = d, Step = s });
                    }
                    Console.WriteLine($"    Арматура плиты: {bd.PlateBars.Count} стержней");
                }

                detailed.BeamDetails.Add(bd);
            }

            return detailed;
        }

        private bool HasMoreTokens() => _tokenIndex < _tokens.Count;

        private double ReadDouble()
        {
            if (_tokenIndex >= _tokens.Count)
                throw new InvalidOperationException($"Конец данных на токене {_tokenIndex}");
            var token = _tokens[_tokenIndex++];
            return double.Parse(token, CultureInfo.InvariantCulture);
        }

        private int ReadInt()
        {
            if (_tokenIndex >= _tokens.Count)
                throw new InvalidOperationException($"Конец данных на токене {_tokenIndex}");
            var token = _tokens[_tokenIndex++];
            if (token.Contains('.'))
                return (int)Math.Round(double.Parse(token, CultureInfo.InvariantCulture));
            return int.Parse(token);
        }
    }
}