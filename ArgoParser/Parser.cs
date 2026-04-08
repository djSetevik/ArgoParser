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

            // Количество балок
            gp.BeamCount = ReadInt();
            Console.WriteLine($"  Количество балок: {gp.BeamCount}");

            // Координаты осей балок
            for (int i = 0; i < gp.BeamCount; i++)
                gp.BeamCoordinates.Add(ReadDouble());

            gp.BallastType = ReadDouble();
            gp.SleeperType = ReadDouble();

            gp.TrackAxisZ.Add(ReadDouble());
            gp.TrackAxisZ.Add(ReadDouble());

            // Признак наличия диафрагм только для многобалочных ПС
            if (gp.BeamCount > 1)
            {
                gp.DiaphragmPresence = ReadDouble();
            }

            // Количество точек контура балласта
            int ballastPointCount = ReadInt();
            Console.WriteLine($"  Точек контура балласта: {ballastPointCount}");

            // Точки контура балласта - пары Z Y
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

            bool isPlateStructure = (ribCount == 0);
            bool isEdgeBeam = (beamNumber == 1 || beamNumber == totalBeams);
            bool isInnerBeam = (totalBeams > 2) && !isEdgeBeam;
            bool hasLoads = isEdgeBeam || (totalBeams <= 2);

            Console.WriteLine($"  [Тип: ribCount={ribCount}, isEdge={isEdgeBeam}, isInner={isInnerBeam}, hasLoads={hasLoads}, isPlate={isPlateStructure}]");

            // 1. Тротуары и ограждения (4 числа)
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

            // 2. Арматура плиты
            beam.SlabReinforcement = ParseSlabReinforcement();

            // 3. Сосредоточенные силы
            int concentratedForceCount = ReadInt();
            Console.WriteLine($"  Сосредоточенных сил: {concentratedForceCount}");
            for (int i = 0; i < concentratedForceCount; i++)
            {
                double x = ReadDouble();
                double force = ReadDouble();
                beam.ConcentratedForces.Add(new ConcentratedForce { X = x, Value = force });
            }

            // 4. Расчётные сечения
            beam.SectionCount = ReadInt();
            Console.WriteLine($"  Расчетных сечений: {beam.SectionCount}");
            for (int i = 0; i < beam.SectionCount; i++)
                beam.SectionCoordinates.Add(ReadDouble());

            // 5. Характерные точки
            beam.SlabBeamJunction[0] = ReadInt();
            beam.SlabBeamJunction[1] = ReadInt();
            beam.SlabVuteJunction[0] = ReadInt();
            beam.SlabVuteJunction[1] = ReadInt();

            if (isEdgeBeam)
            {
                beam.BorderSlabJunction[0] = ReadInt();
                beam.BorderSlabJunction[1] = ReadInt();
            }

            // Для однобалочных ПС — ещё одна пара точек
            // (граница борта балластного корыта с большими координатами Z)
            if (totalBeams == 1)
            {
                beam.BorderSlabJunction2[0] = ReadInt();
                beam.BorderSlabJunction2[1] = ReadInt();
            }

            // 6. Продольные разрезы
            beam.LongitudinalCutLower = ReadDouble();
            beam.LongitudinalCutUpper = ReadDouble();
            Console.WriteLine($"  Продольные разрезы: нижний={beam.LongitudinalCutLower}, верхний={beam.LongitudinalCutUpper}");

            // 7. Контур поперечного сечения
            int crossSectionCount = ReadInt();
            Console.WriteLine($"  Точек контура сечения: {crossSectionCount}");

            for (int i = 0; i < crossSectionCount; i++)
            {
                double z = ReadDouble();
                double y = ReadDouble();
                beam.CrossSectionContour.Add(new Point2D(z, y));
            }

            // 8. Изменённые точки и отгибы
            // ДВА ФОРМАТА:
            //   Простой (A/B/S-серии):  changedCount [indices] [coords] bendCount [bends]
            //   Двублочный (N-серии):   0 count1 [data1] 0 count2 [data2] flag bendCount [bends]
            //
            // Алгоритм детекции:
            //   1. first > 0 → простой формат (first = changedCount)
            //   2. first == 0, second > 0 → проверяем токен на позиции current + 3*second:
            //      если = 0 → двублочный (separator2)
            //      иначе → простой (changedCount=0, second=bendCount)
            //   3. first == 0, second == 0 → простой (changedCount=0, bendCount=0)

            int firstValue = ReadInt();

            bool isTwoBlockFormat = false;
            bool isOneBlockWithSeparators = false;
            int secondValue = 0;

            if (firstValue == 0)
            {
                // Заглядываем вперёд НЕ потребляя токен
                secondValue = PeekInt();

                if (secondValue > 0)
                {
                    // Проверяем: после N индексов + N пар координат стоит 0?
                    int lookaheadPos = _tokenIndex + 1 + 3 * secondValue;
                    if (lookaheadPos < _tokens.Count)
                    {
                        double lookaheadVal = double.Parse(_tokens[lookaheadPos], CultureInfo.InvariantCulture);
                        if (lookaheadVal == 0)
                        {
                            // Нашли паттерн: 0 N [3N данных] 0 ...
                            // Нужно различить:
                            //   Двублочный (N4): ... 0 count2 INDEX(целое) ...
                            //   Одноблочный с сепараторами (N2): ... 0 bendCount AREA(дробное) ...
                            int extraCheckPos = lookaheadPos + 2;
                            if (extraCheckPos < _tokens.Count)
                            {
                                double valAfter = double.Parse(_tokens[extraCheckPos], CultureInfo.InvariantCulture);
                                if (valAfter != Math.Floor(valAfter))
                                {
                                    // Дробное число → площадь отгиба → одноблочный формат
                                    isOneBlockWithSeparators = true;
                                }
                                else
                                {
                                    // Целое число → индекс точки → двублочный формат
                                    isTwoBlockFormat = true;
                                }
                            }
                            else
                            {
                                isOneBlockWithSeparators = true;
                            }
                        }
                    }
                }
            }

            int bendCount;

            if (firstValue > 0)
            {
                // Простой формат: firstValue = changedCount
                Console.WriteLine($"  Формат: простой, changedCount={firstValue}");
                beam.ChangedPointsCount = firstValue;

                for (int i = 0; i < firstValue; i++)
                    beam.ChangedPointIndices.Add(ReadInt());
                for (int i = 0; i < firstValue; i++)
                {
                    double z = ReadDouble();
                    double y = ReadDouble();
                    beam.ChangedPoints.Add(new Point2D(z, y));
                }

                bendCount = ReadInt();
            }
            else if (isOneBlockWithSeparators)
            {
                // Одноблочный с сепараторами (N2-серия): 0 count [data] 0 bendCount
                // Первый 0 уже потреблён как firstValue
                beam.ChangedPointsCount = ReadInt(); // потребляем count
                Console.WriteLine($"  Формат: одноблочный с сепараторами, changedCount={beam.ChangedPointsCount}");

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

                ReadDouble(); // потребляем второй сепаратор (0)
                bendCount = ReadInt();
            }
            else if (isTwoBlockFormat)
            {
                // Двублочный формат: first=separator1(0), second=count1
                // secondValue был только просмотрен (Peek), теперь потребляем
                ReadInt(); // потребляем count1 (== secondValue)
                Console.WriteLine($"  Формат: двублочный, count1={secondValue}");

                beam.ChangedPointsCount = secondValue;

                if (secondValue > 0)
                {
                    for (int i = 0; i < secondValue; i++)
                        beam.ChangedPointIndices.Add(ReadInt());
                    for (int i = 0; i < secondValue; i++)
                    {
                        double z = ReadDouble();
                        double y = ReadDouble();
                        beam.ChangedPoints.Add(new Point2D(z, y));
                    }
                    Console.WriteLine($"    Блок1: indices=[{string.Join(", ", beam.ChangedPointIndices)}]");
                }

                // Блок 2
                double separator2 = ReadDouble();
                int changedCount2 = ReadInt();
                Console.WriteLine($"    Блок2: sep={separator2}, count={changedCount2}");
                beam.ChangedPointsCount2 = changedCount2;

                if (changedCount2 > 0)
                {
                    for (int i = 0; i < changedCount2; i++)
                        beam.ChangedPointIndices2.Add(ReadInt());
                    for (int i = 0; i < changedCount2; i++)
                    {
                        double z = ReadDouble();
                        double y = ReadDouble();
                        beam.ChangedPoints2.Add(new Point2D(z, y));
                    }
                    Console.WriteLine($"    Блок2: indices=[{string.Join(", ", beam.ChangedPointIndices2)}]");
                }

                // Флаг симметрии отгибов
                beam.BendSymmetryFlag = ReadDouble();
                Console.WriteLine($"  Флаг симметрии отгибов: {beam.BendSymmetryFlag}");

                bendCount = ReadInt();
            }
            else
            {
                // Простой формат: changedCount=0 (first=0), bendCount=следующий токен
                bendCount = ReadInt(); // потребляем bendCount (был только просмотрен как secondValue)
                Console.WriteLine($"  Формат: простой, changedCount=0, bendCount={bendCount}");
                beam.ChangedPointsCount = 0;
            }

            // 9. Отогнутые стержни
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

            // 10. Хомуты
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

            // 11. Растянутые стержни
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

            // 12. Сжатые стержни
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

            Console.WriteLine($"  === Конец балки #{beamNumber}, токен: {_tokenIndex} ===");

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

                // Растянутые стержни
                for (int i = 0; i < beam.TensileBars.Count; i++)
                {
                    if (!HasMoreTokens()) break;

                    int count = ReadInt();

                    if (count == 0)
                    {
                        // Проверяем: "0 0" означает пустую группу (пропускаем оба нуля)
                        if (HasMoreTokens())
                        {
                            int nextVal = PeekInt();
                            if (nextVal == 0)
                            {
                                ReadInt(); // потребляем второй 0
                                Console.WriteLine($"    Растянутые #{i + 1}: пусто (0 0)");
                                continue;
                            }
                        }
                        _tokenIndex--;
                        break;
                    }

                    if (count < 0)
                    {
                        _tokenIndex--;
                        break;
                    }

                    double diameter = ReadDouble();
                    var group = new DetailedBarGroup { Count = count, Diameter = diameter };

                    for (int j = 0; j < count; j++)
                    {
                        if (!HasMoreTokens()) break;
                        group.ZCoordinates.Add(ReadDouble());
                    }

                    bd.TensileBars.Add(group);
                    Console.WriteLine($"    Растянутые #{i + 1}: {count} × Ø{diameter}");
                }

                // Сжатые стержни
                for (int i = 0; i < beam.CompressedBars.Count; i++)
                {
                    if (!HasMoreTokens()) break;

                    int count = ReadInt();

                    if (count == 0)
                    {
                        if (HasMoreTokens())
                        {
                            int nextVal = PeekInt();
                            if (nextVal == 0)
                            {
                                ReadInt();
                                Console.WriteLine($"    Сжатые #{i + 1}: пусто (0 0)");
                                continue;
                            }
                        }
                        _tokenIndex--;
                        break;
                    }

                    if (count < 0)
                    {
                        _tokenIndex--;
                        break;
                    }

                    double diameter = ReadDouble();
                    var group = new DetailedBarGroup { Count = count, Diameter = diameter };

                    for (int j = 0; j < count; j++)
                    {
                        if (!HasMoreTokens()) break;
                        group.ZCoordinates.Add(ReadDouble());
                    }

                    bd.CompressedBars.Add(group);
                    Console.WriteLine($"    Сжатые #{i + 1}: {count} × Ø{diameter}");
                }

                // Арматура плиты
                if (beam.SlabReinforcement?.CalculatedBarsCount > 0)
                {
                    Console.WriteLine($"    Арматура плиты: {beam.SlabReinforcement.CalculatedBarsCount} стержней");
                    for (int i = 0; i < beam.SlabReinforcement.CalculatedBarsCount; i++)
                    {
                        if (!HasMoreTokens()) break;
                        double d = ReadDouble();
                        double s = ReadDouble();
                        bd.PlateBars.Add(new PlateBarInfo { Diameter = d, Step = s });
                    }
                }

                detailed.BeamDetails.Add(bd);
            }

            return detailed;
        }

        private bool HasMoreTokens() => _tokenIndex < _tokens.Count;

        private int PeekInt()
        {
            if (_tokenIndex >= _tokens.Count) return -1;
            var token = _tokens[_tokenIndex];
            if (token.Contains('.'))
                return (int)Math.Round(double.Parse(token, CultureInfo.InvariantCulture));
            return int.Parse(token);
        }

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