using System;
using System.Collections.Generic;
using System.Linq;

namespace ArgoParser
{
    public class ArgoToPrssmConverter
    {
        private int _sectionIdCounter = 1;
        private int _materialIdCounter = 1;

        public PrssmDocument Convert(ArgoDocument argoDoc)
        {
            if (argoDoc == null)
                throw new ArgumentNullException(nameof(argoDoc));

            var prssm = new PrssmDocument
            {
                BeamsNumber = argoDoc.GlobalParams.BeamCount
            };

            var material = ConvertMaterial(argoDoc.GlobalParams);
            double firstBeamCoord = argoDoc.GlobalParams.BeamCoordinates.FirstOrDefault();

            List<(double X, double Y)> firstBeamCentered = null;

            for (int i = 0; i < argoDoc.Beams.Count; i++)
            {
                var argoBeam = argoDoc.Beams[i];
                double beamCoord = argoDoc.GlobalParams.BeamCoordinates[i];

                double position = (beamCoord - firstBeamCoord) * 10;
                double step = 0;
                if (i > 0)
                {
                    double prevCoord = argoDoc.GlobalParams.BeamCoordinates[i - 1];
                    step = (beamCoord - prevCoord) * 10;
                }

                // 1. Применяем изменённые точки
                var contour = ApplyChangedPoints(argoBeam);

                // 2. Конвертируем в мм относительно оси балки
                var rawPoints = SectionCalculator.ConvertArgoContour(contour, beamCoord);

                // 3. ЦТ сырого контура
                var rawProps = SectionCalculator.Calculate(rawPoints);

                // 4. Центрируем по ЦТ
                var centered = rawPoints
                    .Select(p => (p.X - rawProps.Xc, p.Y - rawProps.Yc))
                    .ToList();

                // 5. Находим центр ребра в центрированных координатах
                //    Ребро = две нижние точки контура (минимальный Y)
                double profMinY = centered.Min(p => p.Item2);
                var bottomPts = centered
                    .Where(p => Math.Abs(p.Item2 - profMinY) < 1).ToList();
                double ribLeftX = bottomPts.Any() ? bottomPts.Min(p => p.Item1) : 0;
                double ribRightX = bottomPts.Any() ? bottomPts.Max(p => p.Item1) : 0;
                double ribCenterX = (ribLeftX + ribRightX) / 2.0;

                // 6. Зеркалирование
                bool isReflectedY = false;
                if (i > 0 && firstBeamCentered != null)
                    isReflectedY = DetectMirror(firstBeamCentered, centered);
                if (i == 0)
                    firstBeamCentered = centered;

                // 7. Характеристики
                var props = SectionCalculator.Calculate(centered);

                double profMaxY = centered.Max(p => p.Item2);
                double profMinX = centered.Min(p => p.Item1);
                double profMaxX = centered.Max(p => p.Item1);

                double ycValue = props.Xc / 1000.0;
                double zcValue = props.Yc / 1000.0;
                double offsetZ = profMinY;

                // StressPoints
                var bPts = centered.Where(p => Math.Abs(p.Item2 - profMinY) < 1).ToList();
                var tPts = centered.Where(p => Math.Abs(p.Item2 - profMaxY) < 1).ToList();
                double bMinX = bPts.Any() ? bPts.Min(p => p.Item1) : profMinX;
                double bMaxX = bPts.Any() ? bPts.Max(p => p.Item1) : profMaxX;
                double tMinX = tPts.Any() ? tPts.Min(p => p.Item1) : profMinX;
                double tMaxX = tPts.Any() ? tPts.Max(p => p.Item1) : profMaxX;

                var section = new PrssmSection
                {
                    Id = _sectionIdCounter++,
                    Name = isReflectedY ? "Ж/б сечение балки - копия" : "Ж/б сечение балки",
                    SectionType = 0,
                    Yc = Math.Round(ycValue, 10),
                    Zc = Math.Round(zcValue, 10),
                    Area = props.Area,
                    Perimeter = props.Perimeter,
                    Iyy = props.Iyy,
                    Izz = props.Izz,
                    Iyz = props.Iyz,
                    It = props.It,
                    Syy = props.Syy,
                    Szz = props.Szz,
                    Byy = props.Byy,
                    Bzz = props.Bzz,
                    WyyPlus = props.WyyPlus,
                    WyyMinus = props.WyyMinus,
                    WzzPlus = props.WzzPlus,
                    WzzMinus = props.WzzMinus,
                    OffsetType = 8,
                    OffsetY = 0,
                    OffsetZ = Math.Round(offsetZ, 2),
                    WeightFactor = 1
                };

                var shape = new PrssmShape
                {
                    Id = 1,
                    Name = section.Name,
                    CadType = 0,
                    IsReflectY = isReflectedY,
                    Location = new PrssmPoint(0, 0),
                    Profile = new List<PrssmProfileRegion>
                    {
                        new PrssmProfileRegion
                        {
                            RegionType = 0,
                            Points = BuildProfilePoints(centered)
                        }
                    }
                };
                section.Shapes.Add(shape);

                section.StressPoints = new List<PrssmPoint>
                {
                    new PrssmPoint(Math.Round(tMinX, 2), Math.Round(profMaxY, 2)),
                    new PrssmPoint(Math.Round(tMaxX, 2), Math.Round(profMaxY, 2)),
                    new PrssmPoint(Math.Round(bMaxX, 2), Math.Round(profMinY, 2)),
                    new PrssmPoint(Math.Round(bMinX, 2), Math.Round(profMinY, 2))
                };

                var prssmBeam = new PrssmBeam
                {
                    Id = i + 1,
                    Material = material,
                    Position = position,
                    Step = step,
                    BeamPartsNumber = 1
                };

                prssmBeam.BeamParts.Add(new PrssmBeamPart
                {
                    Id = 1,
                    Section = section,
                    Length = argoDoc.GlobalParams.FullLength * 10,
                    Division = 1,
                    IsStartPier = false,
                    IsEndPier = false
                });

                // Арматура: привязка к левому нижнему углу ребра
                var bindingPoint = new PrssmPoint(section.Yc, section.Zc);

                ConvertLongitudinalReinforcement(
                    argoBeam, argoDoc, prssmBeam, centered, bindingPoint,
                    profMinY, profMaxY, ribCenterX, ribLeftX);

                ConvertTransverseReinforcement(
                    argoBeam, prssmBeam, centered, bindingPoint);

                prssmBeam.LongitudinalReinforcementNumber =
                    prssmBeam.ReinforcementLongitudinals.Count;
                prssmBeam.TransverseReinforcementNumber =
                    prssmBeam.ReinforcementTransverses.Count;

                prssm.Beams.Add(prssmBeam);
            }

            double slabWidth = 0;
            if (argoDoc.GlobalParams.BeamCoordinates.Count >= 2)
                slabWidth = (argoDoc.GlobalParams.BeamCoordinates.Last() -
                    argoDoc.GlobalParams.BeamCoordinates.First()) * 10;

            prssm.SelectedSlab = new PrssmSlab
            {
                Width = slabWidth > 0 ? slabWidth.ToString("F0") : "0"
            };

            return prssm;
        }

        private List<Point2D> ApplyChangedPoints(Beam argoBeam)
        {
            var contour = argoBeam.CrossSectionContour
                .Select(p => new Point2D(p.Z, p.Y)).ToList();

            if (argoBeam.ChangedPointsCount > 0)
            {
                for (int j = 0; j < argoBeam.ChangedPointsCount; j++)
                {
                    int idx = argoBeam.ChangedPointIndices[j] - 1;
                    if (idx >= 0 && idx < contour.Count)
                    {
                        contour[idx] = new Point2D(
                            argoBeam.ChangedPoints[j].Z,
                            argoBeam.ChangedPoints[j].Y);
                    }
                }
            }
            return contour;
        }

        /// <summary>
        /// Арматура в АРГО:
        ///   Z координаты (горизонталь) = от оси ребра (=оси балки), в см
        ///   Y координата (DeltaLower) = от низа ребра вверх, в см
        /// 
        /// В ПАРИС (центрированный профиль):
        ///   YOffset = X координата в профиле = ribCenterX + Z_арго*10
        ///     (ribCenterX = центр ребра в центрированном профиле)
        ///   ZOffset = Y координата в профиле = profMinY + DeltaLower*10 + d/2
        ///     (profMinY = низ ребра в центрированном профиле)
        /// 
        /// Но вы сказали: привязка от Y нижней левой силовой точки = ribLeftX
        /// Значит YOffset = ribLeftX + Z_арго*10 + (смещение от левого края)?
        /// 
        /// Нет, из отчёта: Z от -37.8 до 37.8 = от центра ребра.
        /// Центр ребра в профиле = ribCenterX.
        /// YOffset = ribCenterX + avg(Z)*10
        /// </summary>
        private void ConvertLongitudinalReinforcement(
    Beam argoBeam, ArgoDocument argoDoc,
    PrssmBeam prssmBeam,
    List<(double X, double Y)> profile,
    PrssmPoint bindingPoint,
    double profMinY, double profMaxY,
    double ribCenterX, double ribLeftX)
        {
            var detailed = argoDoc.DetailedReinforcement?.BeamDetails
                .FirstOrDefault(d => d.BeamNumber == argoBeam.Number);

            if (detailed == null || detailed.TensileBars.Count == 0)
            {
                ConvertLongFromCalc(argoBeam, prssmBeam,
                    profMinY, profMaxY, bindingPoint, ribCenterX);
                return;
            }

            // Растянутые
            for (int i = 0; i < detailed.TensileBars.Count &&
                            i < argoBeam.TensileBars.Count; i++)
            {
                var dg = detailed.TensileBars[i];
                var cb = argoBeam.TensileBars[i];

                double yOffset = 0;
                double stepElement = 0;

                if (dg.ZCoordinates.Count > 0)
                {
                    // Z координаты каждого стержня — от оси балки, в см
                    // Сортируем: первый = самый левый
                    var sortedZ = dg.ZCoordinates.OrderBy(z => z).ToList();

                    if (sortedZ.Count > 1)
                    {
                        double totalWidth = (sortedZ.Last() - sortedZ.First()) * 10;
                        stepElement = totalWidth / (dg.Count - 1);
                    }

                    // YOffset = координата ПЕРВОГО (самого левого) стержня
                    // в координатах профиля (центрированного по ЦТ)
                    // Z_первый от оси балки (в см) → в мм + смещение оси от ЦТ
                    double firstZ = sortedZ.First(); // самый левый, в см от оси
                    yOffset = ribCenterX + firstZ * 10;
                }

                // ZOffset: от низа профиля вверх
                double zOffset = profMinY + (cb.DeltaLower * 10) + (dg.Diameter / 2.0);

                prssmBeam.ReinforcementLongitudinals.Add(
                    new PrssmLongitudinalReinforcement
                    {
                        Diameter = dg.Diameter,
                        ItemsAtRow = dg.Count,
                        NAtItem = 1,
                        BindingPoint = bindingPoint,
                        YOffset = Math.Round(yOffset, 2),
                        ZOffset = Math.Round(zOffset, 2),
                        StepElement = Math.Round(stepElement, 2),
                        OffsetFromStart = Math.Max(0, cb.XMin * 10),
                        SegmentCount = 1,
                        Segments = new List<PrssmReinforcementSegment>
                        {
                    new PrssmReinforcementSegment
                    {
                        Length = (cb.XMax - cb.XMin) * 10,
                    }
                        }
                    });
            }

            // Сжатые
            for (int i = 0; i < detailed.CompressedBars.Count &&
                            i < argoBeam.CompressedBars.Count; i++)
            {
                var dg = detailed.CompressedBars[i];
                var cb = argoBeam.CompressedBars[i];

                double yOffset = 0;
                double stepElement = 0;

                if (dg.ZCoordinates.Count > 0)
                {
                    var sortedZ = dg.ZCoordinates.OrderBy(z => z).ToList();
                    if (sortedZ.Count > 1)
                    {
                        double totalWidth = (sortedZ.Last() - sortedZ.First()) * 10;
                        stepElement = totalWidth / (dg.Count - 1);
                    }
                    double firstZ = sortedZ.First();
                    yOffset = ribCenterX + firstZ * 10;
                }

                double zOffset = profMaxY - (cb.DeltaUpper * 10) - (dg.Diameter / 2.0);

                prssmBeam.ReinforcementLongitudinals.Add(
                    new PrssmLongitudinalReinforcement
                    {
                        Diameter = dg.Diameter,
                        ItemsAtRow = dg.Count,
                        NAtItem = 1,
                        BindingPoint = bindingPoint,
                        YOffset = Math.Round(yOffset, 2),
                        ZOffset = Math.Round(zOffset, 2),
                        StepElement = Math.Round(stepElement, 2),
                        OffsetFromStart = Math.Max(0, cb.XMin * 10),
                        SegmentCount = 1,
                        Segments = new List<PrssmReinforcementSegment>
                        {
                    new PrssmReinforcementSegment
                    {
                        Length = (cb.XMax - cb.XMin) * 10,
                    }
                        }
                    });
            }
        }

        private void ConvertLongFromCalc(
            Beam argoBeam, PrssmBeam prssmBeam,
            double profMinY, double profMaxY,
            PrssmPoint bp, double ribCenterX)
        {
            foreach (var cb in argoBeam.TensileBars)
            {
                if (cb.Area <= 0) continue;
                var (d, n) = EstDiamCount(cb.Area);

                // Без детализации: распределяем равномерно в ребре
                // Ширина ребра приблизительно
                double ribWidth = 800; // будет уточнено
                double stepEl = n > 1 ? (ribWidth - d) / (n - 1) : 0;
                double firstY = ribCenterX - (ribWidth - d) / 2.0;

                prssmBeam.ReinforcementLongitudinals.Add(
                    new PrssmLongitudinalReinforcement
                    {
                        Diameter = d,
                        ItemsAtRow = n,
                        NAtItem = 1,
                        BindingPoint = bp,
                        YOffset = Math.Round(firstY, 2),
                        ZOffset = Math.Round(profMinY + (cb.DeltaLower * 10) + (d / 2.0), 2),
                        OffsetFromStart = Math.Max(0, cb.XMin * 10),
                        StepElement = Math.Round(stepEl, 2),
                        SegmentCount = 1,
                        Segments = new List<PrssmReinforcementSegment>
                        {
                    new PrssmReinforcementSegment
                        { Length = (cb.XMax - cb.XMin) * 10 }
                        }
                    });
            }
        }

        private void ConvertTransverseReinforcement(
            Beam argoBeam, PrssmBeam prssmBeam,
            List<(double X, double Y)> profile,
            PrssmPoint bindingPoint)
        {
            if (argoBeam.StirrupSections.Count == 0) return;

            double h = profile.Max(p => p.Y) - profile.Min(p => p.Y);
            double ribW = EstRibWidth(profile);
            double startX = 0;

            foreach (var ss in argoBeam.StirrupSections)
            {
                if (ss.Area <= 0 || ss.Step <= 0)
                { startX = ss.EndX * 10; continue; }

                double d = EstStirrupDiam(ss.Area);
                double secLen = (ss.EndX * 10) - startX;
                int cnt = (int)Math.Max(1, secLen / (ss.Step * 10));

                prssmBeam.ReinforcementTransverses.Add(
                    new PrssmTransverseReinforcement
                    {
                        Diameter = d,
                        ItemsAtRow = cnt,
                        NAtItem = 1,
                        IsClosed = false,
                        StepElement = ss.Step * 10,
                        OffsetFromStart = startX,
                        YOffset = 0,
                        ZOffset = 0,
                        BindingPoint = bindingPoint,
                        SegmentCount = 2,
                        Segments = new List<PrssmReinforcementSegment>
                        {
                            new PrssmReinforcementSegment
                                { Length = ribW - 60, Angle = 0 },
                            new PrssmReinforcementSegment
                                { Length = h - 40, Angle = 90, Height = h - 40 }
                        }
                    });
                startX = ss.EndX * 10;
            }
        }

        private bool DetectMirror(
            List<(double X, double Y)> c1, List<(double X, double Y)> c2)
        {
            if (c1.Count != c2.Count) return false;
            int m = 0;
            foreach (var p in c2)
                if (c1.Any(q => Math.Abs(-q.X - p.X) < 5 &&
                                Math.Abs(q.Y - p.Y) < 5)) m++;
            return m > c2.Count * 0.8;
        }

        private PrssmMaterial ConvertMaterial(GlobalParameters gp)
        {
            var (n, e, s) = GetCC(gp.ConcreteStrength);
            return new PrssmMaterial
            {
                Id = _materialIdCounter++,
                Name = n,
                MaterialType = 0,
                StandartMaterialType = s,
                YoungModulus = e,
                PoissonRatio = 0.2,
                SpecificWeight = 2.45E-05,
                ThermalCoefficient = 1E-05
            };
        }

        private (string, double, int) GetCC(double s)
        {
            if (s <= 10) return ("Б10", 19000, 10);
            if (s <= 15) return ("Б15", 24000, 13);
            if (s <= 20) return ("Б20", 27500, 15);
            if (s <= 25) return ("Б25", 30000, 16);
            if (s <= 30) return ("Б30", 32500, 17);
            if (s <= 35) return ("Б35", 34500, 18);
            if (s <= 40) return ("Б40", 36000, 19);
            if (s <= 45) return ("Б45", 37000, 20);
            if (s <= 50) return ("Б50", 38000, 21);
            return ("Б60", 39500, 22);
        }

        private List<PrssmProfilePoint> BuildProfilePoints(
            List<(double X, double Y)> pts)
        {
            var r = new List<PrssmProfilePoint>();
            for (int i = 0; i < pts.Count; i++)
            {
                bool ip = true;
                if (i > 0 && i < pts.Count - 1)
                {
                    var a = pts[i - 1]; var b = pts[i]; var c = pts[i + 1];
                    double cr = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    if (Math.Abs(cr) < 0.5) ip = false;
                }
                r.Add(new PrssmProfilePoint
                {
                    X = Math.Round(pts[i].X, 2),
                    Y = Math.Round(pts[i].Y, 2),
                    IsAnchor = true,
                    IsProfilePoint = ip
                });
            }
            return r;
        }

        private double EstRibWidth(List<(double X, double Y)> pts)
        {
            double mid = (pts.Min(p => p.Y) + pts.Max(p => p.Y)) / 2;
            var low = pts.Where(p => p.Y < mid).ToList();
            return low.Count < 2
                ? pts.Max(p => p.X) - pts.Min(p => p.X)
                : low.Max(p => p.X) - low.Min(p => p.X);
        }

        private (double, int) EstDiamCount(double a)
        {
            int[] ds = { 6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32, 36, 40 };
            double am = a * 100;
            foreach (var d in ds.Reverse())
            {
                double ba = Math.PI * d * d / 4;
                int n = (int)Math.Round(am / ba);
                if (n >= 1 && n <= 10) return (d, n);
            }
            return (16, Math.Max(1, (int)Math.Round(am / (Math.PI * 256 / 4))));
        }

        private double EstStirrupDiam(double a)
        {
            double am = a * 100 / 2;
            foreach (var d in new[] { 6, 8, 10, 12, 14 })
                if (Math.PI * d * d / 4 >= am * 0.8) return d;
            return 10;
        }
    }
}